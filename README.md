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

Annotate a view locator class with `[StaticViewLocator]`, make it `partial`, and let the generator provide the lookup tables and fallback helpers.

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
        var func = TryGetFactory(type) ?? TryGetFactoryFromInterfaces(type);

        if (func is not null)
        {
            return func.Invoke();
        }

        var missingView = TryGetMissingView(type) ?? TryGetMissingViewFromInterfaces(type);
        if (missingView is not null)
        {
            return new TextBlock { Text = missingView };
        }

        throw new Exception($"Unable to create view for type: {type}");
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
```

The generator emits:
- `s_views`: resolved mappings from `Type` to `Func<Control>`
- `s_missingViews`: unresolved mappings used for `"Not Found: ..."` fallback text
- helper methods for exact type lookup, generic type-definition lookup, base-class fallback, and interface fallback

By default, the generated lookup order is:
1. exact runtime type
2. generic type definition for generic runtime types
3. base type chain
4. implemented interfaces in reverse order

Source generator will generate mappings using convention-based transforms. By default:
- namespace `ViewModels` becomes `Views`
- type suffix `ViewModel` becomes `View`
- generic arity markers are removed from the target view name
- interface prefix `I` is stripped before resolving the target view name

This allows patterns like:
- `MyApp.ViewModels.SettingsViewModel -> MyApp.Views.SettingsView`
- `MyApp.ViewModels.WidgetViewModel<T> -> MyApp.Views.WidgetView`
- `MyApp.ViewModels.IDetailsViewModel -> MyApp.Views.DetailsView`

```csharp
public partial class ViewLocator
{
	private static Dictionary<Type, Func<Control>> s_views = new()
	{
		[typeof(StaticViewLocatorDemo.ViewModels.TestViewModel)] = () => new StaticViewLocatorDemo.Views.TestView(),
	};

	private static Dictionary<Type, string> s_missingViews = new()
	{
		[typeof(StaticViewLocatorDemo.ViewModels.MainWindowViewModel)] = "Not Found: StaticViewLocatorDemo.Views.MainWindowView",
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
  <StaticViewLocatorNamespaceReplacementRules>ViewModels=Views</StaticViewLocatorNamespaceReplacementRules>
  <StaticViewLocatorTypeNameReplacementRules>ViewModel=View;Vm=Page</StaticViewLocatorTypeNameReplacementRules>
  <StaticViewLocatorStripGenericArityFromViewName>true</StaticViewLocatorStripGenericArityFromViewName>
  <StaticViewLocatorInterfacePrefixesToStrip>I</StaticViewLocatorInterfacePrefixesToStrip>
</PropertyGroup>
```

Defaults and behavior:
- `StaticViewLocatorViewModelNamespacePrefixes` uses `;` or `,` separators and defaults to all namespaces.
- `StaticViewLocatorIncludeReferencedAssemblies` defaults to `false`. When `true`, view models from referenced assemblies are included.
- `StaticViewLocatorIncludeInternalViewModels` defaults to `false`. When `true`, internal view models from referenced assemblies are included only if the referenced assembly exposes them via `InternalsVisibleTo`.
- `StaticViewLocatorAdditionalViewBaseTypes` uses `;` or `,` separators and extends the default view base type list.
- `StaticViewLocatorNamespaceReplacementRules` uses `;` or `,` separators with `from=to` pairs and is applied sequentially to the view-model namespace when deriving the target view namespace. The default includes `ViewModels=Views`.
- `StaticViewLocatorTypeNameReplacementRules` uses `;` or `,` separators with `from=to` pairs and is applied sequentially to the view-model type name when deriving the target view name. The default includes `ViewModel=View`.
- `StaticViewLocatorStripGenericArityFromViewName` defaults to `true`. When enabled, generic arity markers like `` `1 `` are removed from the derived target view name, so `WidgetViewModel<T>` can map to `WidgetView`.
- `StaticViewLocatorInterfacePrefixesToStrip` uses `;` or `,` separators and is applied to interface view-model names before looking up the target view. The default includes `I`.

These properties are exported as `CompilerVisibleProperty` by the package, so analyzers can read them without extra project configuration.

## Supported resolution features

- Exact type mapping
- Open generic mapping, for example `WidgetViewModel<T> -> WidgetView`
- Base-class fallback
- Interface fallback
- Configurable namespace replacement rules
- Configurable type-name replacement rules
- Configurable interface prefix stripping
- Configurable additional allowed view base types
- Optional referenced-assembly scanning
- Optional internal view-model inclusion

## Notes

- Candidate discovery still starts from types whose names end with `ViewModel`.
- Missing views do not block fallback resolution. The generator keeps unresolved targets in `s_missingViews`, so a derived type can still fall back to a base-class or interface mapping before returning a `"Not Found"` placeholder.
- If you provide custom replacement rules, they take precedence over the built-in defaults.

Default view base types:
- `Avalonia.Controls.UserControl`
- `Avalonia.Controls.Window`

Accessibility rules:
- View models in the current compilation are always eligible (subject to namespace prefixes).
- Referenced assembly view models must be public unless `StaticViewLocatorIncludeInternalViewModels` is enabled and `InternalsVisibleTo` is configured.

## License

StaticViewLocator is licensed under the MIT license. See [LICENSE](LICENSE.TXT) file for details.
