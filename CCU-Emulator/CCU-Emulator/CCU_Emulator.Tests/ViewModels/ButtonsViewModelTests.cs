using CCU_Emulator.Tests.Support;
using CCU_Emulator.ViewModels;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.ViewModels;

public class ButtonsViewModelTests
{
    private readonly SessionFixture _f = new();

    [Fact]
    public void MusicAndVolumeCommandsSendTheirPackets()
    {
        var vm = new ButtonsViewModel(_f.Session);
        vm.NextCommand.Execute(null);
        vm.PreviousCommand.Execute(null);
        vm.PlayCommand.Execute(null);
        vm.PauseCommand.Execute(null);
        vm.MediaVolumeUpCommand.Execute(null);
        vm.MuteSpeakerCommand.Execute(null);
        Assert.Equal(
            [H("011701"), H("011702"), H("011703"), H("011704"), H("011D05"), H("011D04")],
            _f.Transport.MainRx);
    }

    [Fact]
    public void CallButtonsFollowTheCallState()
    {
        var vm = new ButtonsViewModel(_f.Session);
        Assert.False(vm.CanAnswerOrReject);
        Assert.False(vm.CanHangUp);

        _f.Phone.SendDdt(Cat(H("0118"), Str8("Alice"), Str8("555"), H("00")));
        Assert.True(vm.CanAnswerOrReject);
        Assert.True(vm.CanHangUp);

        vm.AnswerCommand.Execute(null);
        Assert.Equal(H("011901"), _f.Transport.MainRx[^1]);

        _f.Phone.WriteSimple(H("0134 01"));
        Assert.False(vm.CanAnswerOrReject);
        Assert.True(vm.CanHangUp);
    }
}
