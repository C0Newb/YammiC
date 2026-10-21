namespace CNCO.YccuSdk.Navigation.Protocol;

/// <summary>
/// NaviLite authentication - universal, no per-bike key.
///
/// The CCU's SEC_DATA payload is <c>("part-number" + nonce) XOR 0x0a</c>. Authenticating
/// means de-obfuscating it and echoing the 4-byte nonce back in SEC_DATA_ACK.
/// Ported from the Pillion reference app's Auth.kt. Pure transform, no I/O.
/// </summary>
public static class NaviLiteAuth
{
    private const int Obfuscation = 0x0a;
    private const int NonceLen = 4;

    /// <summary>
    /// The SEC_DATA_ACK payload = the de-obfuscated 4-byte nonce (last 4 bytes XOR 0x0a).
    /// </summary>
    public static byte[] SecDataAckPayload(byte[] secDataPayload)
    {
        var nonce = new byte[NonceLen];
        for (var index = 0; index < NonceLen; index++)
        {
            nonce[index] = (byte)(secDataPayload[secDataPayload.Length - NonceLen + index] ^ Obfuscation);
        }
        return nonce;
    }

    /// <summary>
    /// The CCU part number the bike announces (de-obfuscated string before the nonce).
    /// </summary>
    public static string PartNumber(byte[] secDataPayload)
    {
        var partNumberLength = secDataPayload.Length - NonceLen;
        var chars = new char[partNumberLength];
        for (var index = 0; index < partNumberLength; index++)
        {
            chars[index] = (char)(secDataPayload[index] ^ Obfuscation);
        }
        return new string(chars);
    }
}
