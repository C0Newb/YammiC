using CCU_Emulator.Emulation;
using Xunit;

namespace CCU_Emulator.Tests.Emulation;

public class CcuSettingsTests
{
    [Fact]
    public void AdvertisedNameIsNineteenCharacters()
        => Assert.Equal("YCCU_00000000000000", new CcuSettings().AdvertisedName);

    [Fact]
    public void ValidateFlagsBadIdentity()
    {
        var s = new CcuSettings { CcuId = "123", PassKey = "12345678" };
        var errors = s.Validate();
        Assert.Contains(errors, e => e.Contains("CcuId"));
        Assert.Contains(errors, e => e.Contains("PassKey"));
        Assert.Empty(new CcuSettings().Validate());
    }

    [Fact]
    public void LoadCreatesDefaultsWhenMissingAndRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ccu-test-{Guid.NewGuid():N}", "emulator.settings.json");
        try
        {
            var created = SettingsStore.Load(path);
            Assert.True(File.Exists(path));
            Assert.True(created.Behavior.SendPing);

            created.CcuId = "01234567890123";
            created.Behavior.PingIntervalSeconds = 3;
            created.Transport = TransportKind.WindowsBle;
            SettingsStore.Save(path, created);

            var loaded = SettingsStore.Load(path);
            Assert.Equal("01234567890123", loaded.CcuId);
            Assert.Equal(3, loaded.Behavior.PingIntervalSeconds);
            Assert.Equal(TransportKind.WindowsBle, loaded.Transport);
            Assert.Contains("\"WindowsBle\"", File.ReadAllText(path)); // enums are written as names
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }
}
