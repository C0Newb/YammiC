using CCU_Emulator.Protocol;
using CCU_Emulator.Tests.Support;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Emulation;

public class CcuSessionButtonsTests
{
    private readonly SessionFixture _f = new();

    [Theory]
    [InlineData(MusicAction.Next, "011701")]
    [InlineData(MusicAction.Previous, "011702")]
    [InlineData(MusicAction.Play, "011703")]
    [InlineData(MusicAction.Pause, "011704")]
    public void MusicButtons(MusicAction action, string expected)
    {
        _f.Session.Press(action);
        Assert.Equal(H(expected), Assert.Single(_f.Transport.MainRx));
    }

    [Theory]
    [InlineData(VolumeAction.PhoneUp, "011D01")]
    [InlineData(VolumeAction.PhoneDown, "011D02")]
    [InlineData(VolumeAction.MuteMicrophone, "011D03")]
    [InlineData(VolumeAction.MuteSpeaker, "011D04")]
    [InlineData(VolumeAction.MediaUp, "011D05")]
    [InlineData(VolumeAction.MediaDown, "011D06")]
    public void VolumeButtons(VolumeAction action, string expected)
    {
        _f.Session.Press(action);
        Assert.Equal(H(expected), Assert.Single(_f.Transport.MainRx));
    }

    [Fact]
    public void CallButtonsAreIgnoredWithNoCall()
    {
        Assert.False(_f.Session.Press(CallAction.Answer));
        Assert.False(_f.Session.Press(CallAction.Reject));
        Assert.False(_f.Session.Press(CallAction.HangUp));
        Assert.Empty(_f.Transport.MainRx);
    }

    [Fact]
    public void RingingCallCanBeAnsweredRejectedOrHungUp()
    {
        _f.Phone.SendDdt(Cat(H("0118"), Str8("Alice"), Str8("555"), H("00")));
        Assert.True(_f.Session.Press(CallAction.Answer));
        Assert.True(_f.Session.Press(CallAction.Reject));
        Assert.True(_f.Session.Press(CallAction.HangUp));
        Assert.Equal([H("011901"), H("011900"), H("011902")], _f.Transport.MainRx);
    }

    [Fact]
    public void ActiveCallAllowsOnlyHangUp()
    {
        _f.Phone.WriteSimple(H("0134 01"));
        Assert.False(_f.Session.Press(CallAction.Answer));
        Assert.False(_f.Session.Press(CallAction.Reject));
        Assert.True(_f.Session.Press(CallAction.HangUp));
        Assert.Equal([H("011902")], _f.Transport.MainRx);
    }

    [Fact]
    public void DismissingANotificationNotifiesThePhoneAndRemovesItLocally()
    {
        var proto = Cat(H("18 07 5A"), Str8("Hi"));
        _f.Phone.SendDdt(Cat(new PacketWriter().U16(0x012F).U24(proto.Length).ToArray(), proto));
        Assert.Single(_f.Session.State.Notifications);

        _f.Session.DismissNotification(7);
        Assert.Equal(H("0013 00000007"), Assert.Single(_f.Transport.MainRx));
        Assert.Empty(_f.Session.State.Notifications);
    }

    [Fact]
    public async Task DropConnectionDisconnectsTheTransport()
    {
        await _f.Session.DropConnectionAsync();
        Assert.Equal(1, _f.Transport.DisconnectCalls);
    }
}
