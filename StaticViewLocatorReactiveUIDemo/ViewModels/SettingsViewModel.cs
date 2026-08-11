namespace StaticViewLocatorReactiveUIDemo.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    public string Title => "Settings view";

    public string Description =>
        "This view is also discovered from ReactiveUI.IViewFor<TViewModel> without reflection-based view scanning.";
}
