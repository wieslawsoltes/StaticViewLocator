using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StaticViewLocator;
using Xunit;

namespace StaticViewLocator.Tests.TestHelpers;

internal static class StaticViewLocatorGeneratorVerifier
{
    public static Task VerifyGeneratedSourcesAsync(string source, params (string hintName, string source)[] generatedSources)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var compilation = CSharpCompilation.Create(
            assemblyName: "StaticViewLocatorGenerator.Tests",
            syntaxTrees: new[] { syntaxTree },
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new StaticViewLocatorGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator }, parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        var failures = diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (failures.Length > 0)
        {
            var message = string.Join(Environment.NewLine, failures.Select(static d => d.ToString()));
            throw new Xunit.Sdk.XunitException($"Generator reported diagnostics:{Environment.NewLine}{message}");
        }

        var runResult = driver.GetRunResult();
        var generated = runResult.GeneratedTrees
            .Select(static tree => (HintName: Path.GetFileName(tree.FilePath) ?? string.Empty, Source: tree.GetText().ToString()))
            .ToDictionary(static x => x.HintName, static x => x.Source, StringComparer.Ordinal);

        foreach (var (hintName, expectedSource) in generatedSources)
        {
            if (!generated.TryGetValue(hintName, out var actualSource))
            {
                throw new Xunit.Sdk.XunitException($"Generator did not produce hint '{hintName}'. Generated hints: {string.Join(", ", generated.Keys)}");
            }

            Assert.Equal(expectedSource, actualSource);
        }

        var unexpected = generated.Keys.Except(generatedSources.Select(static g => g.hintName), StringComparer.Ordinal).ToArray();
        if (unexpected.Length > 0)
        {
            throw new Xunit.Sdk.XunitException($"Generator produced unexpected hints: {string.Join(", ", unexpected)}");
        }

        return Task.CompletedTask;
    }

    private static IReadOnlyCollection<MetadataReference> GetMetadataReferences()
    {
        var references = new List<MetadataReference>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;

        foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                continue;
            }

            if (unique.Add(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        foreach (var assembly in GetAdditionalAssemblies())
        {
            if (string.IsNullOrEmpty(assembly?.Location) || !unique.Add(assembly.Location))
            {
                continue;
            }

            references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        return references;
    }

    private static IEnumerable<Assembly> GetAdditionalAssemblies()
    {
        yield return typeof(Control).Assembly;
        yield return typeof(UserControl).Assembly;
        yield return typeof(StaticViewLocatorGenerator).Assembly;
    }
}
