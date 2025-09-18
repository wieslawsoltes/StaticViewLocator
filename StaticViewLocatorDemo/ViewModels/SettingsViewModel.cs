using ReactiveUI;

namespace StaticViewLocatorDemo.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private bool _useDarkTheme = true;
    private bool _enableAnimations = true;
    private bool _receiveReleaseNotifications = true;

    public override string Title => "Settings";

    public bool UseDarkTheme
    {
        get => _useDarkTheme;
        set => this.RaiseAndSetIfChanged(ref _useDarkTheme, value);
    }

    public bool EnableAnimations
    {
        get => _enableAnimations;
        set => this.RaiseAndSetIfChanged(ref _enableAnimations, value);
    }

    public bool ReceiveReleaseNotifications
    {
        get => _receiveReleaseNotifications;
        set => this.RaiseAndSetIfChanged(ref _receiveReleaseNotifications, value);
    }
}
