# StaticViewLocator

[![CI](https://github.com/wieslawsoltes/StaticViewLocator/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/StaticViewLocator/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/StaticViewLocator.svg)](https://www.nuget.org/packages/StaticViewLocator)
[![NuGet](https://img.shields.io/nuget/dt/StaticViewLocator.svg)](https://www.nuget.org/packages/StaticViewLocator)

A C# source generator that automatically implements static view locator for Avalonia without using reflection.

## Usage

Add NuGet package reference to project.

```xml
<PackageReference Include="StaticViewLocator" Version="0.3.0">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

Annotate view locator class with `[StaticViewLocator]` attribute, make class `partial` and imlement `Build` using `s_views` dictionary to retrieve views for `data` objects.

```csharp
[StaticViewLocator]
public partial class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
        {
            return null;
        }

        var type = data.GetType();

        if (s_views.TryGetValue(type, out var func))
        {
            return func.Invoke();
        }

        throw new Exception($"Unable to create view for type: {type}");
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
```

Source generator will generate the `s_views` dictionary similar to below code using convention based on `ViewModel` suffix for view models subsituted to `View` suffix.

```csharp
public partial class ViewLocator
{
	private static Dictionary<Type, Func<Control>> s_views = new()
	{
		[typeof(StaticViewLocatorDemo.ViewModels.MainWindowViewModel)] = () => new TextBlock() { Text = "Not Found: StaticViewLocatorDemo.Views.MainWindowView" },
		[typeof(StaticViewLocatorDemo.ViewModels.TestViewModel)] = () => new StaticViewLocatorDemo.Views.TestView(),
	};
}
```

## MSBuild configuration

You can scope which view model namespaces are considered and opt into additional behaviors.

```xml
<PropertyGroup>
  <StaticViewLocatorViewModelNamespacePrefixes>MyApp.ViewModels;MyApp.Modules</StaticViewLocatorViewModelNamespacePrefixes>
  <StaticViewLocatorIncludeInternalViewModels>false</StaticViewLocatorIncludeInternalViewModels>
  <StaticViewLocatorIncludeReferencedAssemblies>false</StaticViewLocatorIncludeReferencedAssemblies>
  <StaticViewLocatorAdditionalViewBaseTypes>MyApp.Controls.ToolWindowBase</StaticViewLocatorAdditionalViewBaseTypes>
</PropertyGroup>
```

Defaults and behavior:
- `StaticViewLocatorViewModelNamespacePrefixes` uses `;` or `,` separators and defaults to all namespaces.
- `StaticViewLocatorIncludeReferencedAssemblies` defaults to `false`. When `true`, view models from referenced assemblies are included.
- `StaticViewLocatorIncludeInternalViewModels` defaults to `false`. When `true`, internal view models from referenced assemblies are included only if the referenced assembly exposes them via `InternalsVisibleTo`.
- `StaticViewLocatorAdditionalViewBaseTypes` uses `;` or `,` separators and extends the default view base type list.

These properties are exported as `CompilerVisibleProperty` by the package, so analyzers can read them without extra project configuration.

Default view base types:
- `Avalonia.Controls.UserControl`
- `Avalonia.Controls.Window`

Accessibility rules:
- View models in the current compilation are always eligible (subject to namespace prefixes).
- Referenced assembly view models must be public unless `StaticViewLocatorIncludeInternalViewModels` is enabled and `InternalsVisibleTo` is configured.

## License

StaticViewLocator is licensed under the MIT license. See [LICENSE](LICENSE.TXT) file for details.
