using System.Collections.Immutable;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using Microsoft.CodeAnalysis.Text;
using StaticViewLocator;
using PackageIdentity = Microsoft.CodeAnalysis.Testing.PackageIdentity;

namespace StaticViewLocator.Tests.TestHelpers;

internal static class StaticViewLocatorGeneratorVerifier
{
    private static readonly ReferenceAssemblies s_referenceAssemblies = ReferenceAssemblies.Net.Net80.AddPackages(
        ImmutableArray.Create(
            new PackageIdentity("Avalonia", "11.2.5")));

    public static async Task VerifyGeneratedSourcesAsync(string source, params (string hintName, string source)[] generatedSources)
    {
        var test = new Test();
        test.TestState.Sources.Add(source);

        foreach (var (hintName, generatedSource) in generatedSources)
        {
            test.TestState.GeneratedSources.Add((typeof(StaticViewLocatorGenerator), hintName, SourceText.From(generatedSource, Encoding.UTF8)));
        }

        await test.RunAsync();
    }

    private sealed class Test : CSharpSourceGeneratorTest<StaticViewLocatorGenerator, XUnitVerifier>
    {
        public Test()
        {
            ReferenceAssemblies = s_referenceAssemblies;
        }
    }
}
