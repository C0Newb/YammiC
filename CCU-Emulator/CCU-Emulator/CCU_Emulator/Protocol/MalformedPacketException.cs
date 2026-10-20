namespace CCU_Emulator.Protocol;

/// <summary>A packet was shorter than its layout requires, or otherwise undecodable.</summary>
public sealed class MalformedPacketException(string message) : Exception(message);
