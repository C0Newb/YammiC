package co.cnewb.yammic.ymcsdk.models

enum class YmcCellSignalLevel(
    val wireValue: Byte,
) {
    NoConnection(0),
    NoSignal(1),
    Lv1(2),
    Lv2(3),
    Lv3(4),
    Lv4(5),
}
