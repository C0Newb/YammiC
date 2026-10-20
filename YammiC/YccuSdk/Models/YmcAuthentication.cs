namespace CNCO.YccuSdk.Models;

/// <summary>
/// Authentication data
/// </summary>
public class YmcAuthentication
{
    public YmcAuthentication(string ccuId, string passKey)
    {
        CcuId = ccuId;
        PassKey = passKey;
    }

    public string CcuId { get; }
    public string PassKey { get; }
}
