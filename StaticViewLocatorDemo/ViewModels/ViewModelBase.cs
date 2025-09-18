using System;
using ReactiveUI;

namespace StaticViewLocatorDemo.ViewModels;

public class ViewModelBase : ReactiveObject
{
    public virtual string Title => GetType().Name.Replace("ViewModel", string.Empty, StringComparison.Ordinal);
}
