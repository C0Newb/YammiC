namespace CCU_Emulator.Transport;

/// <summary>The byte pipes between phone and CCU. Values are also the loopback frame role byte.</summary>
public enum CcuChannel : byte
{
    /// <summary>Phone writes (simple write characteristic B606C7F9).</summary>
    SimpleWrite = 1,
    /// <summary>CCU notifications (main RX characteristic 9C810D26).</summary>
    MainRx = 2,
    /// <summary>Phone writes (DDT control characteristic 56851826).</summary>
    DdtControl = 3,
    /// <summary>CCU notifications (DDT status characteristic 75AB2788).</summary>
    DdtStatus = 4,
    /// <summary>Phone writes (DDT data characteristic E998AEAB).</summary>
    DdtData = 5,
    /// <summary>Raw bytes on the Classic SPP socket, both directions.</summary>
    Spp = 6,
}
