using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace StaticViewLocator.Tests;

public class StaticViewLocatorGeneratorSealedLocatorTests
{
    [Fact]
    public void SealedLocatorCompilesWithGeneratedDataTemplateHooks()
    {
        const string source = """
using StaticViewLocator;

namespace TestApp
{
    [StaticViewLocator(
        GenerateIDataTemplate = true,
        GenerateRuntimeTypeFallbackMethods = false)]
    public sealed partial class ViewLocator { }
}
""";

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            assemblyName: "StaticViewLocatorSealedLocatorTests",
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
        Assert.Contains("private Control? BuildResolvedView", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("protected virtual", generatedSource, StringComparison.Ordinal);
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