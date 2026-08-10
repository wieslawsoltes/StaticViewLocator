using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StaticViewLocator.Tests.TestHelpers;
using Xunit;

namespace StaticViewLocator.Tests;

public class StaticViewLocatorGeneratorConstructedGenericMappingTests
{
    [Fact]
    public async Task ReactiveUIMappingPreservesConstructedGenericViewModelArguments()
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

namespace TestApp
{
    public sealed class Item { }
    public sealed class GenericViewModel<T> { }

    public sealed class ClosedGenericScreen : UserControl, ReactiveUI.IViewFor<GenericViewModel<Item>>
    {
        public object? ViewModel { get; set; }
    }

    [StaticViewLocator(
        GenerateIViewLocator = true,
        GenerateRuntimeTypeFallbackMethods = false)]
    public partial class ViewLocator { }
}
""";

        var generated = await StaticViewLocatorGeneratorVerifier.GetGeneratedSourcesAsync(source);
        var locatorSource = generated["ViewLocator_StaticViewLocator.cs"];

        Assert.Contains(
            "[typeof(global::TestApp.GenericViewModel<global::TestApp.Item>)] = () => new TestApp.ClosedGenericScreen()",
            locatorSource,
            StringComparison.Ordinal);

        AssertCompiles(source);
    }

    private static void AssertCompiles(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            assemblyName: "StaticViewLocatorConstructedGenericMappingTests",
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
        var errors = updatedCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(static error => error.ToString())));
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