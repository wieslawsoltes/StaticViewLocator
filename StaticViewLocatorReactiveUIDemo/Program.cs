using Avalonia;
#if REACTIVEUI_SYSTEM_REACTIVE
using ReactiveUI.Avalonia.Reactive;
#else
using ReactiveUI.Avalonia;
#endif

namespace StaticViewLocatorReactiveUIDemo;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace()
        .UseReactiveUI(_ => { });
}
