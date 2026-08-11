using System;
using StaticViewLocator.Tests.TestHelpers;
using Xunit;

namespace StaticViewLocator.Tests;

public class StaticViewLocatorGeneratorConstructedGenericMappingTests
{
    [Fact]
    public async Task ReactiveUIMappingPreservesConstructedGenericViewModelArguments()
    {
        const string source = """
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
    public sealed class Item { }
    public sealed class GenericViewModel<T> { }

    public sealed class ClosedGenericScreen : UserControl, ReactiveUI.IViewFor<GenericViewModel<Item>>
    {
        public object? ViewModel { get; set; }
    }

    [StaticViewLocator(
        GenerateIViewLocator = true,
        GenerateRuntimeTypeFallbackMethods = false)]
    public partial class ViewLocator { }
}
""";

        var generated = await StaticViewLocatorGeneratorVerifier.GetGeneratedSourcesAsync(source);
        var locatorSource = generated["ViewLocator_StaticViewLocator.cs"];

        Assert.Contains(
            "[typeof(global::TestApp.GenericViewModel<global::TestApp.Item>)] = () => new TestApp.ClosedGenericScreen()",
            locatorSource,
            StringComparison.Ordinal);

    }
}
