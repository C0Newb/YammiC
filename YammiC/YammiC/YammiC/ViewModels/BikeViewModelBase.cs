using System;
using Avalonia.Threading;
using YammiC.Services;

namespace YammiC.ViewModels;

/// <summary>
/// Base for view models that follow the shared <see cref="IBikeController"/>. Controller events arrive on
/// arbitrary threads; <see cref="OnSnapshot"/> always runs on the UI thread.
/// </summary>
public abstract class BikeViewModelBase : ViewModelBase
{
    protected IBikeController Controller { get; }

    protected BikeViewModelBase(IBikeController controller)
    {
        Controller = controller;
    }

    /// <summary>
    /// Applies the current snapshot and starts following changes. Call at the end of the derived constructor.
    /// </summary>
    protected void Attach()
    {
        OnSnapshot(Controller.Snapshot);
        Controller.Changed += HandleChanged;
    }

    /// <summary>
    /// Stops following the controller (call when the page goes away).
    /// </summary>
    public virtual void Detach() => Controller.Changed -= HandleChanged;

    private void HandleChanged() => Dispatcher.UIThread.Post(() => OnSnapshot(Controller.Snapshot));

    protected abstract void OnSnapshot(BikeSnapshot snapshot);
}
