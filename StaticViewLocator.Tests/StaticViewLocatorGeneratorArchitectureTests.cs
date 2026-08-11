using System;
using System.Linq;
using System.Threading.Tasks;
using StaticViewLocator.Tests.TestHelpers;
using Xunit;

namespace StaticViewLocator.Tests;

public class StaticViewLocatorGeneratorArchitectureTests
{
    [Fact]
    public async Task SupportsInternalLocatorInGlobalNamespace()
    {
        const string source = """
using StaticViewLocator;

[StaticViewLocator(GenerateIDataTemplate = true)]
internal partial class ViewLocator { }
""";

        var generated = await StaticViewLocatorGeneratorVerifier.GetGeneratedSourcesAsync(source);
        var locatorSource = generated["ViewLocator_StaticViewLocator.cs"];

        Assert.Contains("internal partial class ViewLocator", locatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace ;", locatorSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UsesUniqueHintNamesForSameNamedLocators()
    {
        const string source = """
using StaticViewLocator;

namespace First
{
    [StaticViewLocator]
    public partial class ViewLocator { }
}

namespace Second
{
    [StaticViewLocator]
    public partial class ViewLocator { }
}
""";

        var generated = await StaticViewLocatorGeneratorVerifier.GetGeneratedSourcesAsync(source);

        Assert.Contains("First.ViewLocator_StaticViewLocator.cs", generated.Keys);
        Assert.Contains("Second.ViewLocator_StaticViewLocator.cs", generated.Keys);
    }

    [Fact]
    public void ReportsMappedViewWithoutUsableConstructor()
    {
        const string source = """
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp.ViewModels
{
    public sealed class SampleViewModel { }
}

namespace TestApp.Views
{
    public sealed class SampleView : UserControl
    {
        private SampleView() { }
    }
}

namespace TestApp
{
    [StaticViewLocator(GenerateIDataTemplate = true)]
    public partial class ViewLocator { }
}
""";

        var result = StaticViewLocatorGeneratorVerifier.RunGenerator(source);
        var diagnostic = Assert.Single(result.GeneratorDiagnostics, static item => item.Id == "SVL0004");
        Assert.Contains("SampleView", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsNonPartialLocatorBeforeEmission()
    {
        const string source = """
using StaticViewLocator;

namespace TestApp;

[StaticViewLocator]
public class ViewLocator { }
""";

        var result = StaticViewLocatorGeneratorVerifier.RunGenerator(source);
        var diagnostic = Assert.Single(result.GeneratorDiagnostics, static item => item.Id == "SVL0005");
        Assert.Contains("not partial", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("ViewLocator_StaticViewLocator.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsAmbiguousAutomaticReactiveUIMapping()
    {
        var result = StaticViewLocatorGeneratorVerifier.RunGenerator(CreateAmbiguousReactiveUISource(false));
        var diagnostic = Assert.Single(result.GeneratorDiagnostics, static item => item.Id == "SVL0003");

        Assert.Contains("FirstView", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("SecondView", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAmbiguousConfiguredContractMapping()
    {
        const string source = """
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp;

public interface IViewFor<TViewModel> { }
public sealed class SampleModel { }
public sealed class FirstView : UserControl, IViewFor<SampleModel> { }
public sealed class SecondView : UserControl, IViewFor<SampleModel> { }

[StaticViewLocator(ViewModelMappingContracts = new[] { typeof(IViewFor<>) })]
public partial class ViewLocator { }
""";

        var result = StaticViewLocatorGeneratorVerifier.RunGenerator(source);
        var diagnostic = Assert.Single(result.GeneratorDiagnostics, static item => item.Id == "SVL0003");

        Assert.Contains("FirstView", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("SecondView", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitMappingResolvesAutomaticReactiveUIAmbiguity()
    {
        var generated = await StaticViewLocatorGeneratorVerifier.GetGeneratedSourcesAsync(
            CreateAmbiguousReactiveUISource(true));
        var locatorSource = generated["ViewLocator_StaticViewLocator.cs"];

        Assert.Contains("new TestApp.FirstView()", locatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new TestApp.SecondView()", locatorSource, StringComparison.Ordinal);
    }

    private static string CreateAmbiguousReactiveUISource(bool addExplicitMapping)
    {
        var explicitMapping = addExplicitMapping
            ? "[StaticViewMapping(typeof(SampleViewModel), typeof(FirstView))]"
            : string.Empty;
        return $$"""
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
    public sealed class SampleViewModel { }

    public sealed class FirstView : UserControl, ReactiveUI.IViewFor<SampleViewModel>
    {
        public object? ViewModel { get; set; }
    }

    public sealed class SecondView : UserControl, ReactiveUI.IViewFor<SampleViewModel>
    {
        public object? ViewModel { get; set; }
    }

    [StaticViewLocator(GenerateIViewLocator = true)]
    {{explicitMapping}}
    public partial class ViewLocator { }
}
""";
    }
}
