using CCU_Emulator.Emulation;
using CCU_Emulator.ViewModels;
using Xunit;

namespace CCU_Emulator.Tests.ViewModels;

public class LogViewModelTests
{
    [Fact]
    public void ShowsAddedEntriesAndFiltersCaseInsensitively()
    {
        var log = new PacketLog();
        var vm = new LogViewModel(log);

        log.Add(Direction.Rx, "DDT", "MusicMetaData", [1, 2], "Song = Hello");
        log.Add(Direction.Tx, "MainRx", "LanguageRequest", [0, 0x0B]);
        Assert.Equal(2, vm.Visible.Count);

        vm.FilterText = "hello";
        Assert.Single(vm.Visible);
        Assert.Equal("MusicMetaData", vm.Visible[0].Name);

        log.Add(Direction.Rx, "DDT", "Other", [3], "no match");
        Assert.Single(vm.Visible);
        log.Add(Direction.Rx, "DDT", "Another", [3], "HELLO again");
        Assert.Equal(2, vm.Visible.Count);

        vm.FilterText = "";
        Assert.Equal(4, vm.Visible.Count);
    }

    [Fact]
    public void ClearEmptiesBothLists()
    {
        var log = new PacketLog();
        var vm = new LogViewModel(log);
        log.Info("Host", "started");
        vm.ClearCommand.Execute(null);
        Assert.Empty(log.Entries);
        Assert.Empty(vm.Visible);
    }

    [Fact]
    public void ExportTextHasOneLinePerVisibleEntry()
    {
        var log = new PacketLog();
        var vm = new LogViewModel(log);
        log.Add(Direction.Tx, "MainRx", "Ping", [1, 0x35, 0]);
        log.Info("Host", "note");
        var lines = vm.ExportText().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("01 35 00", lines[0]);
    }

    [Fact]
    public void TrimmedEntriesDisappearFromTheVisibleList()
    {
        var log = new PacketLog();
        var vm = new LogViewModel(log);
        for (var i = 0; i < PacketLog.Capacity + 5; i++) log.Info("t", i.ToString());
        Assert.Equal(PacketLog.Capacity, log.Entries.Count);
        Assert.Equal(PacketLog.Capacity, vm.Visible.Count);
    }
}
