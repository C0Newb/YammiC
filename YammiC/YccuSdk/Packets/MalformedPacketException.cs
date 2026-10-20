namespace CNCO.YccuSdk.Packets;

public class MalformedPacketException : Exception
{
    public MalformedPacketException(YmcCommandCode commandCode)
        : base($"Received an invalid packet for {commandCode}.")
    {
    }

    public MalformedPacketException(string message)
        : base(message)
    {
    }
}
