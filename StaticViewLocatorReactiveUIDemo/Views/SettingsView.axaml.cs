#if REACTIVEUI_SYSTEM_REACTIVE
using ReactiveUI.Avalonia.Reactive;
#else
using ReactiveUI.Avalonia;
#endif
using StaticViewLocatorReactiveUIDemo.ViewModels;

namespace StaticViewLocatorReactiveUIDemo.Views;

public sealed partial class SettingsView : ReactiveUserControl<SettingsViewModel>
{
    public SettingsView()
    {
        InitializeComponent();
    }
}
