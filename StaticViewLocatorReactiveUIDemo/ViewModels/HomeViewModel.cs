namespace StaticViewLocatorReactiveUIDemo.ViewModels;

public sealed class HomeViewModel : ViewModelBase
{
    public string Title => "Home view resolved statically";

    public string Description =>
        "The generated IDataTemplate calls the generated IViewLocator, creates HomeView, and assigns this ViewModel.";
}
