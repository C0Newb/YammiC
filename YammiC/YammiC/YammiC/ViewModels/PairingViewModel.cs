using CommunityToolkit.Mvvm.ComponentModel;
using YammiC.Services;

namespace YammiC.ViewModels;

/// <summary>
/// Edits the CCU identity. Pre-filled once from the controller; it deliberately does not follow later
/// snapshots so a log update can't clobber text being typed.
/// </summary>
public partial class PairingViewModel : ViewModelBase
{
    private readonly IBikeController _controller;

    [ObservableProperty]
    public partial string CcuId { get; set; }

    [ObservableProperty]
    public partial string PassKey { get; set; }

    [ObservableProperty]
    public partial string DeviceName { get; set; }

    public PairingViewModel(IBikeController controller)
    {
        _controller = controller;
        var c = controller.Snapshot.Connection;
        CcuId = c.CcuId;
        PassKey = c.PassKey;
        DeviceName = c.DeviceName;
    }

    // Design-time constructor.
    public PairingViewModel() : this(new FakeBikeController())
    {
    }

    public void Save() => _controller.SaveCredentials(CcuId ?? "", PassKey ?? "", DeviceName ?? "");
}
