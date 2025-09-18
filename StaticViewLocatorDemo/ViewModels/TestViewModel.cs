namespace StaticViewLocatorDemo.ViewModels;

public class TestViewModel : ViewModelBase
{
    public override string Title => "Welcome";

    public string Greeting => "Welcome to Avalonia!";

    public string Description => "This page is produced entirely via the static view locator.";
}
