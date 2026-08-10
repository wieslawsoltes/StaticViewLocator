using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StaticViewLocator;
using Xunit;

namespace StaticViewLocator.Tests;

public class StaticViewLocatorGeneratorIntegrationRuntimeTests
{
    [AvaloniaFact]
    public async Task GeneratedReactiveUIAndDataTemplateAdaptersWorkAtRuntime()
    {
        const string source = """
using System;
using Avalonia.Controls;
using StaticViewLocator;

namespace ReactiveUI
{
    public interface IViewFor
    {
        object? ViewModel { get; set; }
    }

    public interface IViewFor<TViewModel> : IViewFor
        where TViewModel : class
    {
        new TViewModel? ViewModel { get; set; }
    }

    public interface IViewLocator
    {
        IViewFor<TViewModel>? ResolveView<TViewModel>() where TViewModel : class;
        IViewFor<TViewModel>? ResolveView<TViewModel>(string? contract) where TViewModel : class;
        IViewFor? ResolveView(object? instance);
        IViewFor? ResolveView(object? instance, string? contract);
    }
}

namespace TestApp.ViewModels
{
    public abstract class ViewModelBase { }

    public sealed class DashboardViewModel : ViewModelBase { }

    public sealed class ContextHost
    {
        public ContextHost(ViewModelBase context) => Context = context;
        public ViewModelBase Context { get; }
    }
}

namespace TestApp.Views
{
    public sealed class DashboardScreen : UserControl, ReactiveUI.IViewFor<TestApp.ViewModels.DashboardViewModel>
    {
        public TestApp.ViewModels.DashboardViewModel? ViewModel { get; set; }

        object? ReactiveUI.IViewFor.ViewModel
        {
            get => ViewModel;
            set => ViewModel = (TestApp.ViewModels.DashboardViewModel?)value;
        }
    }
}

namespace TestApp
{
    [StaticViewLocator(
        GenerateIViewLocator = true,
        GenerateIDataTemplate = true,
        GenerateRuntimeTypeFallbackMethods = false,
        DataTemplateMatchTypes = new[]
        {
            typeof(ViewModels.ViewModelBase),
            typeof(ViewModels.ContextHost),
        })]
    public partial class ViewLocator
    {
        protected virtual Control? BuildFallbackView(object? param)
        {
            return param is ViewModels.ContextHost
                ? new Border { Tag = "context-fallback" }
                : null;
        }
    }
}
""";

        var compilation = CreateCompilation(source);
        var sourceGenerator = new StaticViewLocatorGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(
            new[] { sourceGenerator },
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        using var peStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(peStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        peStream.Position = 0;
        var assembly = Assembly.Load(peStream.ToArray());
        var locatorType = assembly.GetType("TestApp.ViewLocator", throwOnError: true)!;
        var dashboardViewModelType = assembly.GetType("TestApp.ViewModels.DashboardViewModel", throwOnError: true)!;
        var viewModelBaseType = assembly.GetType("TestApp.ViewModels.ViewModelBase", throwOnError: true)!;
        var contextHostType = assembly.GetType("TestApp.ViewModels.ContextHost", throwOnError: true)!;

        Assert.Contains(locatorType.GetInterfaces(), static type => type.FullName == "ReactiveUI.IViewLocator");
        Assert.Contains(locatorType.GetInterfaces(), static type => type.FullName == "Avalonia.Controls.Templates.IDataTemplate");

        var locator = Activator.CreateInstance(locatorType)!;
        var dashboardViewModel = Activator.CreateInstance(dashboardViewModelType)!;
        var contextHost = Activator.CreateInstance(contextHostType, dashboardViewModel)!;

        var build = locatorType.GetMethod("Build", BindingFlags.Public | BindingFlags.Instance)!;
        var dashboardView = (Control)build.Invoke(locator, new[] { dashboardViewModel })!;
        Assert.Equal("TestApp.Views.DashboardScreen", dashboardView.GetType().FullName);
        Assert.Same(dashboardViewModel, dashboardView.GetType().GetProperty("ViewModel")!.GetValue(dashboardView));

        var fallback = Assert.IsType<Border>(build.Invoke(locator, new[] { contextHost }));
        Assert.Equal("context-fallback", fallback.Tag);

        var match = locatorType.GetMethod("Match", BindingFlags.Public | BindingFlags.Instance)!;
        Assert.True((bool)match.Invoke(locator, new[] { dashboardViewModel })!);
        Assert.True((bool)match.Invoke(locator, new[] { contextHost })!);
        Assert.False((bool)match.Invoke(locator, new object?[] { new object() })!);
        Assert.False((bool)match.Invoke(locator, new object?[] { null })!);

        var runtimeResolve = locatorType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method => method.Name == "ResolveView" && !method.IsGenericMethod && method.GetParameters().Length == 1);
        var resolvedView = runtimeResolve.Invoke(locator, new[] { dashboardViewModel });
        Assert.NotNull(resolvedView);
        Assert.Equal("TestApp.Views.DashboardScreen", resolvedView!.GetType().FullName);
        Assert.Same(dashboardViewModel, resolvedView.GetType().GetProperty("ViewModel")!.GetValue(resolvedView));

        var genericResolve = locatorType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method => method.Name == "ResolveView" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
            .MakeGenericMethod(dashboardViewModelType);
        var genericView = genericResolve.Invoke(locator, Array.Empty<object>());
        Assert.NotNull(genericView);
        Assert.Equal("TestApp.Views.DashboardScreen", genericView!.GetType().FullName);

        var invalid = Assert.IsType<TextBlock>(build.Invoke(locator, new object?[] { null }));
        Assert.Equal("Invalid view model Type", invalid.Text);

        Assert.True(viewModelBaseType.IsAssignableFrom(dashboardViewModelType));
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        return CSharpCompilation.Create(
            assemblyName: "StaticViewLocatorIntegrationRuntimeTests",
            syntaxTrees: new[] { syntaxTree },
            references: ResolveReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IReadOnlyCollection<MetadataReference> ResolveReferences()
    {
        var references = new List<MetadataReference>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;

        foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || !unique.Add(path))
            {
                continue;
            }

            references.Add(MetadataReference.CreateFromFile(path));
        }

        foreach (var assembly in new[]
                 {
                     typeof(Control).Assembly,
                     typeof(UserControl).Assembly,
                     typeof(StaticViewLocatorGenerator).Assembly,
                 })
        {
            if (!string.IsNullOrEmpty(assembly.Location) && unique.Add(assembly.Location))
            {
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
        }

        return references;
    }
}
