namespace StaticViewLocatorReactiveUIDemo.ViewModels;

public sealed class ContextHost
{
    public ContextHost(ViewModelBase context)
    {
        Context = context;
    }

    public ViewModelBase Context { get; }
}
