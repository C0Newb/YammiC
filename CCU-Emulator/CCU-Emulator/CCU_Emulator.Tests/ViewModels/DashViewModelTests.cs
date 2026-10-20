using CCU_Emulator.Emulation;
using CCU_Emulator.Protocol;
using CCU_Emulator.ViewModels;
using Xunit;

namespace CCU_Emulator.Tests.ViewModels;

public class DashViewModelTests
{
    private readonly CcuState _state = new();
    private DashViewModel Create() => new(_state);

    [Fact]
    public void StartsOnTheIdleScreen()
    {
        var vm = Create();
        Assert.True(vm.ShowIdle);
        Assert.False(vm.ShowCall);
        Assert.False(vm.ShowVpb);
        Assert.Equal("No music", vm.MusicTitle);
        Assert.Equal("No weather", vm.WeatherText);
        Assert.Equal("--:--", vm.ClockText);
    }

    [Fact]
    public void ScreenPriorityIsCallThenVpbThenIdle()
    {
        var vm = Create();
        _state.VpbImage = [1, 2, 3];
        Assert.True(vm.ShowVpb);
        Assert.False(vm.ShowIdle);

        _state.CallRinging = true;
        Assert.True(vm.ShowCall);
        Assert.False(vm.ShowVpb);

        _state.CallRinging = false;
        _state.CallActive = true;
        Assert.True(vm.ShowCall);

        _state.CallActive = false;
        _state.VpbImage = null;
        Assert.True(vm.ShowIdle);
    }

    [Fact]
    public void MusicAndWeatherAndClockText()
    {
        var vm = Create();
        _state.MusicSong = "Song";
        _state.MusicArtist = "Artist";
        _state.MusicAlbum = "Album";
        _state.MusicDurationSeconds = 200;
        _state.MusicElapsedSeconds = 50;
        _state.MusicPlayback = PlaybackState.Playing;
        _state.UpsertWeather(new WeatherEntry(WeatherSlot.Current, WeatherType.Rain, 12.5, 14, 9, 40));
        _state.PhoneTime = new DateTime(2026, 10, 10, 9, 5, 0);

        Assert.Equal("Song", vm.MusicTitle);
        Assert.Equal("Artist — Album", vm.MusicSubtitle);
        Assert.Equal(25.0, vm.MusicProgress);
        Assert.Equal("▶", vm.PlaybackText);
        Assert.Contains("12.5", vm.WeatherText);
        Assert.Contains("Rain", vm.WeatherText);
        Assert.Equal("09:05", vm.ClockText);
    }

    [Fact]
    public void ProgressIsClampedAndSafeWithNoDuration()
    {
        var vm = Create();
        _state.MusicDurationSeconds = 0;
        _state.MusicElapsedSeconds = 30;
        Assert.Equal(0, vm.MusicProgress);
        _state.MusicDurationSeconds = 10;
        Assert.Equal(100, vm.MusicProgress);
    }

    [Fact]
    public void ChangeNotificationsFireWhenStateChanges()
    {
        var vm = Create();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        _state.CallRinging = true;
        Assert.True(changed.Contains(nameof(DashViewModel.ShowCall)) || changed.Contains(string.Empty));
    }

    [Fact]
    public async Task BannerShowsForANewNotificationThenHides()
    {
        DashViewModel.BannerDuration = TimeSpan.FromMilliseconds(30);
        try
        {
            var vm = Create();
            _state.AddNotification(new NotificationEntry(1, 0, null, "Title", "", "Body"));
            Assert.True(vm.BannerVisible);
            Assert.Equal("Title", vm.BannerTitle);
            Assert.Equal("Body", vm.BannerBody);

            await Task.Delay(300);
            Assert.False(vm.BannerVisible);
        }
        finally
        {
            DashViewModel.BannerDuration = TimeSpan.FromSeconds(8);
        }
    }

    [Fact]
    public void BannerCanBeDismissed()
    {
        var vm = Create();
        _state.AddNotification(new NotificationEntry(1, 0, null, "T", "", "B"));
        vm.DismissBannerCommand.Execute(null);
        Assert.False(vm.BannerVisible);
    }
}
