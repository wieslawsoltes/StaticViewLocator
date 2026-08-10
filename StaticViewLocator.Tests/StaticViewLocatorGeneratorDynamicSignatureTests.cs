using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace StaticViewLocator.Tests;

public class StaticViewLocatorGeneratorDynamicSignatureTests
{
    [Fact]
    public void DynamicMethodsAreTreatedAsObjectSignatures()
    {
        const string source = """
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp
{
    [StaticViewLocator(
        GenerateIDataTemplate = true,
        GenerateRuntimeTypeFallbackMethods = false)]
    public partial class ViewLocator
    {
        public Control? Build(dynamic value) => null;
        public bool Match(dynamic value) => false;
        protected Control? BuildFallbackView(dynamic value) => null;
    }
}
""";

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            assemblyName: "StaticViewLocatorDynamicSignatureTests",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references: ResolveReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new StaticViewLocatorGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var errors = updatedCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(static error => error.ToString())));

        var generatedSource = driver.GetRunResult().GeneratedTrees
            .Single(tree => Path.GetFileName(tree.FilePath) == "ViewLocator_StaticViewLocator.cs")
            .GetText()
            .ToString();
        Assert.DoesNotContain("public Control Build(object? param)", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public bool Match(object? data)", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildFallbackView(object? param)", generatedSource, StringComparison.Ordinal);
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