using CCU_Emulator.Emulation;
using CCU_Emulator.Protocol;
using CommunityToolkit.Mvvm.Input;

namespace CCU_Emulator.ViewModels;

/// <summary>The action-specific "handlebar" buttons. Each one sends one CCU→phone command.</summary>
public sealed partial class ButtonsViewModel : ViewModelBase
{
    private readonly CcuSession _session;

    public ButtonsViewModel(CcuSession session)
    {
        _session = session;
        session.State.PropertyChanged += (_, _) => OnPropertyChanged(string.Empty);
    }

    public bool CanAnswerOrReject => _session.State.CallRinging;
    public bool CanHangUp => _session.State.CallRinging || _session.State.CallActive;

    [RelayCommand] private void Previous() => _session.Press(MusicAction.Previous);
    [RelayCommand] private void Next() => _session.Press(MusicAction.Next);
    [RelayCommand] private void Play() => _session.Press(MusicAction.Play);
    [RelayCommand] private void Pause() => _session.Press(MusicAction.Pause);

    [RelayCommand] private void PhoneVolumeUp() => _session.Press(VolumeAction.PhoneUp);
    [RelayCommand] private void PhoneVolumeDown() => _session.Press(VolumeAction.PhoneDown);
    [RelayCommand] private void MediaVolumeUp() => _session.Press(VolumeAction.MediaUp);
    [RelayCommand] private void MediaVolumeDown() => _session.Press(VolumeAction.MediaDown);
    [RelayCommand] private void MuteMicrophone() => _session.Press(VolumeAction.MuteMicrophone);
    [RelayCommand] private void MuteSpeaker() => _session.Press(VolumeAction.MuteSpeaker);

    [RelayCommand] private void Answer() => _session.Press(CallAction.Answer);
    [RelayCommand] private void Reject() => _session.Press(CallAction.Reject);
    [RelayCommand] private void HangUp() => _session.Press(CallAction.HangUp);
}
