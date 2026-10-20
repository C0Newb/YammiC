#:project ../../CCU-Emulator/CCU-Emulator/CCU_Emulator/CCU_Emulator.csproj

// Usage:  dotnet run tools/PhoneSim/PhoneSim.cs -- <ccuId 14> <passKey 6> [port=48800] [image.jpg]
// Plays a phone against the emulator's loopback transport and prints every frame the CCU sends back.

using System.Net.Sockets;
using System.Text;
using CCU_Emulator.Protocol;

if (args.Length < 2) { Console.Error.WriteLine("usage: PhoneSim <ccuId> <passKey> [port] [image.jpg]"); return 1; }
var ccuId = args[0];
var passKey = args[1];
var port = args.Length > 2 ? int.Parse(args[2]) : 48800;
var imagePath = args.Length > 3 ? args[3] : null;

using var client = new TcpClient();
await client.ConnectAsync("127.0.0.1", port);
var stream = client.GetStream();
byte requestId = 1;
Console.WriteLine($"connected to 127.0.0.1:{port}");

// CCU → phone frames, printed as they arrive.
_ = Task.Run(async () =>
{
    var header = new byte[3];
    try
    {
        while (true)
        {
            await stream.ReadExactlyAsync(header);
            var data = new byte[(header[1] << 8) | header[2]];
            await stream.ReadExactlyAsync(data);
            Console.WriteLine($"  CCU → phone  role {header[0]}  {Convert.ToHexString(data)}");
        }
    }
    catch (Exception) { Console.WriteLine("  (connection closed)"); }
});

async Task WriteFrame(byte role, byte[] data)
{
    var frame = new byte[3 + data.Length];
    frame[0] = role; frame[1] = (byte)(data.Length >> 8); frame[2] = (byte)data.Length;
    data.CopyTo(frame, 3);
    await stream.WriteAsync(frame);
}

Task Simple(byte[] packet) { Console.WriteLine($"phone → CCU  simple  {Convert.ToHexString(packet)}"); return WriteFrame(1, packet); }

async Task Ddt(byte[] message)
{
    Console.WriteLine($"phone → CCU  ddt     {Convert.ToHexString(message)}");
    var id = requestId++;
    await WriteFrame(3, [id, (byte)(message.Length >> 16), (byte)(message.Length >> 8), (byte)message.Length]);
    for (var offset = 0; offset < message.Length; offset += 160)
    {
        var n = Math.Min(160, message.Length - offset);
        var chunk = new byte[4 + n];
        chunk[0] = id; chunk[1] = (byte)(offset >> 16); chunk[2] = (byte)(offset >> 8); chunk[3] = (byte)offset;
        Array.Copy(message, offset, chunk, 4, n);
        await WriteFrame(5, chunk);
    }
}

byte[] Str8(string s) { var b = Encoding.UTF8.GetBytes(s); return [(byte)b.Length, .. b]; }
async Task Pause(int ms = 600) => await Task.Delay(ms);

await Ddt(new PacketWriter().U16(0x0101).Utf8(ccuId).Utf8(passKey).ToArray());       // auth
await Pause();
await Simple(new PacketWriter().U16(0x0002).U8(1).I32(0x1234).ToArray());            // identify
await Simple(new PacketWriter().U16(0x000C).U8(2).U8(0).ToArray());                  // language: English
var phoneTime = DateTimeOffset.Now.ToUnixTimeSeconds() + (long)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalSeconds;
await Simple(new PacketWriter().U16(0x000A).U8(3).I32((int)(phoneTime >> 32)).I32((int)phoneTime).ToArray());   // device time
await Simple(new PacketWriter().U16(0x013C).U8(0xE4).U8(0xB2).U8(0xC8).U8(0x47).ToArray());   // phone state
await Pause();

await Ddt([.. new PacketWriter().U16(0x0116).ToArray(), .. Str8("Test Artist"), .. Str8("Test Album"), .. Str8("Test Song"),
           .. new PacketWriter().U24(210).U8(2).U16(10).U24(15).ToArray()]);          // music, playing, 210 s, 15 s in
await Simple([.. new PacketWriter().U16(0x0004).U8(4).ToArray(), .. Encoding.UTF8.GetBytes("Berlin")]);   // weather location
await Simple(new PacketWriter().U16(0x0005).U8(5).U8(0).U8(3).U16(125).U16(140).U16(90).U8(8).ToArray());  // current: rain 12.5°C, 40%
await Pause();

var proto = new PacketWriter().U8(0x18).U8(1).U8(0x20).U8(0).U8(0x5A).U8(5).Utf8("Hello").U8(0x6A).U8(12).Utf8("From PhoneSim").ToArray();
await Ddt([.. new PacketWriter().U16(0x012F).U24(proto.Length).ToArray(), .. proto]);   // notification
await Pause(2000);

await Ddt([.. new PacketWriter().U16(0x0118).ToArray(), .. Str8("Alice"), .. Str8("+15551234"), 0]);   // incoming call
await Pause(3000);

if (imagePath is not null)
{
    var image = File.ReadAllBytes(imagePath);
    await Simple(new PacketWriter().U16(0x0231).ToArray());                                  // VPB request
    await Pause();
    const int chunkSize = 6144;
    var chunks = (image.Length + chunkSize - 1) / chunkSize;
    for (var i = 0; i < chunks; i++)
    {
        var data = image[(i * chunkSize)..Math.Min(image.Length, (i + 1) * chunkSize)];
        var payload = new PacketWriter().U16(0x0235).I32(20101).U8(i).I32(data.Length).U8(chunks - 1).I32(image.Length)
            .U32(VpbCrc32.Compute(data)).Bytes(data).ToArray();
        await WriteFrame(6, SppFrame.Encode(payload));
        Console.WriteLine($"phone → CCU  spp     chunk {i + 1}/{chunks} ({data.Length} bytes)");
        await Pause(300);
    }
}

Console.WriteLine("script finished; waiting 15 s for CCU traffic (pings), Ctrl+C to quit");
await Task.Delay(15000);
return 0;
