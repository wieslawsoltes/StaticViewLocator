using System;
using System.Threading.Tasks;
using StaticViewLocator.Tests.TestHelpers;
using Xunit;

namespace StaticViewLocator.Tests;

public class StaticViewLocatorGeneratorDataTemplateMatchTests
{
    [Fact]
    public async Task DefaultGeneratedMatchUsesSameExactSemanticsAsGeneratedBuild()
    {
        const string source = """
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp.ViewModels
{
    public sealed class WidgetViewModel<T> { }
}


namespace TestApp.Views
{
    public sealed class WidgetView : UserControl { }
}

namespace TestApp
{
    [StaticViewLocator(GenerateIDataTemplate = true)]
    public partial class ViewLocator { }
}
""";

        var generated = await StaticViewLocatorGeneratorVerifier.GetGeneratedSourcesAsync(source);
        var locatorSource = generated["ViewLocator_StaticViewLocator.cs"];

        Assert.Contains(
            "[typeof(TestApp.ViewModels.WidgetViewModel<>)] = () => new TestApp.Views.WidgetView()",
            locatorSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "return s_views.ContainsKey(type) || s_missingViews.ContainsKey(type);",
            locatorSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("GetGenericTypeDefinition()", locatorSource, StringComparison.Ordinal);
    }
}
