using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace StaticViewLocator;

public sealed partial class StaticViewLocatorGenerator
{
    private const string ReactiveUIViewForMetadataName = "ReactiveUI.IViewFor`1";

    private static Dictionary<INamedTypeSymbol, INamedTypeSymbol> GetReactiveUIMappings(
        Compilation compilation,
        HashSet<INamedTypeSymbol> viewBaseTypes)
    {
        var mappings = new Dictionary<INamedTypeSymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var reactiveUIViewFor = compilation.GetTypeByMetadataName(ReactiveUIViewForMetadataName);
        if (reactiveUIViewFor is null)
        {
            return mappings;
        }

        var contracts = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default)
        {
            reactiveUIViewFor.OriginalDefinition,
        };
        var types = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        CollectTypes(compilation.Assembly.GlobalNamespace, types);

        foreach (var viewType in types
                     .Where(type => type.TypeKind == TypeKind.Class && !type.IsAbstract && !type.IsGenericType)
                     .OrderBy(type => type.ToDisplayString(), StringComparer.Ordinal))
        {
            if (!IsSupportedView(viewType, viewBaseTypes) ||
                !TryGetMappedViewModelType(viewType, contracts, out var viewModelType))
            {
                continue;
            }

            mappings[viewModelType] = viewType;
        }

        return mappings;
    }

    private static string GetGeneratedInterfacesClause(
        INamedTypeSymbol locatorSymbol,
        bool generateIViewLocator,
        bool generateIDataTemplate)
    {
        var interfaces = new List<string>(2);
        if (generateIDataTemplate &&
            !ImplementsInterface(locatorSymbol, "Avalonia.Controls.Templates.IDataTemplate"))
        {
            interfaces.Add("global::Avalonia.Controls.Templates.IDataTemplate");
        }

        if (generateIViewLocator && !ImplementsInterface(locatorSymbol, "ReactiveUI.IViewLocator"))
        {
            interfaces.Add("global::ReactiveUI.IViewLocator");
        }

        return interfaces.Count == 0
            ? string.Empty
            : " : " + string.Join(", ", interfaces);
    }

    private static bool ImplementsInterface(INamedTypeSymbol locatorSymbol, string interfaceMetadataName)
    {
        return locatorSymbol.AllInterfaces.Any(type =>
            string.Equals(GetTypeMetadataName(type.OriginalDefinition), interfaceMetadataName, StringComparison.Ordinal));
    }

    private static bool HasMethod(
        INamedTypeSymbol locatorSymbol,
        string methodName,
        int arity,
        params string[] parameterTypeMetadataNames)
    {
        return locatorSymbol.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Any(method =>
                method.MethodKind == MethodKind.Ordinary &&
                method.Arity == arity &&
                HasParameterTypes(method, parameterTypeMetadataNames));
    }

    private static bool HasParameterTypes(IMethodSymbol method, IReadOnlyList<string> parameterTypeMetadataNames)
    {
        if (method.Parameters.Length != parameterTypeMetadataNames.Count)
        {
            return false;
        }

        for (var index = 0; index < method.Parameters.Length; index++)
        {
            if (method.Parameters[index].RefKind != RefKind.None ||
                !string.Equals(
                    GetTypeMetadataName(method.Parameters[index].Type),
                    parameterTypeMetadataNames[index],
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasTryCreateViewExact(INamedTypeSymbol locatorSymbol)
    {
        return locatorSymbol.GetMembers("TryCreateViewExact")
            .OfType<IMethodSymbol>()
            .Any(method =>
                method.MethodKind == MethodKind.Ordinary &&
                method.Arity == 0 &&
                method.Parameters.Length == 2 &&
                method.Parameters[0].RefKind == RefKind.None &&
                method.Parameters[1].RefKind == RefKind.Out &&
                string.Equals(GetTypeMetadataName(method.Parameters[0].Type), "System.Type", StringComparison.Ordinal) &&
                string.Equals(GetTypeMetadataName(method.Parameters[1].Type), "Avalonia.Controls.Control", StringComparison.Ordinal));
    }

    private static bool HasDataTemplateBuildMethod(INamedTypeSymbol locatorSymbol)
    {
        return HasMethod(locatorSymbol, "Build", 0, "System.Object");
    }

    private static bool HasDataTemplateMatchMethod(INamedTypeSymbol locatorSymbol)
    {
        return HasMethod(locatorSymbol, "Match", 0, "System.Object");
    }

    private static string GetHookModifier(INamedTypeSymbol locatorSymbol)
    {
        return locatorSymbol.IsSealed ? "private" : "protected virtual";
    }

    private static string? GetTypeMetadataName(ITypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeKind == TypeKind.Dynamic)
        {
            return "System.Object";
        }

        if (typeSymbol is not INamedTypeSymbol namedType)
        {
            return null;
        }

        var typeNames = new Stack<string>();
        for (var current = namedType; current is not null; current = current.ContainingType)
        {
            typeNames.Push(current.MetadataName);
        }

        var typeName = string.Join("+", typeNames);
        var namespaceName = namedType.ContainingNamespace.ToDisplayString();
        return string.IsNullOrEmpty(namespaceName)
            ? typeName
            : namespaceName + "." + typeName;
    }

    private static bool ShouldGenerateIViewLocator(INamedTypeSymbol locatorSymbol)
    {
        var locatorAttribute = locatorSymbol.GetAttributes()
            .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == StaticViewLocatorAttributeDisplayString);
        if (locatorAttribute is null)
        {
            return false;
        }

        foreach (var argument in locatorAttribute.NamedArguments)
        {
            if (argument.Key == "GenerateIViewLocator" && argument.Value.Value is bool value)
            {
                return value;
            }
        }

        return false;
    }

    private static bool ShouldGenerateIDataTemplate(INamedTypeSymbol locatorSymbol)
    {
        var locatorAttribute = locatorSymbol.GetAttributes()
            .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == StaticViewLocatorAttributeDisplayString);
        if (locatorAttribute is null)
        {
            return false;
        }

        foreach (var argument in locatorAttribute.NamedArguments)
        {
            if (argument.Key == "GenerateIDataTemplate" && argument.Value.Value is bool value)
            {
                return value;
            }
        }

        return false;
    }

    private static ImmutableArray<ITypeSymbol> GetDataTemplateMatchTypes(INamedTypeSymbol locatorSymbol)
    {
        var locatorAttribute = locatorSymbol.GetAttributes()
            .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == StaticViewLocatorAttributeDisplayString);
        if (locatorAttribute is null)
        {
            return ImmutableArray<ITypeSymbol>.Empty;
        }

        foreach (var argument in locatorAttribute.NamedArguments)
        {
            if (argument.Key != "DataTemplateMatchTypes" || argument.Value.Kind != TypedConstantKind.Array)
            {
                continue;
            }

            var builder = ImmutableArray.CreateBuilder<ITypeSymbol>();
            foreach (var value in argument.Value.Values)
            {
                if (value.Value is ITypeSymbol type)
                {
                    builder.Add(type);
                }
            }

            return builder.ToImmutable();
        }

        return ImmutableArray<ITypeSymbol>.Empty;
    }

    private static void AppendIViewLocator(StringBuilder source, INamedTypeSymbol locatorSymbol)
    {
        if (!HasMethod(locatorSymbol, "ResolveView", 1))
        {
            source.Append(
                """

    public global::ReactiveUI.IViewFor<TViewModel>? ResolveView<TViewModel>()
        where TViewModel : class
    {
        return ResolveView<TViewModel>(null);
    }
""");
        }

        if (!HasMethod(locatorSymbol, "ResolveView", 1, "System.String"))
        {
            source.Append(
                """

    public global::ReactiveUI.IViewFor<TViewModel>? ResolveView<TViewModel>(string? contract)
        where TViewModel : class
    {
        if (contract is not null || !TryCreateViewExact(typeof(TViewModel), out var control))
        {
            return null;
        }

        return control as global::ReactiveUI.IViewFor<TViewModel>;
    }
""");
        }

        if (!HasMethod(locatorSymbol, "ResolveView", 0, "System.Object"))
        {
            source.Append(
                """

    public global::ReactiveUI.IViewFor? ResolveView(object? instance)
    {
        return ResolveView(instance, null);
    }
""");
        }

        if (!HasMethod(locatorSymbol, "ResolveView", 0, "System.Object", "System.String"))
        {
            source.Append(
                """

    public global::ReactiveUI.IViewFor? ResolveView(object? instance, string? contract)
    {
        if (instance is null || contract is not null || !TryCreateViewExact(instance.GetType(), out var control))
        {
            return null;
        }

        var view = control as global::ReactiveUI.IViewFor;
        if (view is not null)
        {
            view.ViewModel = instance;
        }

        return view;
    }
""");
        }
    }

    private static void AppendDataTemplateBuild(
        StringBuilder source,
        INamedTypeSymbol locatorSymbol,
        bool generateIViewLocator)
    {
        var hookModifier = GetHookModifier(locatorSymbol);
        source.Append(
            """

    public Control Build(object? param)
    {
        var viewModelType = param?.GetType();
        if (viewModelType is null)
        {
            return BuildInvalidView(param);
        }

        Control? control = BuildResolvedView(param);
        if (control is not null)
        {
            return control;
        }

        control = BuildFallbackView(param);
        if (control is not null)
        {
            return control;
        }

        return BuildMissingView(param, viewModelType);
    }
""");

        if (!HasMethod(locatorSymbol, "BuildResolvedView", 0, "System.Object"))
        {
            if (generateIViewLocator)
            {
                source.Append(
                    $$"""

    {{hookModifier}} Control? BuildResolvedView(object? param)
    {
        if (param is null || !TryCreateViewExact(param.GetType(), out var control))
        {
            return null;
        }

        if (control is global::ReactiveUI.IViewFor view)
        {
            view.ViewModel = param;
        }
        else if (control is not null)
        {
            control.DataContext = param;
        }

        return control;
    }
""");
            }
            else
            {
                source.Append(
                    $$"""

    {{hookModifier}} Control? BuildResolvedView(object? param)
    {
        if (param is null || !TryCreateViewExact(param.GetType(), out var control))
        {
            return null;
        }

        if (control is not null)
        {
            control.DataContext = param;
        }

        return control;
    }
""");
            }
        }

        if (!HasMethod(locatorSymbol, "BuildFallbackView", 0, "System.Object"))
        {
            source.Append(
                $$"""

    {{hookModifier}} Control? BuildFallbackView(object? param)
    {
        return null;
    }
""");
        }

        if (!HasMethod(locatorSymbol, "BuildInvalidView", 0, "System.Object"))
        {
            source.Append(
                $$"""

    {{hookModifier}} Control BuildInvalidView(object? param)
    {
        return new TextBlock { Text = "Invalid view model Type" };
    }
""");
        }

        if (!HasMethod(locatorSymbol, "BuildMissingView", 0, "System.Object", "System.Type"))
        {
            source.Append(
                $$"""

    {{hookModifier}} Control BuildMissingView(object? param, Type viewModelType)
    {
        return new TextBlock { Text = $"View for {viewModelType.FullName} is not found." };
    }
""");
        }
    }

    private static void AppendDataTemplateMatch(StringBuilder source, INamedTypeSymbol locatorSymbol)
    {
        var hookModifier = GetHookModifier(locatorSymbol);
        source.Append(
            """

    public bool Match(object? data)
    {
        return MatchDataTemplate(data);
    }
""");

        if (HasMethod(locatorSymbol, "MatchDataTemplate", 0, "System.Object"))
        {
            return;
        }

        var matchTypes = GetDataTemplateMatchTypes(locatorSymbol);
        if (matchTypes.IsDefaultOrEmpty)
        {
            source.Append(
                $$"""

    {{hookModifier}} bool MatchDataTemplate(object? data)
    {
        if (data is null)
        {
            return false;
        }

        var type = data.GetType();
        return s_views.ContainsKey(type) || s_missingViews.ContainsKey(type);
    }
""");
            return;
        }

        source.Append(
            $$"""

    {{hookModifier}} bool MatchDataTemplate(object? data)
    {
        if (data is null)
        {
            return false;
        }

        var type = data.GetType();
        return
""");

        for (var index = 0; index < matchTypes.Length; index++)
        {
            var typeName = matchTypes[index].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            source.Append("            typeof(");
            source.Append(typeName);
            source.Append(").IsAssignableFrom(type)");
            source.AppendLine(index == matchTypes.Length - 1 ? ";" : " ||");
        }

        source.Append("    }\n");
    }
}
