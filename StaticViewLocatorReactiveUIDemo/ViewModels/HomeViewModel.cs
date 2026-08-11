namespace StaticViewLocatorReactiveUIDemo.ViewModels;

public sealed class HomeViewModel : ViewModelBase
{
    public string Title => "Home view resolved statically";

    public string Description =>
        "The generated IDataTemplate creates HomeView through exact static lookup and assigns this ViewModel.";
}
