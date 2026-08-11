#if REACTIVEUI_SYSTEM_REACTIVE
using ReactiveUI.Avalonia.Reactive;
#else
using ReactiveUI.Avalonia;
#endif

namespace StaticViewLocatorReactiveUIDemo.Controls;

public sealed class ReactiveViewHost : ViewModelViewHost
{
}
