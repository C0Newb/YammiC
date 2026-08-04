package co.cnewb.yammic.ymcsdk.packets

import java.util.UUID

public class GattNotificationEvent(
    val uuid: UUID,
    val value: ByteArray,
)
