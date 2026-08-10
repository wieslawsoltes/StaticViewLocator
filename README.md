# StaticViewLocator

[![CI](https://github.com/wieslawsoltes/StaticViewLocator/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/StaticViewLocator/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/StaticViewLocator.svg)](https://www.nuget.org/packages/StaticViewLocator)
[![NuGet](https://img.shields.io/nuget/dt/StaticViewLocator.svg)](https://www.nuget.org/packages/StaticViewLocator)

A C# source generator that automatically implements static view locator for Avalonia without using reflection.

## Usage

Add NuGet package reference to project.

```xml
<PackageReference Include="StaticViewLocator" Version="0.4.0">
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

### Explicit mappings

Use `[StaticViewMapping]` on the locator when a view and view model do not follow the configured naming rules. Explicit mappings override convention-based discovery and also support model types whose names do not end in `ViewModel`.

```csharp
[StaticViewLocator]
[StaticViewMapping(typeof(LoginViewModel), typeof(LogInView))]
[StaticViewMapping(typeof(DashboardModel), typeof(DashboardScreen))]
public partial class ViewLocator : IDataTemplate
{
}
```

### Generic MVVM contract mappings

Use `ViewModelMappingContracts` when a framework already expresses the view/view-model relationship through a one-argument generic interface or base class. The generator scans concrete Avalonia views at compile time, follows their interface and base-type ancestry, and maps the contract's generic argument to the concrete view.

```csharp
public interface IViewFor<TViewModel>
{
}

public abstract class FrameworkView<TViewModel> : UserControl, IViewFor<TViewModel>
{
}

public sealed class DashboardScreen : FrameworkView<DashboardModel>
{
}

[StaticViewLocator(
    ViewModelMappingContracts = new[] { typeof(IViewFor<>) })]
public partial class ViewLocator
{
}
```

This generates `DashboardModel -> DashboardScreen` even though neither type follows the default `*ViewModel -> *View` naming convention. Multiple contracts can be supplied in the array.

Contract discovery has these constraints:

- each configured contract must be an open generic type with exactly one type parameter;
- the contract may be an interface or a base class, including one inherited indirectly;
- discovered views must be concrete, non-generic types from the current compilation;
- discovered views must derive from `UserControl`, `Window`, or a type configured through `StaticViewLocatorAdditionalViewBaseTypes`;
- contract discovery happens at compile time and adds no runtime assembly scanning;
- ambiguous view-model mappings should be resolved with an explicit `[StaticViewMapping]` override.

Mapping sources are applied in this order, from highest to lowest priority:

1. `[StaticViewMapping]` explicit override
2. `ViewModelMappingContracts` inference
3. configured namespace and type-name conventions

### Exact factory generation

Set `GenerateViewFactoryMethods = true` to generate this private partial-class helper:

```csharp
private static bool TryCreateViewExact(Type viewModelType, out Control? view)
```

It performs only an exact dictionary lookup and invokes the statically generated constructor delegate. It does not walk base types or interfaces and does not construct closed generic types at runtime. This is useful when an MVVM framework needs to create a view and then apply its own view-model assignment or lifecycle rules.

When a locator supplies its own `Build` method, set `GenerateRuntimeTypeFallbackMethods = false` to omit the legacy `BaseType`, `GetInterfaces()`, and generic-type-definition fallback helpers. If the generator must emit `Build`, those helpers are always emitted because the generated implementation depends on them.

### ReactiveUI `IViewLocator`

ReactiveUI 24 exposes generic AOT-friendly `ResolveView<TViewModel>` overloads as well as runtime-instance overloads. Configure `IViewFor<>` as the mapping contract and use the exact factory for all four methods:

```csharp
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ReactiveUI;
using StaticViewLocator;

[StaticViewLocator(
    GenerateRuntimeTypeFallbackMethods = false,
    GenerateViewFactoryMethods = true,
    ViewModelMappingContracts = new[] { typeof(IViewFor<>) })]
public partial class ViewLocator : IDataTemplate, IViewLocator
{
    public Control? Build(object? data)
    {
        return ResolveView(data) as Control;
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }

    public IViewFor<TViewModel>? ResolveView<TViewModel>()
        where TViewModel : class
    {
        return ResolveView<TViewModel>(null);
    }

    public IViewFor<TViewModel>? ResolveView<TViewModel>(string? contract)
        where TViewModel : class
    {
        if (contract is not null || !TryCreateViewExact(typeof(TViewModel), out var control))
        {
            return null;
        }

        return control as IViewFor<TViewModel>;
    }

    public IViewFor? ResolveView(object? instance)
    {
        return ResolveView(instance, null);
    }

    public IViewFor? ResolveView(object? instance, string? contract)
    {
        if (instance is null ||
            contract is not null ||
            !TryCreateViewExact(instance.GetType(), out var control))
        {
            return null;
        }

        var view = control as IViewFor;
        if (view is not null)
        {
            view.ViewModel = instance;
        }

        return view;
    }
}
```

Replace `ViewModelBase` in `Match` with the application's view-model root type or another fast predicate. Non-null contracts return `null` in this example because the generated map is unkeyed. Register the locator as the application's ReactiveUI locator after ReactiveUI initialization, using the dependency-injection container selected by the application. For example, with Microsoft.Extensions.DependencyInjection:

```csharp
services.AddSingleton<ReactiveUI.IViewLocator, ViewLocator>();
```

The generator itself does not reference ReactiveUI. `ViewModelMappingContracts` and the exact factory are framework-neutral, so the same pattern can support another MVVM framework's generic view contract.

### Attribute options

| Option | Default | Behavior |
| --- | --- | --- |
| `GenerateViewFactoryMethods` | `false` | Emits `TryCreateViewExact(Type, out Control?)` for framework adapters and custom locators. |
| `GenerateRuntimeTypeFallbackMethods` | `true` | Emits base/interface/open-generic runtime fallback helpers for custom `Build` implementations. It cannot disable helpers required by a generator-provided `Build`. |
| `ViewModelMappingContracts` | empty | Infers mappings from configured open generic contracts with one type parameter. |

`[StaticViewMapping(typeof(TViewModel), typeof(TView))]` is repeatable and is the final override for a mapped view model. It also admits model types whose names do not end in `ViewModel`.

The generator emits:
- `s_views`: resolved mappings from `Type` to `Func<Control>`
- `s_missingViews`: unresolved mappings used for `"Not Found: ..."` fallback text
- optional exact factory creation through `TryCreateViewExact`
- runtime helpers for generic type-definition, base-class, and interface fallback when required or enabled

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
- Explicit view/view-model mapping overrides
- Compile-time mapping inference from generic MVVM contracts
- Optional exact factory generation for framework adapters
- Optional omission of runtime type-walking helpers in custom locators
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

- Convention candidate discovery starts from types whose names end with `ViewModel`. Explicit and generic-contract mappings can add other model types.
- Missing views do not block fallback resolution. The generator keeps unresolved targets in `s_missingViews`, so a derived type can still fall back to a base-class or interface mapping before returning a `"Not Found"` placeholder.
- If you provide custom replacement rules, they take precedence over the built-in defaults.
- Exact factory generation is intentionally separate from runtime fallback. Framework adapters should prefer exact creation when the framework supplies the concrete view-model type.

Default view base types:
- `Avalonia.Controls.UserControl`
- `Avalonia.Controls.Window`

Accessibility rules:
- View models in the current compilation are always eligible (subject to namespace prefixes).
- Referenced assembly view models must be public unless `StaticViewLocatorIncludeInternalViewModels` is enabled and `InternalsVisibleTo` is configured.

## License

StaticViewLocator is licensed under the MIT license. See [LICENSE](LICENSE.TXT) file for details.
