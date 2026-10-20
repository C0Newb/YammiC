using CCU_Emulator.Protocol;
using CCU_Emulator.Transport;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Support;

/// <summary>Plays the phone side against a <see cref="FakeTransport"/>.</summary>
internal sealed class PhoneSimulator(FakeTransport transport)
{
    private byte _requestId = 1;

    public void Connect() => transport.RaiseBle(true);

    public void WriteSimple(byte[] packet) => transport.RaiseData(CcuChannel.SimpleWrite, packet);

    /// <summary>Sends a message through a full DDT session, in chunks of <paramref name="chunkSize"/> bytes.</summary>
    public void SendDdt(byte[] message, int chunkSize = 20)
    {
        var id = _requestId++;
        transport.RaiseData(CcuChannel.DdtControl, [id, (byte)(message.Length >> 16), (byte)(message.Length >> 8), (byte)message.Length]);
        for (var offset = 0; offset < message.Length; offset += chunkSize)
        {
            var n = Math.Min(chunkSize, message.Length - offset);
            var chunk = new byte[4 + n];
            chunk[0] = id;
            chunk[1] = (byte)(offset >> 16);
            chunk[2] = (byte)(offset >> 8);
            chunk[3] = (byte)offset;
            Array.Copy(message, offset, chunk, 4, n);
            transport.RaiseData(CcuChannel.DdtData, chunk);
        }
    }

    public void Authenticate(string ccuId, string passKey) => SendDdt(Cat(H("0101"), Ascii(ccuId), Ascii(passKey)));

    /// <summary>Sends one SPP-framed payload.</summary>
    public void SendSpp(byte[] payload) => transport.RaiseData(CcuChannel.Spp, SppFrame.Encode(payload));
}
