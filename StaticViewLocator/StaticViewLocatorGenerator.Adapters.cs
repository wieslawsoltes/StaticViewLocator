using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace StaticViewLocator;

public sealed partial class StaticViewLocatorGenerator
{
    private const string AvaloniaControlMetadataName = "Avalonia.Controls.Control";
    private const string AvaloniaDataTemplateMetadataName = "Avalonia.Controls.Templates.IDataTemplate";
    private const string ReactiveUIViewForUntypedMetadataName = "ReactiveUI.IViewFor";
    private const string ReactiveUIViewForMetadataName = "ReactiveUI.IViewFor`1";
    private const string ReactiveUIViewLocatorMetadataName = "ReactiveUI.IViewLocator";

    private static readonly DiagnosticDescriptor MissingAdapterType = new(
        id: "SVL0001",
        title: "Required adapter type is unavailable",
        messageFormat: "'{0}' requires a reference containing '{1}'",
        category: "StaticViewLocator.Generation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The requested framework adapter cannot be generated until all of its contract types are referenced.");

    private static readonly DiagnosticDescriptor IncompatibleAdapterMember = new(
        id: "SVL0002",
        title: "Source member is incompatible with generated adapter",
        messageFormat: "Member '{0}' conflicts with generated adapter member '{1}'; expected {2}",
        category: "StaticViewLocator.Generation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A source-declared member with the generated C# signature must satisfy the adapter or hook contract.");

    private static readonly DiagnosticDescriptor AmbiguousViewMapping = new(
        id: "SVL0003",
        title: "View mapping is ambiguous",
        messageFormat: "View model '{0}' is implemented by both '{1}' and '{2}'; add StaticViewMappingAttribute to select one view",
        category: "StaticViewLocator.Generation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Static view location can select only one inferred view for each view-model type.");

    private static readonly DiagnosticDescriptor ViewCannotBeConstructed = new(
        id: "SVL0004",
        title: "Mapped view cannot be constructed",
        messageFormat: "View '{0}' mapped to '{1}' is not accessible or has no accessible constructor callable without arguments",
        category: "StaticViewLocator.Generation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Generated static factories require an accessible concrete view and a constructor callable without arguments.");

    private static readonly DiagnosticDescriptor UnsupportedLocatorType = new(
        id: "SVL0005",
        title: "Locator type cannot be extended by generated source",
        messageFormat: "Locator '{0}' is {1}; StaticViewLocator requires a non-static, non-file-local top-level partial class",
        category: "StaticViewLocator.Generation",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Generated partial declarations cannot extend static or file-local locator classes.");

    private static Dictionary<INamedTypeSymbol, INamedTypeSymbol> GetReactiveUIMappings(
        Compilation compilation,
        HashSet<INamedTypeSymbol> viewBaseTypes,
        IReadOnlyDictionary<INamedTypeSymbol, INamedTypeSymbol> configuredMappings,
        IReadOnlyDictionary<INamedTypeSymbol, INamedTypeSymbol> explicitMappings,
        ICollection<Diagnostic> diagnostics,
        HashSet<INamedTypeSymbol> ambiguousViewModels)
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

            if (ambiguousViewModels.Contains(viewModelType) ||
                configuredMappings.ContainsKey(viewModelType) ||
                explicitMappings.ContainsKey(viewModelType))
            {
                continue;
            }

            if (mappings.TryGetValue(viewModelType, out var existingView))
            {
                if (!SymbolEqualityComparer.Default.Equals(existingView, viewType))
                {
                    diagnostics.Add(Diagnostic.Create(
                        AmbiguousViewMapping,
                        GetDiagnosticLocation(viewType),
                        viewModelType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        existingView.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        viewType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                    mappings.Remove(viewModelType);
                    ambiguousViewModels.Add(viewModelType);
                }

                continue;
            }

            mappings.Add(viewModelType, viewType);
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

    private static IMethodSymbol? FindMethod(
        INamedTypeSymbol locatorSymbol,
        string methodName,
        int arity,
        params string[] parameterTypeMetadataNames)
    {
        return locatorSymbol.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method =>
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

    private static bool HasTryCreateViewExact(
        INamedTypeSymbol locatorSymbol,
        ICollection<Diagnostic> diagnostics)
    {
        var method = locatorSymbol.GetMembers("TryCreateViewExact")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(candidate =>
                candidate.MethodKind == MethodKind.Ordinary &&
                candidate.Arity == 0 &&
                candidate.Parameters.Length == 2 &&
                candidate.Parameters[0].RefKind == RefKind.None &&
                candidate.Parameters[1].RefKind == RefKind.Out &&
                string.Equals(GetTypeMetadataName(candidate.Parameters[0].Type), "System.Type", StringComparison.Ordinal) &&
                string.Equals(GetTypeMetadataName(candidate.Parameters[1].Type), AvaloniaControlMetadataName, StringComparison.Ordinal));

        if (method is null)
        {
            return false;
        }

        if (!HasValueReturnType(method, SpecialType.System_Boolean))
        {
            ReportIncompatibleMember(
                diagnostics,
                method,
                "TryCreateViewExact(Type, out Control?)",
                "a method returning bool");
        }

        return true;
    }

    private static bool HasDataTemplateBuildMethod(
        INamedTypeSymbol locatorSymbol,
        ICollection<Diagnostic> diagnostics)
    {
        var method = FindMethod(locatorSymbol, "Build", 0, "System.Object");
        if (method is null)
        {
            return false;
        }

        if (!IsPublicInstanceMethod(method) ||
            !HasValueReturnType(method, AvaloniaControlMetadataName))
        {
            ReportIncompatibleMember(
                diagnostics,
                method,
                "Build(object?)",
                "a public instance method returning Avalonia.Controls.Control?");
        }

        return true;
    }

    private static bool HasDataTemplateMatchMethod(
        INamedTypeSymbol locatorSymbol,
        ICollection<Diagnostic> diagnostics)
    {
        var method = FindMethod(locatorSymbol, "Match", 0, "System.Object");
        if (method is null)
        {
            return false;
        }

        if (!IsPublicInstanceMethod(method) || !HasValueReturnType(method, SpecialType.System_Boolean))
        {
            ReportIncompatibleMember(
                diagnostics,
                method,
                "Match(object?)",
                "a public instance method returning bool");
        }

        return true;
    }

    private static bool ValidateReactiveUIAdapterTypes(
        Compilation compilation,
        INamedTypeSymbol locatorSymbol,
        ICollection<Diagnostic> diagnostics,
        bool requested)
    {
        if (!requested)
        {
            return false;
        }

        return ValidateRequiredTypes(
            compilation,
            locatorSymbol,
            diagnostics,
            "GenerateIViewLocator",
            ReactiveUIViewLocatorMetadataName,
            ReactiveUIViewForUntypedMetadataName,
            ReactiveUIViewForMetadataName);
    }

    private static bool ValidateDataTemplateAdapterTypes(
        Compilation compilation,
        INamedTypeSymbol locatorSymbol,
        ICollection<Diagnostic> diagnostics,
        bool requested)
    {
        if (!requested)
        {
            return false;
        }

        return ValidateRequiredTypes(
            compilation,
            locatorSymbol,
            diagnostics,
            "GenerateIDataTemplate",
            AvaloniaControlMetadataName,
            AvaloniaDataTemplateMetadataName);
    }

    private static bool ValidateRequiredTypes(
        Compilation compilation,
        INamedTypeSymbol locatorSymbol,
        ICollection<Diagnostic> diagnostics,
        string optionName,
        params string[] metadataNames)
    {
        foreach (var metadataName in metadataNames)
        {
            if (compilation.GetTypeByMetadataName(metadataName) is not null)
            {
                continue;
            }

            diagnostics.Add(Diagnostic.Create(
                MissingAdapterType,
                GetDiagnosticLocation(locatorSymbol),
                optionName,
                metadataName));
            return false;
        }

        return true;
    }

    private static bool IsPublicInstanceMethod(IMethodSymbol method)
    {
        return method.DeclaredAccessibility == Accessibility.Public && !method.IsStatic;
    }

    private static bool HasValueReturnType(IMethodSymbol method, SpecialType specialType)
    {
        return !method.ReturnsByRef &&
               !method.ReturnsByRefReadonly &&
               method.ReturnType.SpecialType == specialType;
    }

    private static bool HasValueReturnType(IMethodSymbol method, string metadataName)
    {
        return !method.ReturnsByRef &&
               !method.ReturnsByRefReadonly &&
               string.Equals(GetTypeMetadataName(method.ReturnType), metadataName, StringComparison.Ordinal);
    }

    private static bool HasResolveViewMethod(
        INamedTypeSymbol locatorSymbol,
        ICollection<Diagnostic> diagnostics,
        int arity,
        string generatedMember,
        params string[] parameterTypeMetadataNames)
    {
        var method = FindMethod(locatorSymbol, "ResolveView", arity, parameterTypeMetadataNames);
        if (method is null)
        {
            return false;
        }

        var compatible = IsPublicInstanceMethod(method) &&
                         (arity == 0
                             ? HasValueReturnType(method, ReactiveUIViewForUntypedMetadataName)
                             : HasGenericViewForReturnType(method) && HasViewModelClassConstraint(method));
        if (!compatible)
        {
            ReportIncompatibleMember(
                diagnostics,
                method,
                generatedMember,
                arity == 0
                    ? "a public instance method returning ReactiveUI.IViewFor?"
                    : "a public instance method returning ReactiveUI.IViewFor<TViewModel>? with a 'class' constraint");
        }

        return true;
    }

    private static bool HasGenericViewForReturnType(IMethodSymbol method)
    {
        return !method.ReturnsByRef &&
               !method.ReturnsByRefReadonly &&
               method.TypeParameters.Length == 1 &&
               method.ReturnType is INamedTypeSymbol returnType &&
               string.Equals(
                   GetTypeMetadataName(returnType.OriginalDefinition),
                   ReactiveUIViewForMetadataName,
                   StringComparison.Ordinal) &&
               returnType.TypeArguments.Length == 1 &&
               SymbolEqualityComparer.Default.Equals(returnType.TypeArguments[0], method.TypeParameters[0]);
    }

    private static bool HasViewModelClassConstraint(IMethodSymbol method)
    {
        if (method.TypeParameters.Length != 1)
        {
            return false;
        }

        var typeParameter = method.TypeParameters[0];
        return typeParameter.HasReferenceTypeConstraint &&
               !typeParameter.HasValueTypeConstraint &&
               !typeParameter.HasUnmanagedTypeConstraint &&
               !typeParameter.HasConstructorConstraint &&
               typeParameter.ConstraintTypes.IsEmpty;
    }

    private static bool HasControlHook(
        Compilation compilation,
        INamedTypeSymbol locatorSymbol,
        ICollection<Diagnostic> diagnostics,
        string methodName,
        string generatedMember,
        params string[] parameterTypeMetadataNames)
    {
        var method = FindMethod(locatorSymbol, methodName, 0, parameterTypeMetadataNames);
        if (method is null)
        {
            return false;
        }

        var controlType = compilation.GetTypeByMetadataName(AvaloniaControlMetadataName);
        if (method.ReturnsByRef ||
            method.ReturnsByRefReadonly ||
            controlType is null ||
            !compilation.HasImplicitConversion(method.ReturnType, controlType))
        {
            ReportIncompatibleMember(
                diagnostics,
                method,
                generatedMember,
                "a method whose return type is implicitly convertible to Avalonia.Controls.Control?");
        }

        return true;
    }

    private static bool HasBooleanHook(
        INamedTypeSymbol locatorSymbol,
        ICollection<Diagnostic> diagnostics,
        string methodName,
        string generatedMember,
        params string[] parameterTypeMetadataNames)
    {
        var method = FindMethod(locatorSymbol, methodName, 0, parameterTypeMetadataNames);
        if (method is null)
        {
            return false;
        }

        if (!HasValueReturnType(method, SpecialType.System_Boolean))
        {
            ReportIncompatibleMember(diagnostics, method, generatedMember, "a method returning bool");
        }

        return true;
    }

    private static void ReportIncompatibleMember(
        ICollection<Diagnostic> diagnostics,
        IMethodSymbol method,
        string generatedMember,
        string expected)
    {
        diagnostics.Add(Diagnostic.Create(
            IncompatibleAdapterMember,
            GetDiagnosticLocation(method),
            method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            generatedMember,
            expected));
    }

    private static Location GetDiagnosticLocation(ISymbol symbol)
    {
        return symbol.Locations.FirstOrDefault(static location => location.IsInSource) ?? Location.None;
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
        var namespaceName = GetNamespaceName(namedType.ContainingNamespace);
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

    private static void AppendIViewLocator(
        StringBuilder source,
        INamedTypeSymbol locatorSymbol,
        ICollection<Diagnostic> diagnostics)
    {
        if (!HasResolveViewMethod(
                locatorSymbol,
                diagnostics,
                1,
                "ResolveView<TViewModel>()"))
        {
            source.Append(
                """

    public global::ReactiveUI.IViewFor<TViewModel>? ResolveView<TViewModel>()
        where TViewModel : class
    {
        return ResolveView<TViewModel>(null);
    }
""");
            source.AppendLine();
        }

        if (!HasResolveViewMethod(
                locatorSymbol,
                diagnostics,
                1,
                "ResolveView<TViewModel>(string?)",
                "System.String"))
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
            source.AppendLine();
        }

        if (!HasResolveViewMethod(
                locatorSymbol,
                diagnostics,
                0,
                "ResolveView(object?)",
                "System.Object"))
        {
            source.Append(
                """

    public global::ReactiveUI.IViewFor? ResolveView(object? instance)
    {
        return ResolveView(instance, null);
    }
""");
            source.AppendLine();
        }

        if (!HasResolveViewMethod(
                locatorSymbol,
                diagnostics,
                0,
                "ResolveView(object?, string?)",
                "System.Object",
                "System.String"))
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
            source.AppendLine();
        }
    }

    private static void AppendDataTemplateBuild(
        StringBuilder source,
        Compilation compilation,
        INamedTypeSymbol locatorSymbol,
        bool generateIViewLocator,
        ICollection<Diagnostic> diagnostics)
    {
        var hookModifier = GetHookModifier(locatorSymbol);
        source.Append(
            """

    public Control? Build(object? param)
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
        source.AppendLine();

        if (!HasControlHook(
                compilation,
                locatorSymbol,
                diagnostics,
                "BuildResolvedView",
                "BuildResolvedView(object?)",
                "System.Object"))
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
                source.AppendLine();
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
                source.AppendLine();
            }
        }

        if (!HasControlHook(
                compilation,
                locatorSymbol,
                diagnostics,
                "BuildFallbackView",
                "BuildFallbackView(object?)",
                "System.Object"))
        {
            source.Append(
                $$"""

    {{hookModifier}} Control? BuildFallbackView(object? param)
    {
        return null;
    }
""");
            source.AppendLine();
        }

        if (!HasControlHook(
                compilation,
                locatorSymbol,
                diagnostics,
                "BuildInvalidView",
                "BuildInvalidView(object?)",
                "System.Object"))
        {
            source.Append(
                $$"""

    {{hookModifier}} Control BuildInvalidView(object? param)
    {
        return new TextBlock { Text = "Invalid view model type." };
    }
""");
            source.AppendLine();
        }

        if (!HasControlHook(
                compilation,
                locatorSymbol,
                diagnostics,
                "BuildMissingView",
                "BuildMissingView(object?, Type)",
                "System.Object",
                "System.Type"))
        {
            source.Append(
                $$"""

    {{hookModifier}} Control BuildMissingView(object? param, Type viewModelType)
    {
        var message = s_missingViews.TryGetValue(viewModelType, out var missingView)
            ? missingView
            : $"View for {viewModelType.FullName} is not found.";
        return new TextBlock { Text = message };
    }
""");
            source.AppendLine();
        }
    }

    private static void AppendDataTemplateMatch(
        StringBuilder source,
        INamedTypeSymbol locatorSymbol,
        ICollection<Diagnostic> diagnostics)
    {
        var hookModifier = GetHookModifier(locatorSymbol);
        source.Append(
            """

    public bool Match(object? data)
    {
        return MatchDataTemplate(data);
    }
""");
        source.AppendLine();

        if (HasBooleanHook(
                locatorSymbol,
                diagnostics,
                "MatchDataTemplate",
                "MatchDataTemplate(object?)",
                "System.Object"))
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
            source.AppendLine();
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
        source.AppendLine();

        for (var index = 0; index < matchTypes.Length; index++)
        {
            var typeName = matchTypes[index].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            source.Append("            typeof(");
            source.Append(typeName);
            source.Append(").IsAssignableFrom(type)");
            source.AppendLine(index == matchTypes.Length - 1 ? ";" : " ||");
        }

        source.AppendLine("    }");
    }
}
