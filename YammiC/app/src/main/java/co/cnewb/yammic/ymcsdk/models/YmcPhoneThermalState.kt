package co.cnewb.yammic.ymcsdk.models

enum class YmcPhoneThermalState(
    val wireValue: Byte,
) {
    Low(0),
    Middle(1),
    High(2),
}
