using CNCO.YccuSdk.Navigation;

namespace YammiC.Services;

public static class NavigationStateFormatter
{
    public static string Describe(NavigationState state) => state switch
    {
        NavigationState.Connecting => "Connecting...",
        NavigationState.Active a => $"Active ({a.Fps} fps)",
        NavigationState.Error e => $"Error: {e.Message}",
        _ => "Inactive",
    };
}
