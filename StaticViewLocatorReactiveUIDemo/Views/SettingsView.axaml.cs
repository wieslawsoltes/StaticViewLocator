using ReactiveUI.Avalonia;
using StaticViewLocatorReactiveUIDemo.ViewModels;

namespace StaticViewLocatorReactiveUIDemo.Views;

public sealed partial class SettingsView : ReactiveUserControl<SettingsViewModel>
{
    public SettingsView()
    {
        InitializeComponent();
    }
}
