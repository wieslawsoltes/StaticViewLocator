using Avalonia.Controls;
using Avalonia.Data;
using StaticViewLocatorReactiveUIDemo.ViewModels;

namespace StaticViewLocatorReactiveUIDemo;

[StaticViewLocator.StaticViewLocator(
    GenerateIViewLocator = true,
    GenerateIDataTemplate = true,
    GenerateRuntimeTypeFallbackMethods = false,
    DataTemplateMatchTypes = new[] { typeof(ViewModelBase), typeof(ContextHost) })]
public partial class ViewLocator
{
    // This project-specific hook is declared in the user's partial class.
    // The generator detects it and omits its default BuildFallbackView implementation.
    protected virtual Control? BuildFallbackView(object? param)
    {
        if (param is not ContextHost)
        {
            return null;
        }

        var contentControl = new ContentControl
        {
            DataContext = param,
        };
        contentControl.Bind(
            ContentControl.ContentProperty,
            new Binding(nameof(ContextHost.Context)));
        return contentControl;
    }
}
