using CNCO.YccuSdk.Packets.App;
using CNCO.YccuSdk.Yrc;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class YrcBikeRequestTests
{
    // What the bike sent for its import menu (from a device log): cmd 0x0205, length 8, request 2, then the protobuf.
    private static readonly byte[] BikeListRequest = Convert.FromHexString("0205000008" + "02" + "08021014180120" + "02");

    [Fact]
    public void TheBikesImportRequest_ParsesIntoModeTypeAndKinds()
    {
        var request = YrcCodec.ParseRequest(BikeListRequest.AsSpan(6));
        Assert.Equal(new[] { 2 }, request.DataKinds);
        Assert.Equal(new[] { 20 }, request.Modes); // MT5
        Assert.Equal(1, request.ModeType);
        Assert.Equal(2, request.KwStatusFlag);
        Assert.Empty(request.SavedNumbers);
    }

    [Fact]
    public void ALoadRequest_CarriesTheChosenSavedNumbers()
    {
        var proto = new ProtoWriter().Packed(1, new[] { 2, 4 }).Packed(2, new[] { 29 }).Varint(3, 4)
            .Bytes(5, new ProtoWriter().Varint(6, 3).ToArray()).Bytes(5, new ProtoWriter().Varint(6, 7).ToArray()).ToArray();
        var request = YrcCodec.ParseRequest(proto);
        Assert.Equal(new[] { 3, 7 }, request.SavedNumbers);
        Assert.Equal(new[] { 29 }, request.Modes);
        Assert.Equal(4, request.ModeType);
    }

    [Fact]
    public void TheSavedList_RoundTripsAndOnlyCarriesWhatWasAsked()
    {
        var profile = new YrcProfile(20, 3, 1, "CUSTOM", new[] { new YrcValue(1, 2, 0, 2), new YrcValue(2, 1, 1, 3) });

        var namesOnly = YrcCodec.ParseInfo(YrcCodec.BuildSavedList(new[] { YrcInfoKind.Names }, new[] { profile }));
        var entry = Assert.Single(namesOnly.Profiles);
        Assert.Equal((20, 3, 1, "CUSTOM"), (entry.Mode, entry.SavedNo, entry.ModeType, entry.Name));
        Assert.Empty(entry.Values);

        var withValues = YrcCodec.ParseInfo(YrcCodec.BuildSavedList(new[] { YrcInfoKind.Names, YrcInfoKind.Current }, new[] { profile }));
        Assert.Equal(new[] { (1, 2), (2, 1) }, withValues.Profiles[0].Values.Select(v => (v.Kind, v.Current)));
    }

    [Fact]
    public void ANothingToOfferAnswer_CarriesOnlyTheResult()
    {
        var info = YrcCodec.ParseInfo(YrcCodec.BuildSavedList(new[] { YrcInfoKind.Names }, Array.Empty<YrcProfile>(), YrcCodec.ResultNoDataForMode));
        Assert.Equal(YrcCodec.ResultNoDataForMode, info.Result);
        Assert.Empty(info.Profiles);
    }

    [Fact]
    public void ALoadTheBikeAskedFor_IsMarkedAsStartedByTheBike()
    {
        var profile = new YrcProfile(20, 1, 1, "CUSTOM", new[] { new YrcValue(1, 2, 0, 2) });
        var info = YrcCodec.ParseInfo(YrcCodec.BuildLoad(new[] { profile }, YrcCodec.StartedByBike));
        Assert.Equal(YrcCodec.StartedByBike, info.StartTrigger);
        Assert.Equal(YrcCodec.StartedByApp, YrcCodec.ParseInfo(YrcCodec.BuildLoad(new[] { profile })).StartTrigger);
    }

    [Fact]
    public void TheExportAnswer_IsRequestIdResultAndName()
    {
        var packet = new AppNotifySaveYrcSettingsResult(null!, 7, 0, "CUSTOM").Serialize(99);
        Assert.Equal(Convert.FromHexString("0204" + "07" + "00000000").Concat("CUSTOM"u8.ToArray()), packet);
    }

    [Fact]
    public void TheLoadAndListAnswers_UseTheBikesRequestId()
    {
        var proto = new byte[] { 1, 2, 3 };
        Assert.Equal(Convert.FromHexString("0208000003" + "05" + "010203"), new AppLoadYrcSettingsData(null!, 5, proto).Serialize(99));
        Assert.Equal(Convert.FromHexString("0206000003" + "09" + "010203"), new AppResponseSavedYrcSettings(null!, 9, proto).Serialize(99));
    }
}
