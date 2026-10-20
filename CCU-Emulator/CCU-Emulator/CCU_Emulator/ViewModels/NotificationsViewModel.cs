using System.Collections.ObjectModel;
using CCU_Emulator.Emulation;
using CommunityToolkit.Mvvm.Input;

namespace CCU_Emulator.ViewModels;

public sealed partial class NotificationsViewModel(CcuState state, CcuSession session) : ViewModelBase
{
    public ObservableCollection<NotificationEntry> Items => state.Notifications;

    /// <summary>The rider dismisses the notification on the dash.</summary>
    [RelayCommand]
    private void Dismiss(NotificationEntry? entry)
    {
        if (entry is not null) session.DismissNotification(entry.Id);
    }
}
