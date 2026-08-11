using ReactiveUI.Avalonia;
using StaticViewLocatorReactiveUIDemo.ViewModels;

namespace StaticViewLocatorReactiveUIDemo.Views;

public sealed partial class HomeView : ReactiveUserControl<HomeViewModel>
{
    public HomeView()
    {
        InitializeComponent();
    }
}
