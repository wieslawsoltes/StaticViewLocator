using System;
using System.Linq;
using System.Threading.Tasks;
using StaticViewLocator.Tests.TestHelpers;
using Xunit;

namespace StaticViewLocator.Tests;

public class StaticViewLocatorGeneratorDynamicSignatureTests
{
    [Fact]
    public async Task DynamicMethodsAreTreatedAsObjectSignatures()
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

        var generated = await StaticViewLocatorGeneratorVerifier.GetGeneratedSourcesAsync(source);
        var generatedSource = generated["ViewLocator_StaticViewLocator.cs"];
        Assert.DoesNotContain("public Control Build(object? param)", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public bool Match(object? data)", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildFallbackView(object? param)", generatedSource, StringComparison.Ordinal);
    }
}
