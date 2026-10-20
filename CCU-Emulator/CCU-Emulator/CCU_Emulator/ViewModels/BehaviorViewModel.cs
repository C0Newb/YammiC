using CCU_Emulator.Emulation;

namespace CCU_Emulator.ViewModels;

public sealed class BehaviorViewModel(CcuBehavior behavior) : ViewModelBase
{
    public CcuBehavior Behavior { get; } = behavior;
}
