using System.Collections.Generic;
using ReactiveUI;

namespace StaticViewLocatorDemo.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private ViewModelBase _selectedPage;

    public MainWindowViewModel()
    {
        Pages = new ViewModelBase[]
        {
            new DashboardViewModel(),
            new TestViewModel(),
            new SettingsViewModel(),
            new ReportsViewModel(),
        };

        _selectedPage = Pages[0];
    }

    public IReadOnlyList<ViewModelBase> Pages { get; }

    public ViewModelBase SelectedPage
    {
        get => _selectedPage;
        set => this.RaiseAndSetIfChanged(ref _selectedPage, value);
    }
}
