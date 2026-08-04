package co.cnewb.yammic.ymcsdk.models

enum class YmcWeatherRequestType(
    val code: Byte,
) {
    Current(0x01),
    Point(0x02),
}
