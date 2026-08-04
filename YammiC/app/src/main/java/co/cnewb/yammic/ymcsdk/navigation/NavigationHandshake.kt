package co.cnewb.yammic.ymcsdk.navigation

import co.cnewb.yammic.logging.Logger
import co.cnewb.yammic.ymcsdk.navigation.protocol.NAVILITE_FRAME_TYPE_PHONE
import co.cnewb.yammic.ymcsdk.navigation.protocol.NAVILITE_PDT_POINTER
import co.cnewb.yammic.ymcsdk.navigation.protocol.NAVILITE_PDT_VALUE
import co.cnewb.yammic.ymcsdk.navigation.protocol.NaviLiteCodec
import co.cnewb.yammic.ymcsdk.navigation.protocol.NaviLiteAuth
import co.cnewb.yammic.ymcsdk.navigation.protocol.NaviLiteServiceType

/**
 * NaviLite connection handshake orchestrator.
 *
 * Implements the 4-step handshake sequence required to authenticate with the Yamaha
 * CCU (Connected Control Unit) over the NaviLite protocol:
 *
 *   1. ESN_UPDATE → ESN_ACK
 *      The CCU announces its ESN (serial number); we acknowledge.
 *
 *   2. AUTH_REQUEST
 *      Phone initiates authentication.
 *
 *   3. SEC_DATA → SEC_DATA_ACK
 *      CCU sends obfuscated authentication data (part number + nonce XOR 0x0a);
 *      phone de-obfuscates, extracts the 4-byte nonce, and echoes it back.
 *
 *   4. Setup burst
 *      Phone sends a series of initial status frames to populate the CCU's display
 *      (NAV_STATUS, DAY_NIGHT, HOME, OFFICE, GPS, etc.).
 *
 * Ported from Pillion's Handshake.perform(): while awaiting ESN_UPDATE/SEC_DATA, unrelated
 * frames the CCU sends in between (e.g. dash button-press requests) are skipped rather than
 * treated as a protocol error. No I/O timeouts are imposed; blocking reads on real channels
 * are expected.
 */
class NavigationHandshake(
    private val channel: NavByteChannel,
    logger: Logger = Logger(tag = "NavigationHandshake", sinks = emptyList()),
) {
    private val log = logger.section("NavigationHandshake")

    /** Exposed so callers can keep reading off the same buffered stream after the handshake
     *  completes (e.g. to drain IMAGE_ACK frames) without losing bytes already read into it. */
    val reader = NavFrameReader(channel)

    /**
     * Execute the full handshake sequence.
     * Blocks on channel I/O until handshake completes or fails.
     */
    fun execute() {
        log.debug("waiting for ESN_UPDATE")
        // Step 1: Receive ESN_UPDATE and respond with ESN_ACK
        handleEsnUpdate()
        sendEsnAck()

        // Step 2: Send AUTH_REQUEST
        sendAuthRequest()

        log.debug("waiting for SEC_DATA")
        // Step 3: Receive SEC_DATA and respond with SEC_DATA_ACK
        receiveSecDataAndRespond()

        // Step 4: Send setup burst
        sendSetupBurst()
        log.debug("setup burst sent, authenticated")
    }

    /**
     * Waits for ESN_UPDATE, skipping any unrelated frames the CCU sends first.
     * Returns true once received; throws only if the channel closes before it arrives.
     */
    fun handleEsnUpdate(): Boolean {
        val frame = awaitService(NaviLiteServiceType.ESN_UPDATE)
        log.debug("ESN_UPDATE received (payload ${frame.payload.size}B)")
        return true
    }

    /**
     * Send ESN_ACK to the CCU. Payload is a fixed 2-byte value (`01 00`), matching the
     * reference SDK — an empty payload is silently ignored by the CCU.
     */
    fun sendEsnAck() {
        val frame = NaviLiteCodec.build(
            frameType = NAVILITE_FRAME_TYPE_PHONE,
            serviceType = NaviLiteServiceType.ESN_ACK,
            payloadDataType = NAVILITE_PDT_VALUE,
            payload = byteArrayOf(1, 0),
        )
        channel.write(frame)
        log.debug("sent ESN_ACK")
    }

    /**
     * Send AUTH_REQUEST to initiate authentication with the CCU. Payload encodes the app
     * version (1820 / 0x071c) the reference SDK sends; the CCU does not proceed to SEC_DATA
     * for a request with an empty/malformed payload.
     */
    fun sendAuthRequest() {
        val frame = NaviLiteCodec.build(
            frameType = NAVILITE_FRAME_TYPE_PHONE,
            serviceType = NaviLiteServiceType.AUTH_REQUEST,
            payloadDataType = NAVILITE_PDT_POINTER,
            payload = AUTH_REQUEST_PAYLOAD,
        )
        channel.write(frame)
        log.debug("sent AUTH_REQUEST")
    }

    /**
     * Receive SEC_DATA from the CCU (skipping any unrelated frames first) and respond with
     * SEC_DATA_ACK.
     *
     * The SEC_DATA payload contains the CCU's part number and a 4-byte nonce,
     * all XOR-ed with 0x0a for obfuscation. We de-obfuscate, extract the nonce,
     * and send it back in the SEC_DATA_ACK frame.
     */
    fun receiveSecDataAndRespond() {
        val frame = awaitService(NaviLiteServiceType.SEC_DATA)
        // The part number also tells us the CCU model (and therefore its display resolution —
        // see Pillion's "Vehicle Model Detection"), useful for diagnosing model-specific issues.
        log.debug("SEC_DATA received: partNumber=${runCatching { NaviLiteAuth.partNumber(frame.payload) }.getOrDefault("?")}")

        // Extract the nonce and send it back
        val nonce = NaviLiteAuth.secDataAckPayload(frame.payload)
        val ackFrame = NaviLiteCodec.build(
            frameType = NAVILITE_FRAME_TYPE_PHONE,
            serviceType = NaviLiteServiceType.SEC_DATA_ACK,
            payloadDataType = NAVILITE_PDT_POINTER,
            payload = nonce,
        )
        channel.write(ackFrame)
        log.debug("sent SEC_DATA_ACK")
    }

    /**
     * Send the post-authentication setup burst: the values the reference SDK sends before
     * the CCU accepts IMAGE frames. GPS and APP_SETTING are sent twice (once with initial
     * values, again with final ones), matching the reference SDK exactly.
     */
    fun sendSetupBurst() {
        for ((serviceType, payloadDataType, payload) in SETUP_BURST) {
            val frame = NaviLiteCodec.build(
                frameType = NAVILITE_FRAME_TYPE_PHONE,
                serviceType = serviceType,
                payloadDataType = payloadDataType,
                payload = payload,
            )
            channel.write(frame)
            log.debug("setup burst: sent serviceType=$serviceType (${payload.size}B)")
        }
    }

    /** Reads frames until one matches [serviceType], skipping any that don't. */
    private fun awaitService(serviceType: Int): NavFrameView {
        var frame = reader.next()
        while (frame.serviceType != serviceType) {
            log.debug("awaitService($serviceType): skipping frame serviceType=${frame.serviceType} (${frame.payload.size}B)")
            frame = reader.next()
        }
        return frame
    }

    private companion object {
        // appVersion 1820 (0x071c) + extra, matching the reference SDK's AUTH_REQUEST payload.
        val AUTH_REQUEST_PAYLOAD = byteArrayOf(0x1c, 0x07, 0, 1, 0, 0, 0, 0)

        // (serviceType, payloadDataType, payload) — the post-auth setup the CCU expects before
        // it renders images, ported from the reference SDK's setup burst.
        val SETUP_BURST: List<Triple<Int, Int, ByteArray>> = listOf(
            Triple(NaviLiteServiceType.NAV_STATUS, NAVILITE_PDT_VALUE, byteArrayOf(0, 0)),
            Triple(NaviLiteServiceType.DAY_NIGHT, NAVILITE_PDT_VALUE, byteArrayOf(1, 0)),
            // 1-byte payload confirmed from StreetCross's native GetHomeSettingUpdateMessage/
            // GetOfficeSettingUpdateMessage (libnaviliteprotocol.so): the packer only ever
            // pushes a single `isSet & 1` byte, not the 2-byte "00 00" Pillion's docs guessed at
            // — 0x00 = not configured, 0x01 = configured. Claiming "configured" (0x01) without
            // real saved home/office data made the CCU error out during nav init and disable
            // nav, so this reports "not configured" (0x00) until real address data is wired up.
            Triple(NaviLiteServiceType.HOME, NAVILITE_PDT_VALUE, byteArrayOf(0)),
            Triple(NaviLiteServiceType.OFFICE, NAVILITE_PDT_VALUE, byteArrayOf(0)),
            Triple(NaviLiteServiceType.GPS, NAVILITE_PDT_VALUE, byteArrayOf(1, 0)),
            Triple(NaviLiteServiceType.APP_SETTING, NAVILITE_PDT_VALUE, byteArrayOf(0, 0)),
            Triple(
                NaviLiteServiceType.ZOOM,
                NAVILITE_PDT_POINTER,
                byteArrayOf(0x07, 0x19, 0x06, 0x00, 0x30, 0x2e, 0x32, 0x20, 0x6d, 0x69),
            ),
            Triple(NaviLiteServiceType.ROAD, NAVILITE_PDT_POINTER, ByteArray(0)),
            Triple(
                NaviLiteServiceType.SPEED_LIMIT,
                NAVILITE_PDT_POINTER,
                byteArrayOf(0, 0, 0, 0, 0x03, 0x6d, 0x70, 0x68),
            ),
            Triple(NaviLiteServiceType.GPS, NAVILITE_PDT_VALUE, byteArrayOf(1, 0)),
            Triple(NaviLiteServiceType.APP_SETTING, NAVILITE_PDT_VALUE, byteArrayOf(1, 0)),
        )
    }
}
