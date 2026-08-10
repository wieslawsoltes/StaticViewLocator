using ReactiveUI;

namespace StaticViewLocatorReactiveUIDemo.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private object? _currentContent;
    private ViewModelBase? _currentViewModel;

    public MainWindowViewModel()
    {
        Home = new HomeViewModel();
        Settings = new SettingsViewModel();
        WrappedSettings = new ContextHost(Settings);
        _currentContent = Home;
        _currentViewModel = Home;
    }

    public HomeViewModel Home { get; }

    public SettingsViewModel Settings { get; }

    public ContextHost WrappedSettings { get; }

    public object? CurrentContent
    {
        get => _currentContent;
        private set => this.RaiseAndSetIfChanged(ref _currentContent, value);
    }

    public ViewModelBase? CurrentViewModel
    {
        get => _currentViewModel;
        private set => this.RaiseAndSetIfChanged(ref _currentViewModel, value);
    }

    public void ShowHome()
    {
        CurrentContent = Home;
        CurrentViewModel = Home;
    }

    public void ShowSettings()
    {
        CurrentContent = Settings;
        CurrentViewModel = Settings;
    }

    public void ShowWrappedSettings()
    {
        CurrentContent = WrappedSettings;
        CurrentViewModel = Settings;
    }
}
