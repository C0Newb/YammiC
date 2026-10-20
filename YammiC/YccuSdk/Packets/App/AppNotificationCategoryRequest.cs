namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Declares to the CCU which notification categories this app will forward.
/// Sent once at connection time. Wire: [cmd 2B][12 bool bytes].
/// Byte order: other, incomingCall, missedCall, voicemail, social, schedule,
///             email, news, healthAndFitness, businessAndFinance, location, entertainment.
/// </summary>
public class AppNotificationCategoryRequest : AppOutgoingRequest
{
    public AppNotificationCategoryRequest(
        IYmcPacketHandler packetHandler,
        bool other = false,
        bool incomingCall = false,
        bool missedCall = false,
        bool voicemail = false,
        bool social = false,
        bool schedule = false,
        bool email = false,
        bool news = false,
        bool healthAndFitness = false,
        bool businessAndFinance = false,
        bool location = false,
        bool entertainment = false)
        : base(packetHandler, YmcCommandCode.NotificationCategory)
    {
        Other = other;
        IncomingCall = incomingCall;
        MissedCall = missedCall;
        Voicemail = voicemail;
        Social = social;
        Schedule = schedule;
        Email = email;
        News = news;
        HealthAndFitness = healthAndFitness;
        BusinessAndFinance = businessAndFinance;
        Location = location;
        Entertainment = entertainment;
    }

    public bool Other { get; }
    public bool IncomingCall { get; }
    public bool MissedCall { get; }
    public bool Voicemail { get; }
    public bool Social { get; }
    public bool Schedule { get; }
    public bool Email { get; }
    public bool News { get; }
    public bool HealthAndFitness { get; }
    public bool BusinessAndFinance { get; }
    public bool Location { get; }
    public bool Entertainment { get; }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId) => Concat(
        Preamble,
        new[]
        {
            AsByte(Other),
            AsByte(IncomingCall),
            AsByte(MissedCall),
            AsByte(Voicemail),
            AsByte(Social),
            AsByte(Schedule),
            AsByte(Email),
            AsByte(News),
            AsByte(HealthAndFitness),
            AsByte(BusinessAndFinance),
            AsByte(Location),
            AsByte(Entertainment),
        });

    private static byte AsByte(bool value) => value ? (byte)1 : (byte)0;
}
