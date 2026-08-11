#if REACTIVEUI_SYSTEM_REACTIVE
using ReactiveUI.Reactive;
#else
using ReactiveUI;
#endif

namespace StaticViewLocatorReactiveUIDemo.ViewModels;

public abstract class ViewModelBase : ReactiveObject
{
}
