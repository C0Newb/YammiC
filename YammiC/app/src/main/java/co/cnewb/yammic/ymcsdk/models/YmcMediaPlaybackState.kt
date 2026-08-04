package co.cnewb.yammic.ymcsdk.models

enum class YmcMediaPlaybackState(
    val value: Byte,
) {
    Paused(0x01),
    Playing(0x02),
    Rewinding(0x03),
    FastForwarding(0x04),
    MediaDeviceUnavailable(-1),
}
