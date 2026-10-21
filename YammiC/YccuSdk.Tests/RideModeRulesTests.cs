using CNCO.YccuSdk.Platform;
using CNCO.YccuSdk.Yrc;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class RideModeRulesTests
{
    private sealed class Store : IYmcKeyValueStore
    {
        private readonly Dictionary<string, string> _values = new();
        public string? GetString(string key) => _values.TryGetValue(key, out var v) ? v : null;
        public void SetString(string key, string value) => _values[key] = value;
    }

    private static YrcProfile Profile(int mode, string name, int current = 3) =>
        new(mode, 0, 2, name, new[] { new YrcValue(1, current, 1, 4), new YrcValue(2, current, 0, 9) });

    [Theory]
    [InlineData(28, "TRACK 1", true)]
    [InlineData(31, "TRACK 4", true)]
    [InlineData(19, "CUSTOM    ", true)]
    [InlineData(16, "SPORT", false)]
    [InlineData(17, "STREET", false)]
    [InlineData(18, "RAIN", false)]
    public void OnlyTrackAndCustomModesAreEditable(int mode, string name, bool editable) =>
        Assert.Equal(editable, YrcNames.IsEditable(Profile(mode, name)));

    [Fact]
    public void SendingToABuiltInMode_IsRefusedBeforeAnythingIsSent()
    {
        var bike = new[] { Profile(16, "SPORT"), Profile(28, "TRACK 1") };
        var problems = ProfileLibrary.CheckAgainstBike(Profile(16, "SPORT", 2), bike);
        Assert.Contains(problems, p => p.Contains("built-in"));
        Assert.Empty(ProfileLibrary.CheckAgainstBike(Profile(28, "TRACK 1", 2), bike));
    }

    [Fact]
    public void Update_ChangesNameAndValuesWithinTheBikesRange()
    {
        var library = new ProfileLibrary(new Store());
        var entry = library.Add(Profile(28, "TRACK 1"), "mine");

        Assert.True(library.Update(entry.Id, "renamed", "A VERY LONG NAME", new Dictionary<int, int> { [1] = 99, [2] = 5 }));

        var saved = library.Items.Single();
        Assert.Equal("renamed", saved.Label);
        Assert.Equal(10, saved.Name.Length);
        Assert.Equal(4, saved.Values.Single(v => v.Kind == 1).Current); // clamped to the bike's maximum
        Assert.Equal(5, saved.Values.Single(v => v.Kind == 2).Current);
        Assert.False(library.Update("missing", "x", "y", new Dictionary<int, int>()));
    }
}
