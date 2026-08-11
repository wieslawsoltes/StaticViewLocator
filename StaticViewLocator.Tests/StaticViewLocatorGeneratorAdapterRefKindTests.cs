using System.Threading.Tasks;
using StaticViewLocator.Tests.TestHelpers;
using Xunit;

namespace StaticViewLocator.Tests;

public class StaticViewLocatorGeneratorAdapterRefKindTests
{
    [Fact]
    public async Task RefKindOverloadsDoNotSuppressByValueAdapterMembers()
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
        GenerateIDataTemplate = true,
        GenerateRuntimeTypeFallbackMethods = false)]
    public partial class ViewLocator
    {
        public Control? Build(ref object? value) => null;
        public bool Match(in object? value) => false;
        public ReactiveUI.IViewFor? ResolveView(ref object? value) => null;
        protected Control? BuildFallbackView(ref object? value) => null;
    }
}
""";

        await StaticViewLocatorGeneratorVerifier.GetGeneratedSourcesAsync(source);
    }
}
