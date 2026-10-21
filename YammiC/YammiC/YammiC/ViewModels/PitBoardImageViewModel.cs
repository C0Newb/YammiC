using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YammiC.Services;

namespace YammiC.ViewModels;

/// <summary>
/// Picks an image and pushes it to the bike's Virtual Pit Board (or clears the board).
/// </summary>
public partial class PitBoardImageViewModel : ViewModelBase
{
    private const string DefaultImageName = "sandwich.jpg";

    private readonly IBikeController _controller;

    // The image that "Send image" pushes, as picked/loaded (any format Skia can decode).
    private byte[]? _data;

    public PitBoardImageViewModel(IBikeController controller)
    {
        _controller = controller;
        UseDefaultImage();
    }

    // Design-time constructor.
    public PitBoardImageViewModel() : this(new FakeBikeController())
    {
    }

    [ObservableProperty]
    public partial Bitmap? Preview { get; set; }

    [ObservableProperty]
    public partial string ImageName { get; set; } = "";

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial bool IsSending { get; set; }

    /// <summary>
    /// Selects a user-picked image to send (called by the view after its file picker returns).
    /// </summary>
    public void SetImage(byte[] data, string name)
    {
        try
        {
            using var stream = new MemoryStream(data);
            Preview = new Bitmap(stream);
            _data = data;
            ImageName = name;
            Status = "";
        }
        catch (Exception e)
        {
            Status = $"Can't use that image: {e.Message}";
        }
    }

    /// <summary>
    /// Goes back to the bundled sandwich.
    /// </summary>
    [RelayCommand]
    private void UseDefaultImage()
    {
        try
        {
            using var asset = AssetLoader.Open(new Uri($"avares://YammiC/Assets/{DefaultImageName}"));
            using var memory = new MemoryStream();
            asset.CopyTo(memory);
            SetImage(memory.ToArray(), DefaultImageName);
        }
        catch (Exception e)
        {
            // Not fatal (e.g. design-time previewer without the resource).
            Status = $"Default image unavailable: {e.Message}";
        }
    }

    [RelayCommand]
    private async Task Send()
    {
        if (_data == null || IsSending) return;
        IsSending = true;
        Status = "Sending...";
        try
        {
            Status = await _controller.SendVpbImageAsync(_data)
                ? "Sent"
                : "Not sent (is the bike connected and VPB enabled?)";
        }
        catch (Exception e)
        {
            Status = $"Failed: {e.Message}";
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private async Task Clear()
    {
        if (IsSending) return;
        IsSending = true;
        try
        {
            Status = await _controller.ClearVpbAsync() ? "Cleared" : "Couldn't clear (is the bike connected?)";
        }
        catch (Exception e)
        {
            Status = $"Failed: {e.Message}";
        }
        finally
        {
            IsSending = false;
        }
    }
}
