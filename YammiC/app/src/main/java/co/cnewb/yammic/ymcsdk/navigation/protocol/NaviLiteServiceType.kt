package co.cnewb.yammic.ymcsdk.navigation.protocol

/** NaviLite frameType for messages the phone sends to the bike. */
internal const val NAVILITE_FRAME_TYPE_PHONE: Int = 6

/** NaviLite payloadDataType values. */
internal const val NAVILITE_PDT_VALUE: Int = 0
internal const val NAVILITE_PDT_POINTER: Int = 1

/** NaviLite service-type identifiers used by the handshake and image channel. */
internal object NaviLiteServiceType {
    // handshake
    const val ESN_UPDATE = 66
    const val ESN_ACK = 81
    const val AUTH_REQUEST = 33
    const val SEC_DATA = 83
    const val SEC_DATA_ACK = 84
    // image channel
    const val IMAGE = 0
    const val IMAGE_ACK = 80
    // post-auth setup burst
    const val NAV_STATUS = 2
    const val DAY_NIGHT = 31
    const val HOME = 10
    const val OFFICE = 11
    const val GPS = 13
    const val APP_SETTING = 12
    const val ZOOM = 14
    const val ROAD = 3
    const val SPEED_LIMIT = 17
    // dash -> phone: fired when the rider taps "Go home" / "Go to work" on the CCU
    const val GO_HOME_REQUEST = 53
    const val GO_OFFICE_REQUEST = 54
    // dash -> phone: fired when the rider opens/leaves a content screen (e.g. Favorites,
    // Nearby gas stations) — payload is [contentType][0x00], contentType 3=favorites, 4=gas.
    const val START_CONTENT_UPDATE_REQUEST = 55
    const val STOP_CONTENT_UPDATE_REQUEST = 56
    // phone -> dash: list-size replies to START_CONTENT_UPDATE_REQUEST. Payload confirmed from
    // StreetCross's native GetFavoritePoiUpdateMessage/GetGasPoiUpdateMessage
    // (libnaviliteprotocol.so): [itemCount][0x00].
    const val FAV_POI_LIST_UPDATE = 7
    const val GAS_POI_LIST_UPDATE = 8
    // phone -> dash: one per list item, sent after FAV_POI_LIST_UPDATE/GAS_POI_LIST_UPDATE.
    // Payload confirmed from StreetCross's native GetFavoritePoiDataUpdateMessage/
    // GetGasPoiDataUpdateMessage (libnaviliteprotocol.so) — see NaviLitePoiEntry.
    const val FAV_POI_LIST_DATA_UPDATE = 98
    const val GAS_POI_LIST_DATA_UPDATE = 99
}

/** APP_START_CONTENT_UPDATE_REQUEST/APP_STOP_CONTENT_UPDATE_REQUEST contentType values. */
internal object NaviLiteContentType {
    const val NAVI_IMAGE = 1
    const val FAVORITE_LIST = 3
    const val STATIONS_LIST = 4
}

/** FAV/GAS_POI_LIST_DATA_UPDATE's direction byte — relative to the vehicle, not compass. */
internal object NaviLitePoiDirectionType {
    const val UNDEFINED: Byte = 0
    const val FRONT: Byte = 1
    const val FRONT_RIGHT: Byte = 2
    const val RIGHT: Byte = 3
    const val BACK_RIGHT: Byte = 4
    const val BACK: Byte = 5
    const val BACK_LEFT: Byte = 6
    const val LEFT: Byte = 7
    const val FRONT_LEFT: Byte = 8
}

/**
 * One FAV_POI_LIST_DATA_UPDATE/GAS_POI_LIST_DATA_UPDATE entry.
 *
 * Wire format reverse-engineered from StreetCross's native
 * GetFavoritePoiDataUpdateMessage(short, short, signed char, float, string, string) /
 * GetGasPoiDataUpdateMessage(...) in libnaviliteprotocol.so — every field (including the
 * `float` distance) collapses to a single byte in the actual packer, so distance is quantized,
 * not a full IEEE-754 float:
 *
 * `[index][unknown][direction][len(name)][len(road)][distance][road bytes][name bytes]`
 *
 * The two length-prefix bytes are written in (name, road) order, but the string bytes
 * themselves are appended in (road, name) order — a genuine quirk of the native packer, not a
 * transcription error; confirmed by reading the disassembly's push order directly.
 */
internal data class NaviLitePoiEntry(
    val index: Int,
    val direction: Byte,
    val distance: Int,
    val name: String,
    val road: String,
) {
    fun toPayload(): ByteArray {
        val nameBytes = name.toByteArray(Charsets.US_ASCII)
        val roadBytes = road.toByteArray(Charsets.US_ASCII)
        val payload = ByteArray(6 + roadBytes.size + nameBytes.size)
        payload[0] = index.toByte()
        payload[1] = 0
        payload[2] = direction
        payload[3] = nameBytes.size.coerceIn(0, 144).toByte()
        payload[4] = roadBytes.size.coerceIn(0, 144).toByte()
        payload[5] = distance.coerceIn(0, 255).toByte()
        roadBytes.copyInto(payload, 6)
        nameBytes.copyInto(payload, 6 + roadBytes.size)
        return payload
    }
}
