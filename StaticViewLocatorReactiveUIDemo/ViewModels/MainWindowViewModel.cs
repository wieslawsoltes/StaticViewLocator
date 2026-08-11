using System.Windows.Input;
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

        ShowHomeCommand = ReactiveCommand.Create(ShowHome);
        ShowSettingsCommand = ReactiveCommand.Create(ShowSettings);
        ShowWrappedSettingsCommand = ReactiveCommand.Create(ShowWrappedSettings);
    }

    public HomeViewModel Home { get; }

    public SettingsViewModel Settings { get; }

    public ContextHost WrappedSettings { get; }

    public ICommand ShowHomeCommand { get; }

    public ICommand ShowSettingsCommand { get; }

    public ICommand ShowWrappedSettingsCommand { get; }

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

    private void ShowHome()
    {
        CurrentContent = Home;
        CurrentViewModel = Home;
    }

    private void ShowSettings()
    {
        CurrentContent = Settings;
        CurrentViewModel = Settings;
    }

    private void ShowWrappedSettings()
    {
        CurrentContent = WrappedSettings;
        CurrentViewModel = Settings;
    }
}
