# StaticViewLocator

[![CI](https://github.com/wieslawsoltes/StaticViewLocator/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/StaticViewLocator/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/StaticViewLocator.svg)](https://www.nuget.org/packages/StaticViewLocator)
[![NuGet](https://img.shields.io/nuget/dt/StaticViewLocator.svg)](https://www.nuget.org/packages/StaticViewLocator)

A C# source generator that automatically implements static view locator for Avalonia without using reflection.

## Usage

Add NuGet package reference to project.

```xml
<PackageReference Include="StaticViewLocator" Version="0.5.0">
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
3. automatic `ReactiveUI.IViewFor<TViewModel>` inference when `GenerateIViewLocator = true`
4. configured namespace and type-name conventions

### Exact factory generation

Set `GenerateViewFactoryMethods = true` to generate this private partial-class helper:

```csharp
private static bool TryCreateViewExact(Type viewModelType, out Control? view)
```

It performs only an exact dictionary lookup and invokes the statically generated constructor delegate. It does not walk base types or interfaces and does not construct closed generic types at runtime. This is useful when an MVVM framework needs to create a view and then apply its own view-model assignment or lifecycle rules.

If the annotated partial class already declares a compatible `TryCreateViewExact(Type, out Control?)`, the generator reuses it instead of emitting a duplicate helper.

When a locator supplies its own `Build` method, set `GenerateRuntimeTypeFallbackMethods = false` to omit the legacy `BaseType`, `GetInterfaces()`, and generic-type-definition fallback helpers. If the generator must emit the legacy `Build`, those helpers are always emitted because that implementation depends on them. The generated `IDataTemplate.Build` path uses exact static lookup and does not emit these runtime type-walking helpers unless a source-declared `Build(object?)` requires them and the option remains enabled.

### Generated ReactiveUI `IViewLocator` and Avalonia `IDataTemplate`

The generator can optionally generate the complete ReactiveUI `IViewLocator` and Avalonia `IDataTemplate` adapter. This removes the manual `Build`, `Match`, and four `ResolveView` methods from the locator class.

```csharp
using ReactiveUI;
using StaticViewLocator;

[StaticViewLocator(
    GenerateIViewLocator = true,
    GenerateIDataTemplate = true,
    GenerateRuntimeTypeFallbackMethods = false,
    DataTemplateMatchTypes = new[] { typeof(ViewModelBase), typeof(IDockable) })]
public partial class ViewLocator
{
}
```

`GenerateIViewLocator = true` requires the consumer project to reference ReactiveUI. It automatically discovers concrete Avalonia views implementing `ReactiveUI.IViewFor<TViewModel>`, so `ViewModelMappingContracts = new[] { typeof(IViewFor<>) }` and `GenerateViewFactoryMethods = true` are not required for this mode. User-configured `ViewModelMappingContracts` take precedence over this automatic ReactiveUI inference, while `[StaticViewMapping]` remains the final override. The generated locator implements all four current ReactiveUI resolution overloads. Runtime-instance resolution assigns `IViewFor.ViewModel`; non-null contracts return `null` because the generated map is currently unkeyed.

`GenerateIDataTemplate = true` adds `IDataTemplate`, `Build(object?)`, and `Match(object?)`. `DataTemplateMatchTypes` provides a fast application-specific match predicate. If it is empty, the generated `Match` checks the statically generated view and missing-view maps using the same exact-type semantics as generated `Build`.

The generated `Build` pipeline is:

1. `BuildInvalidView(param)` for `null` input.
2. `BuildResolvedView(param)` for the normal statically mapped view.
3. `BuildFallbackView(param)` for application-specific fallback cases.
4. `BuildMissingView(param, viewModelType)` for the final not-found control.

For non-sealed locator classes, default hook implementations are generated as `protected virtual`, allowing normal subclass overrides. For sealed locator classes the generated defaults are `private`, because virtual members are illegal on sealed types. In both cases, a hook with the corresponding by-value `object?`-based signature declared directly in the annotated partial class suppresses generation of that default hook; unrelated or `ref`/`in`/`out` overloads do not suppress it. This allows application-specific behavior without replacing the public generated `Build` method. For example, a Dock-style context fallback can be implemented as:

```csharp
[StaticViewLocator(
    GenerateIViewLocator = true,
    GenerateIDataTemplate = true,
    GenerateRuntimeTypeFallbackMethods = false,
    DataTemplateMatchTypes = new[] { typeof(ViewModelBase), typeof(IDockable) })]
public partial class ViewLocator
{
    protected virtual Control? BuildFallbackView(object? param)
    {
        if (param is not IDockable { Context: ViewModelBase })
        {
            return null;
        }

        var contentControl = new ContentControl
        {
            DataContext = param,
        };
        contentControl.Bind(
            ContentControl.ContentProperty,
            new Binding(nameof(IDockable.Context)));
        return contentControl;
    }
}
```

The generator assembly itself does not reference ReactiveUI. ReactiveUI types are referenced only in generated consumer source when `GenerateIViewLocator` is enabled.

A complete Avalonia + ReactiveUI example is available in `StaticViewLocatorReactiveUIDemo`. It displays the same navigation state through two side-by-side paths: an Avalonia `ContentControl` using the generated `IDataTemplate`, and a ReactiveUI `ViewModelViewHost` whose `ViewLocator` is the generated locator. The sample also demonstrates the context-wrapper fallback without runtime view scanning.

### Attribute options

| Option | Default | Behavior |
| --- | --- | --- |
| `GenerateViewFactoryMethods` | `false` | Emits `TryCreateViewExact(Type, out Control?)` for framework adapters and custom locators unless a compatible source helper already exists. |
| `GenerateRuntimeTypeFallbackMethods` | `true` | Emits base/interface/open-generic runtime fallback helpers when a legacy or source-declared `Build` path needs them; generated `IDataTemplate.Build` does not require them. |
| `GenerateIViewLocator` | `false` | Generates ReactiveUI `IViewLocator`, all four `ResolveView` overloads, and automatic `IViewFor<TViewModel>` compile-time mappings. Requires a ReactiveUI reference in the consumer project. |
| `GenerateIDataTemplate` | `false` | Generates Avalonia `IDataTemplate`, `Build`, `Match`, and customizable build hooks. |
| `ViewModelMappingContracts` | empty | Infers mappings from configured open generic contracts with one type parameter. |
| `DataTemplateMatchTypes` | empty | Types accepted by generated `Match`; when empty, generated maps are checked using exact runtime type. |

`[StaticViewMapping(typeof(TViewModel), typeof(TView))]` is repeatable and is the final override for a mapped view model. It also admits model types whose names do not end in `ViewModel`.

The generator emits:
- `s_views`: resolved mappings from `Type` to `Func<Control>`
- `s_missingViews`: unresolved mappings used for `"Not Found: ..."` fallback text
- optional exact factory creation through `TryCreateViewExact`
- optional generated ReactiveUI `IViewLocator`
- optional generated Avalonia `IDataTemplate`
- runtime helpers for generic type-definition, base-class, and interface fallback only when required or enabled for a source-declared/legacy `Build` path

By default, the legacy generated lookup order is:
1. exact runtime type
2. generic type definition for generic runtime types
3. base type chain
4. implemented interfaces in reverse order

The generated `IViewLocator` and generated `IDataTemplate.Build` paths intentionally use exact static lookup. This keeps their resolution predictable and avoids the runtime type walking used by the legacy `Build` implementation.

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
- Optional generated ReactiveUI `IViewLocator`
- Optional generated Avalonia `IDataTemplate`
- Customizable generated `Build` pipeline hooks
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
- Missing views do not block fallback resolution. The generator keeps unresolved targets in `s_missingViews`, so a derived type can still fall back to a base-class or interface mapping before returning a `"Not Found"` placeholder in the legacy runtime-fallback path.
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
