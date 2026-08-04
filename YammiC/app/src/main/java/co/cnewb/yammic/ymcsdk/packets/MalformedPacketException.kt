package co.cnewb.yammic.ymcsdk.packets

class MalformedPacketException : Exception {
    constructor(commandCode: YmcCommandCode) : super("Received an invalid packet for $commandCode.") {}
    constructor(message: String) : super(message)
}
