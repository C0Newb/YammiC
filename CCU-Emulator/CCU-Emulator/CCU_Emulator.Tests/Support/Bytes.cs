using System.Text;

namespace CCU_Emulator.Tests.Support;

internal static class Bytes
{
    /// <summary>Hex string (spaces allowed) to bytes.</summary>
    public static byte[] H(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    /// <summary>Length-prefixed (1 byte) UTF-8 string.</summary>
    public static byte[] Str8(string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        return [(byte)b.Length, .. b];
    }

    public static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    public static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
}
