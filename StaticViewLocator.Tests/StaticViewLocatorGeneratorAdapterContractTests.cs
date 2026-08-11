using System;
using System.Linq;
using StaticViewLocator.Tests.TestHelpers;
using Xunit;

namespace StaticViewLocator.Tests;

public class StaticViewLocatorGeneratorAdapterContractTests
{
    [Fact]
    public void MissingReactiveUIReferenceProducesTargetedDiagnostic()
    {
        const string source = """
using StaticViewLocator;

namespace TestApp;

[StaticViewLocator(GenerateIViewLocator = true)]
public partial class ViewLocator { }
""";

        var result = StaticViewLocatorGeneratorVerifier.RunGenerator(source);
        var diagnostic = Assert.Single(result.GeneratorDiagnostics, static item => item.Id == "SVL0001");
        Assert.Contains("ReactiveUI.IViewLocator", diagnostic.GetMessage(), StringComparison.Ordinal);

        var generatedSource = result.RunResult.GeneratedTrees
            .Single(tree => tree.FilePath.EndsWith("ViewLocator_StaticViewLocator.cs", StringComparison.Ordinal))
            .GetText()
            .ToString();
        Assert.DoesNotContain("global::ReactiveUI", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void IncompatibleDataTemplateMembersProduceTargetedDiagnostics()
    {
        const string source = """
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp;

[StaticViewLocator(GenerateIDataTemplate = true)]
public partial class ViewLocator
{
    private Control? Build(object? value) => null;
    public int Match(object? value) => 0;
}
""";

        var result = StaticViewLocatorGeneratorVerifier.RunGenerator(source);
        var diagnostics = result.GeneratorDiagnostics.Where(static item => item.Id == "SVL0002").ToArray();

        Assert.Equal(2, diagnostics.Length);
        Assert.Contains(diagnostics, static item => item.GetMessage().Contains("Build(object?)", StringComparison.Ordinal));
        Assert.Contains(diagnostics, static item => item.GetMessage().Contains("Match(object?)", StringComparison.Ordinal));
    }

    [Fact]
    public void IncompatibleExactFactoryAndHookProduceTargetedDiagnostics()
    {
        const string source = """
using System;
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp;

[StaticViewLocator(GenerateIDataTemplate = true)]
public partial class ViewLocator
{
    private static int TryCreateViewExact(Type type, out Control? view)
    {
        view = null;
        return 0;
    }

    protected object? BuildFallbackView(object? value) => null;
}
""";

        var result = StaticViewLocatorGeneratorVerifier.RunGenerator(source);
        var diagnostics = result.GeneratorDiagnostics.Where(static item => item.Id == "SVL0002").ToArray();

        Assert.Equal(2, diagnostics.Length);
        Assert.Contains(diagnostics, static item => item.GetMessage().Contains("TryCreateViewExact", StringComparison.Ordinal));
        Assert.Contains(diagnostics, static item => item.GetMessage().Contains("BuildFallbackView", StringComparison.Ordinal));
    }

    [Fact]
    public void IncompatibleReactiveUIResolveMethodProducesTargetedDiagnostic()
    {
        const string source = """
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
    [StaticViewLocator(GenerateIViewLocator = true)]
    public partial class ViewLocator
    {
        public object? ResolveView<TViewModel>() where TViewModel : class => null;
    }
}
""";

        var result = StaticViewLocatorGeneratorVerifier.RunGenerator(source);
        var diagnostic = Assert.Single(result.GeneratorDiagnostics, static item => item.Id == "SVL0002");
        Assert.Contains("ResolveView<TViewModel>()", diagnostic.GetMessage(), StringComparison.Ordinal);
    }
}
