using System;
using System.Threading.Tasks;
using StaticViewLocator.Tests.TestHelpers;
using Xunit;

namespace StaticViewLocator.Tests;

public class StaticViewLocatorGeneratorSealedLocatorTests
{
    [Fact]
    public async Task SealedLocatorCompilesWithGeneratedDataTemplateHooks()
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

        var generated = await StaticViewLocatorGeneratorVerifier.GetGeneratedSourcesAsync(source);
        var generatedSource = generated["ViewLocator_StaticViewLocator.cs"];
        Assert.Contains("private Control? BuildResolvedView", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("protected virtual", generatedSource, StringComparison.Ordinal);
    }
}
