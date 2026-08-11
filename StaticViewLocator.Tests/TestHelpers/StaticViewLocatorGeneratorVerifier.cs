using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using StaticViewLocator;
using Xunit;

namespace StaticViewLocator.Tests.TestHelpers;

internal static class StaticViewLocatorGeneratorVerifier
{
    public static Task<IReadOnlyDictionary<string, string>> GetGeneratedSourcesAsync(
        string source,
        IReadOnlyDictionary<string, string>? globalOptions = null)
    {
        var result = RunGenerator(source, globalOptions);
        AssertNoErrors(result);
        var generated = result.RunResult.GeneratedTrees
            .Select(static tree => (HintName: Path.GetFileName(tree.FilePath) ?? string.Empty, Source: tree.GetText().ToString()))
            .ToDictionary(static x => x.HintName, static x => x.Source, StringComparer.Ordinal);

        return Task.FromResult<IReadOnlyDictionary<string, string>>(generated);
    }

    public static Task VerifyGeneratedSourcesAsync(string source, params (string hintName, string source)[] generatedSources)
    {
        return VerifyGeneratedSourcesAsync(source, globalOptions: null, generatedSources);
    }

    public static Task VerifyGeneratedSourcesAsync(
        string source,
        IReadOnlyDictionary<string, string>? globalOptions = null,
        params (string hintName, string source)[] generatedSources)
    {
        var result = RunGenerator(source, globalOptions);
        AssertNoErrors(result);
        var generated = result.RunResult.GeneratedTrees
            .Select(static tree => (HintName: Path.GetFileName(tree.FilePath) ?? string.Empty, Source: tree.GetText().ToString()))
            .ToDictionary(static x => x.HintName, static x => x.Source, StringComparer.Ordinal);

        foreach (var (hintName, expectedSource) in generatedSources)
        {
            if (!generated.TryGetValue(hintName, out var actualSource))
            {
                throw new Xunit.Sdk.XunitException($"Generator did not produce hint '{hintName}'. Generated hints: {string.Join(", ", generated.Keys)}");
            }

            Assert.Equal(NormalizeExpectedSource(hintName, expectedSource), actualSource);
        }

        var unexpected = generated.Keys.Except(generatedSources.Select(static g => g.hintName), StringComparer.Ordinal).ToArray();
        if (unexpected.Length > 0)
        {
            throw new Xunit.Sdk.XunitException($"Generator produced unexpected hints: {string.Join(", ", unexpected)}");
        }

        return Task.CompletedTask;
    }

    public static GeneratorTestResult RunGenerator(
        string source,
        IReadOnlyDictionary<string, string>? globalOptions = null)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            assemblyName: "StaticViewLocatorGenerator.Tests",
            syntaxTrees: new[] { syntaxTree },
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new StaticViewLocatorGenerator().AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: parseOptions,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(globalOptions));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);
        return new GeneratorTestResult(driver.GetRunResult(), updatedCompilation, generatorDiagnostics);
    }

    private static void AssertNoErrors(GeneratorTestResult result)
    {
        var failures = result.GeneratorDiagnostics
            .Concat(result.Compilation.GetDiagnostics())
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (failures.Length == 0)
        {
            return;
        }

        var message = string.Join(Environment.NewLine, failures.Select(static diagnostic => diagnostic.ToString()));
        throw new Xunit.Sdk.XunitException($"Generated compilation failed:{Environment.NewLine}{message}");
    }

    internal sealed class GeneratorTestResult
    {
        public GeneratorTestResult(
            GeneratorDriverRunResult runResult,
            Compilation compilation,
            ImmutableArray<Diagnostic> generatorDiagnostics)
        {
            RunResult = runResult;
            Compilation = compilation;
            GeneratorDiagnostics = generatorDiagnostics;
        }

        public GeneratorDriverRunResult RunResult { get; }

        public Compilation Compilation { get; }

        public ImmutableArray<Diagnostic> GeneratorDiagnostics { get; }
    }

    private static string NormalizeExpectedSource(string hintName, string expectedSource)
    {
        if (!string.Equals(hintName, "StaticViewLocatorAttribute.cs", StringComparison.Ordinal) ||
            expectedSource.Contains("GenerateIViewLocator", StringComparison.Ordinal))
        {
            return expectedSource;
        }

        var newLine = expectedSource.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var existing =
            $"    public bool GenerateRuntimeTypeFallbackMethods {{ get; set; }} = true;{newLine}{newLine}" +
            "    public Type[] ViewModelMappingContracts { get; set; } = Array.Empty<Type>();";
        var extended =
            $"    public bool GenerateRuntimeTypeFallbackMethods {{ get; set; }} = true;{newLine}{newLine}" +
            $"    public bool GenerateIViewLocator {{ get; set; }}{newLine}{newLine}" +
            $"    public bool GenerateIDataTemplate {{ get; set; }}{newLine}{newLine}" +
            $"    public Type[] ViewModelMappingContracts {{ get; set; }} = Array.Empty<Type>();{newLine}{newLine}" +
            "    public Type[] DataTemplateMatchTypes { get; set; } = Array.Empty<Type>();";

        return expectedSource.Replace(existing, extended, StringComparison.Ordinal);
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions EmptyOptions = new TestAnalyzerConfigOptions(null);
        private readonly AnalyzerConfigOptions _globalOptions;

        public TestAnalyzerConfigOptionsProvider(IReadOnlyDictionary<string, string>? globalOptions)
        {
            _globalOptions = new TestAnalyzerConfigOptions(globalOptions);
        }

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => EmptyOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => EmptyOptions;
    }

    private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> _options;

        public TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string>? options)
        {
            _options = options ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        public override bool TryGetValue(string key, out string value)
        {
            return _options.TryGetValue(key, out value!);
        }
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
