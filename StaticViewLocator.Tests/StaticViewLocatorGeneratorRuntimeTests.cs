using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using StaticViewLocator;
using Xunit;
using PackageIdentity = Microsoft.CodeAnalysis.Testing.PackageIdentity;

namespace StaticViewLocator.Tests;

public class StaticViewLocatorGeneratorRuntimeTests
{
    private static readonly ReferenceAssemblies s_referenceAssemblies = ReferenceAssemblies.Net.Net80.AddPackages(
        ImmutableArray.Create(
            new PackageIdentity("Avalonia", "11.2.5")));

    [AvaloniaFact]
    public async Task CreatesRegisteredViewInstances()
    {
        const string source = @"
using System;
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp
{
    [StaticViewLocator]
    public partial class ViewLocator
    {
        public static Control Resolve(object vm)
        {
            if (vm is null)
            {
                throw new ArgumentNullException(nameof(vm));
            }

            return s_views[vm.GetType()]();
        }
    }
}

namespace TestApp.ViewModels
{
    public class SampleViewModel
    {
    }

    public class MissingViewModel
    {
    }
}

namespace TestApp.Views
{
    public class SampleView : UserControl
    {
    }
}
";

        var compilation = await CreateCompilationAsync(source);
        var sourceGenerator = new StaticViewLocatorGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(new[] { sourceGenerator }, parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        using var peStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(peStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        peStream.Seek(0, SeekOrigin.Begin);
        var assembly = Assembly.Load(peStream.ToArray());

        var locatorType = assembly.GetType("TestApp.ViewLocator") ?? throw new InvalidOperationException("Generated locator type not found.");
        var resolveMethod = locatorType.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static) ?? throw new InvalidOperationException("Resolve method not found.");
        var sampleViewModel = assembly.CreateInstance("TestApp.ViewModels.SampleViewModel") ?? throw new InvalidOperationException("Unable to create SampleViewModel instance.");
        var missingViewModel = assembly.CreateInstance("TestApp.ViewModels.MissingViewModel") ?? throw new InvalidOperationException("Unable to create MissingViewModel instance.");

        _ = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(StaticViewLocatorGeneratorRuntimeTests).Assembly);

        var sampleControl = (Control)resolveMethod.Invoke(null, new[] { sampleViewModel })!;
        var missingControl = (Control)resolveMethod.Invoke(null, new[] { missingViewModel })!;

        Assert.Equal("TestApp.Views.SampleView", sampleControl.GetType().FullName);
        Assert.Equal("Avalonia.Controls.TextBlock", missingControl.GetType().FullName);
    }

    private static async Task<CSharpCompilation> CreateCompilationAsync(string source)
    {
        var parseOptions = new CSharpParseOptions();
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var references = await s_referenceAssemblies.ResolveAsync(LanguageNames.CSharp, default);

        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
