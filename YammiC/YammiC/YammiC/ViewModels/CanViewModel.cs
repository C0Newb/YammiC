using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CNCO.YccuSdk.Can;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YammiC.Services;

namespace YammiC.ViewModels;

public record CanValueRow(string Name, string Value);

public record CanRawRow(string Id, string Name, string Bytes, string Age, string Changed);

/// <summary>A checkbox for one CAN ID that can be requested.</summary>
public partial class CanSignalOption : ObservableObject
{
    public CanSignalOption(CanSignalInfo info, bool selected)
    {
        Id = info.Id;
        Title = $"0x{info.Id:X4}  {info.Name}";
        Description = info.Description;
        IsSelected = selected;
    }

    public int Id { get; }
    public string Title { get; }
    public string Description { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>Live bike (CAN) data: the decoded values and the raw bytes of every ID we asked for.</summary>
public partial class CanViewModel : BikeViewModelBase
{
    private bool _applying;

    [ObservableProperty]
    public partial bool CanDataEnabled { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    /// <summary>Extra CAN IDs in hex (e.g. 0x0123, 0400) to ask the bike for while exploring.</summary>
    [ObservableProperty]
    public partial string ExtraIdsText { get; set; } = "";

    [ObservableProperty]
    public partial string ExtraIdsStatus { get; set; } = "";

    [ObservableProperty]
    public partial string DiscoveryReport { get; set; } = "";

    [ObservableProperty]
    public partial bool IsDiscovering { get; set; }

    public ObservableCollection<CanSignalOption> Signals { get; } = new();

    public ObservableCollection<CanValueRow> Values { get; } = new();

    public ObservableCollection<CanRawRow> Raw { get; } = new();

    public CanViewModel(IBikeController controller) : base(controller)
    {
        ExtraIdsText = string.Join(", ", Controller.CanExtraIds.Select(id => $"0x{id:X4}"));
        var selected = Controller.CanSignalIds;
        foreach (var info in CanSignals.Catalog)
        {
            var option = new CanSignalOption(info, selected.Contains(info.Id));
            option.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CanSignalOption.IsSelected)) ApplySelection();
            };
            Signals.Add(option);
        }

        Controller.CanChanged += HandleCanChanged;
        Attach();
        RefreshCan();
    }

    // Design-time constructor.
    public CanViewModel() : this(new FakeBikeController())
    {
    }

    public override void Detach()
    {
        base.Detach();
        Controller.CanChanged -= HandleCanChanged;
    }

    protected override void OnSnapshot(BikeSnapshot snapshot) => RefreshCan();

    [RelayCommand]
    private void ApplyExtraIds()
    {
        var ids = new System.Collections.Generic.List<int>();
        foreach (var token in ExtraIdsText.Split(new[] { ',', ';', ' ', (char)10, (char)9 }, StringSplitOptions.RemoveEmptyEntries))
        {
            var hex = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token[2..] : token;
            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) || id is < 0 or > 0xFFFF)
            {
                ExtraIdsStatus = $"'{token}' is not a hex ID between 0 and FFFF.";
                return;
            }
            ids.Add(id);
        }
        Controller.CanExtraIds = ids;
        ExtraIdsText = string.Join(", ", Controller.CanExtraIds.Select(id => $"0x{id:X4}"));
        ExtraIdsStatus = ids.Count == 0
            ? "No extra IDs."
            : Controller.CanDataEnabled ? "Asking for them now. Anything the bike answers shows in the raw signals below." : "Saved. Turn on 'Ask the bike for data' to request them.";
    }

    [RelayCommand]
    private async Task Discover()
    {
        if (IsDiscovering) return;
        IsDiscovering = true;
        DiscoveryReport = "Asking the bike...";
        try
        {
            DiscoveryReport = await Controller.DiscoverSupportedSignalsAsync();
        }
        catch (Exception e)
        {
            DiscoveryReport = $"Failed: {e.Message}";
        }
        finally
        {
            IsDiscovering = false;
        }
    }

    private void ApplySelection() =>
        Controller.CanSignalIds = Signals.Where(s => s.IsSelected).Select(s => s.Id).ToList();

    private void HandleCanChanged() => Dispatcher.UIThread.Post(RefreshCan);

    partial void OnCanDataEnabledChanged(bool value)
    {
        if (_applying) return;
        Controller.CanDataEnabled = value;
        RefreshCan();
    }

    private void RefreshCan()
    {
        _applying = true;
        try
        {
            CanDataEnabled = Controller.CanDataEnabled;
        }
        finally
        {
            _applying = false;
        }

        var v = Controller.CanValues;
        var raw = Controller.CanRaw;
        var now = DateTime.UtcNow;

        Status = !CanDataEnabled ? "Off. Turn on to ask the bike for data (adds Bluetooth traffic)."
            : raw.Count == 0 ? "Waiting for the bike... (connect first; data arrives a moment after connecting)"
            : $"Receiving {raw.Count} signal{(raw.Count == 1 ? "" : "s")}";

        var rows = new[]
        {
            new CanValueRow("RPM", Num(v.Rpm, "0", " rpm")),
            new CanValueRow("Gear", CanSignals.GearText(v.Gear)),
            new CanValueRow("Water temp", Num(v.WaterTempC, "0", " °C")),
            new CanValueRow("Intake air temp", Num(v.IntakeAirTempC, "0", " °C")),
            new CanValueRow("Throttle (TPS)", Num(v.Tps, "0.0", "") + (v.Tps != null ? "  (scale unverified)" : "")),
            new CanValueRow("Accelerator (APS)", Num(v.Aps, "0.0", "") + (v.Aps != null ? "  (scale unverified)" : "")),
            new CanValueRow("Bank angle", Num(v.BankAngle, "0.0", "°")),
            new CanValueRow("Pitch rate", Num(v.PitchRate, "0.0", "")),
            new CanValueRow("Acceleration X", Num(v.AccelX, "0.00", "")),
            new CanValueRow("Acceleration Y", Num(v.AccelY, "0.00", "")),
            new CanValueRow("Front wheel speed", Num(v.FrontWheelSpeed, "0.0", "")),
            new CanValueRow("Rear wheel speed", Num(v.RearWheelSpeed, "0.0", "")),
        };
        Replace(Values, rows);

        Replace(Raw, raw.Select(r => new CanRawRow(
            $"0x{r.Id:X4}",
            CanSignals.Catalog.FirstOrDefault(c => c.Id == r.Id)?.Name ?? "",
            Convert.ToHexString(r.Data).Chunk(2).Select(p => new string(p)).Aggregate("", (a, b) => a.Length == 0 ? b : a + " " + b),
            Ago(now - r.ReceivedAt),
            Ago(now - r.ChangedAt))));
    }

    private const string Dash = "—";

    private static string Num(double? value, string format, string unit) =>
        value is { } d ? d.ToString(format, CultureInfo.CurrentCulture) + unit : Dash;

    private static string Ago(TimeSpan age) =>
        age.TotalSeconds < 1 ? "now" : age.TotalMinutes < 1 ? $"{age.TotalSeconds:0}s ago" : $"{age.TotalMinutes:0}m ago";

    private static void Replace<T>(ObservableCollection<T> target, System.Collections.Generic.IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
}
