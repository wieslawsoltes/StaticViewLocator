using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace StaticViewLocator.Tests;

public class StaticViewLocatorGeneratorCombinedAdapterTests
{
    [AvaloniaFact]
    public void CombinedAdaptersBuildConventionOnlyAvaloniaViews()
    {
        const string source = """
using Avalonia.Controls;
using StaticViewLocator;

namespace ReactiveUI
{
    public interface IViewFor { object? ViewModel { get; set; } }
    public interface IViewFor<TViewModel> : IViewFor where TViewModel : class { }
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
    public sealed class PlainViewModel { }
}

namespace TestApp.Views
{
    public sealed class PlainView : UserControl { }
}

namespace TestApp
{
    [StaticViewLocator(
        GenerateIViewLocator = true,
        GenerateIDataTemplate = true,
        GenerateRuntimeTypeFallbackMethods = false)]
    public partial class ViewLocator { }
}
""";

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            assemblyName: "StaticViewLocatorCombinedAdapterTests",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references: ResolveReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new StaticViewLocatorGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        using var peStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(peStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        peStream.Position = 0;
        var assembly = Assembly.Load(peStream.ToArray());
        var locatorType = assembly.GetType("TestApp.ViewLocator", throwOnError: true)!;
        var viewModelType = assembly.GetType("TestApp.ViewModels.PlainViewModel", throwOnError: true)!;
        var viewModel = Activator.CreateInstance(viewModelType)!;
        var locator = Activator.CreateInstance(locatorType)!;

        var build = locatorType.GetMethod("Build", BindingFlags.Public | BindingFlags.Instance)!;
        var view = Assert.IsAssignableFrom<Control>(build.Invoke(locator, new[] { viewModel }));

        Assert.Equal("TestApp.Views.PlainView", view.GetType().FullName);
        Assert.Same(viewModel, view.DataContext);

        var resolveView = locatorType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method => method.Name == "ResolveView" && !method.IsGenericMethod && method.GetParameters().Length == 1);
        Assert.Null(resolveView.Invoke(locator, new[] { viewModel }));
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