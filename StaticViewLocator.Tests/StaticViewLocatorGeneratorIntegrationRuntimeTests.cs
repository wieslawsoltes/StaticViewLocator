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

public class StaticViewLocatorGeneratorIntegrationRuntimeTests
{
    [AvaloniaFact]
    public void GeneratedReactiveUIAndDataTemplateAdaptersWorkAtRuntime()
    {
        const string source = """
using System;
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
    public abstract class ViewModelBase { }

    public sealed class DashboardViewModel : ViewModelBase { }

    public sealed class ContextHost
    {
        public ContextHost(ViewModelBase context) => Context = context;
        public ViewModelBase Context { get; }
    }
}

namespace TestApp.Views
{
    public sealed class DashboardScreen : UserControl, ReactiveUI.IViewFor<TestApp.ViewModels.DashboardViewModel>
    {
        public TestApp.ViewModels.DashboardViewModel? ViewModel { get; set; }

        object? ReactiveUI.IViewFor.ViewModel
        {
            get => ViewModel;
            set => ViewModel = (TestApp.ViewModels.DashboardViewModel?)value;
        }
    }
}

namespace TestApp
{
    [StaticViewLocator(
        GenerateIViewLocator = true,
        GenerateIDataTemplate = true,
        GenerateRuntimeTypeFallbackMethods = false,
        DataTemplateMatchTypes = new[]
        {
            typeof(ViewModels.ViewModelBase),
            typeof(ViewModels.ContextHost),
        })]
    public partial class ViewLocator
    {
        protected virtual Control? BuildFallbackView(object? param)
        {
            return param is ViewModels.ContextHost
                ? new Border { Tag = "context-fallback" }
                : null;
        }
    }
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
        var dashboardViewModelType = assembly.GetType("TestApp.ViewModels.DashboardViewModel", throwOnError: true)!;
        var viewModelBaseType = assembly.GetType("TestApp.ViewModels.ViewModelBase", throwOnError: true)!;
        var contextHostType = assembly.GetType("TestApp.ViewModels.ContextHost", throwOnError: true)!;

        Assert.Contains(locatorType.GetInterfaces(), static type => type.FullName == "ReactiveUI.IViewLocator");
        Assert.Contains(locatorType.GetInterfaces(), static type => type.FullName == "Avalonia.Controls.Templates.IDataTemplate");

        var locator = Activator.CreateInstance(locatorType)!;
        var dashboardViewModel = Activator.CreateInstance(dashboardViewModelType)!;
        var contextHost = Activator.CreateInstance(contextHostType, dashboardViewModel)!;

        var build = locatorType.GetMethod("Build", BindingFlags.Public | BindingFlags.Instance)!;
        var dashboardView = (Control)build.Invoke(locator, new[] { dashboardViewModel })!;
        Assert.Equal("TestApp.Views.DashboardScreen", dashboardView.GetType().FullName);
        Assert.Same(dashboardViewModel, dashboardView.GetType().GetProperty("ViewModel")!.GetValue(dashboardView));

        var fallback = Assert.IsType<Border>(build.Invoke(locator, new[] { contextHost }));
        Assert.Equal("context-fallback", fallback.Tag);

        var match = locatorType.GetMethod("Match", BindingFlags.Public | BindingFlags.Instance)!;
        Assert.True((bool)match.Invoke(locator, new[] { dashboardViewModel })!);
        Assert.True((bool)match.Invoke(locator, new[] { contextHost })!);
        Assert.False((bool)match.Invoke(locator, new object?[] { new object() })!);
        Assert.False((bool)match.Invoke(locator, new object?[] { null })!);

        var runtimeResolve = locatorType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method => method.Name == "ResolveView" && !method.IsGenericMethod && method.GetParameters().Length == 1);
        var resolvedView = runtimeResolve.Invoke(locator, new[] { dashboardViewModel });
        Assert.NotNull(resolvedView);
        Assert.Equal("TestApp.Views.DashboardScreen", resolvedView!.GetType().FullName);
        Assert.Same(dashboardViewModel, resolvedView.GetType().GetProperty("ViewModel")!.GetValue(resolvedView));

        var genericResolve = locatorType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method => method.Name == "ResolveView" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
            .MakeGenericMethod(dashboardViewModelType);
        var genericView = genericResolve.Invoke(locator, Array.Empty<object>());
        Assert.NotNull(genericView);
        Assert.Equal("TestApp.Views.DashboardScreen", genericView!.GetType().FullName);

        var invalid = Assert.IsType<TextBlock>(build.Invoke(locator, new object?[] { null }));
        Assert.Equal("Invalid view model type.", invalid.Text);

        Assert.True(viewModelBaseType.IsAssignableFrom(dashboardViewModelType));
    }
}
