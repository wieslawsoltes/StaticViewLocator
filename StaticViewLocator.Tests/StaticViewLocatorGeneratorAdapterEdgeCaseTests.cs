using System;
using System.Threading.Tasks;
using StaticViewLocator.Tests.TestHelpers;
using Xunit;

namespace StaticViewLocator.Tests;

public class StaticViewLocatorGeneratorAdapterEdgeCaseTests
{
    [Fact]
    public async Task UnrelatedOverloadsDoNotSuppressGeneratedAdapterMembers()
    {
        const string source = """
using Avalonia.Controls;
using StaticViewLocator;

namespace ReactiveUI
{
    public interface IViewFor
    {
        object? ViewModel { get; set; }
    }

    public interface IViewFor<TViewModel> : IViewFor
        where TViewModel : class
    {
        new TViewModel? ViewModel { get; set; }
    }

    public interface IViewLocator
    {
        IViewFor<TViewModel>? ResolveView<TViewModel>() where TViewModel : class;
        IViewFor<TViewModel>? ResolveView<TViewModel>(string? contract) where TViewModel : class;
        IViewFor? ResolveView(object? instance);
        IViewFor? ResolveView(object? instance, string? contract);
    }
}

namespace TestApp.ViewModels
{
    public sealed class SampleViewModel { }
}

namespace TestApp.Views
{
    public sealed class SampleView : UserControl, ReactiveUI.IViewFor<TestApp.ViewModels.SampleViewModel>
    {
        public TestApp.ViewModels.SampleViewModel? ViewModel { get; set; }

        object? ReactiveUI.IViewFor.ViewModel
        {
            get => ViewModel;
            set => ViewModel = (TestApp.ViewModels.SampleViewModel?)value;
        }
    }
}

namespace TestApp
{
    [StaticViewLocator(
        GenerateIViewLocator = true,
        GenerateIDataTemplate = true,
        GenerateRuntimeTypeFallbackMethods = false)]
    public partial class ViewLocator
    {
        public Control? Build(string? value) => null;
        public bool Match(string? value) => false;
        public ReactiveUI.IViewFor? ResolveView(string? value) => null;
        protected Control? BuildFallbackView(string? value) => null;
    }
}
""";

        var generated = await StaticViewLocatorGeneratorVerifier.GetGeneratedSourcesAsync(source);
        var locatorSource = generated["ViewLocator_StaticViewLocator.cs"];

        Assert.Contains("public Control? Build(object? param)", locatorSource, StringComparison.Ordinal);
        Assert.Contains("public bool Match(object? data)", locatorSource, StringComparison.Ordinal);
        Assert.Contains("ResolveView(object? instance)", locatorSource, StringComparison.Ordinal);
        Assert.Contains("protected virtual Control? BuildFallbackView(object? param)", locatorSource, StringComparison.Ordinal);

    }

    [Fact]
    public async Task ConfiguredMappingContractTakesPrecedenceOverAutomaticReactiveUIMapping()
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
    public interface IConfiguredView<TViewModel> { }

    public sealed class DashboardModel { }

    public sealed class ConfiguredDashboardView : UserControl, IConfiguredView<DashboardModel> { }

    public sealed class ReactiveDashboardView : UserControl, ReactiveUI.IViewFor<DashboardModel>
    {
        public object? ViewModel { get; set; }
    }

    [StaticViewLocator(
        GenerateIViewLocator = true,
        GenerateRuntimeTypeFallbackMethods = false,
        ViewModelMappingContracts = new[] { typeof(IConfiguredView<>) })]
    public partial class ViewLocator { }
}
""";

        var generated = await StaticViewLocatorGeneratorVerifier.GetGeneratedSourcesAsync(source);
        var locatorSource = generated["ViewLocator_StaticViewLocator.cs"];

        Assert.Contains(
            "[typeof(TestApp.DashboardModel)] = () => new TestApp.ConfiguredDashboardView()",
            locatorSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("new TestApp.ReactiveDashboardView()", locatorSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneratedIDataTemplateDoesNotEmitUnusedRuntimeFallbackHelpers()
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
    public sealed class SampleView : UserControl { }
}

namespace TestApp
{
    [StaticViewLocator(GenerateIDataTemplate = true)]
    public partial class ViewLocator { }
}
""";

        var generated = await StaticViewLocatorGeneratorVerifier.GetGeneratedSourcesAsync(source);
        var locatorSource = generated["ViewLocator_StaticViewLocator.cs"];

        Assert.Contains("public Control? Build(object? param)", locatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetFactoryFromInterfaces", locatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetMissingViewFromInterfaces", locatorSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExistingExactFactoryIsReusedInsteadOfGeneratedAgain()
    {
        const string source = """
using System;
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
    [StaticViewLocator(
        GenerateIViewLocator = true,
        GenerateRuntimeTypeFallbackMethods = false)]
    public partial class ViewLocator
    {
        private static bool TryCreateViewExact(Type viewModelType, out Control? view)
        {
            view = null;
            return false;
        }
    }
}
""";

        var generated = await StaticViewLocatorGeneratorVerifier.GetGeneratedSourcesAsync(source);
        var locatorSource = generated["ViewLocator_StaticViewLocator.cs"];

        Assert.DoesNotContain("private static bool TryCreateViewExact", locatorSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CovariantBuildHookReturnTypeCompiles()
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
        protected Border? BuildResolvedView(object? value) => null;
    }
}
""";

        await StaticViewLocatorGeneratorVerifier.GetGeneratedSourcesAsync(source);
    }

    [Fact]
    public async Task ExistingReactiveUIResolveMethodsAreNotDuplicated()
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
    [StaticViewLocator(
        GenerateIViewLocator = true,
        GenerateRuntimeTypeFallbackMethods = false)]
    public partial class ViewLocator
    {
        public ReactiveUI.IViewFor<TViewModel>? ResolveView<TViewModel>() where TViewModel : class => null;
        public ReactiveUI.IViewFor<TViewModel>? ResolveView<TViewModel>(string? contract) where TViewModel : class => null;
        public ReactiveUI.IViewFor? ResolveView(object? instance) => null;
        public ReactiveUI.IViewFor? ResolveView(object? instance, string? contract) => null;
    }
}
""";

        await StaticViewLocatorGeneratorVerifier.GetGeneratedSourcesAsync(source);
    }
}
