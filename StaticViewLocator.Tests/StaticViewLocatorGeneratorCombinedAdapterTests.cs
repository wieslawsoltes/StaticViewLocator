using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.CodeAnalysis;
using StaticViewLocator.Tests.TestHelpers;
using Xunit;

namespace StaticViewLocator.Tests;

public class StaticViewLocatorGeneratorCombinedAdapterTests
{
    [AvaloniaFact]
    public void CombinedAdaptersBuildConventionOnlyAvaloniaViews()
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

namespace TestApp.ViewModels
{
    public sealed class PlainViewModel { }
}

namespace TestApp.Views
{
    public sealed class PlainView : UserControl { }
}

namespace TestApp
{
    [StaticViewLocator(
        GenerateIViewLocator = true,
        GenerateIDataTemplate = true,
        GenerateRuntimeTypeFallbackMethods = false)]
    public partial class ViewLocator { }
}
""";

        var result = StaticViewLocatorGeneratorVerifier.RunGenerator(source);
        Assert.Empty(result.GeneratorDiagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        using var peStream = new MemoryStream();
        var emitResult = result.Compilation.Emit(peStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        peStream.Position = 0;
        var assembly = Assembly.Load(peStream.ToArray());
        var locatorType = assembly.GetType("TestApp.ViewLocator", throwOnError: true)!;
        var viewModelType = assembly.GetType("TestApp.ViewModels.PlainViewModel", throwOnError: true)!;
        var viewModel = Activator.CreateInstance(viewModelType)!;
        var locator = Activator.CreateInstance(locatorType)!;

        var build = locatorType.GetMethod("Build", BindingFlags.Public | BindingFlags.Instance)!;
        var view = Assert.IsAssignableFrom<Control>(build.Invoke(locator, new[] { viewModel }));

        Assert.Equal("TestApp.Views.PlainView", view.GetType().FullName);
        Assert.Same(viewModel, view.DataContext);

        var resolveView = locatorType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method => method.Name == "ResolveView" && !method.IsGenericMethod && method.GetParameters().Length == 1);
        Assert.Null(resolveView.Invoke(locator, new[] { viewModel }));
    }
}
