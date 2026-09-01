using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.CodeAnalysis;
using StaticViewLocator.Tests.TestHelpers;
using Xunit;

namespace StaticViewLocator.Tests;

public class StaticViewLocatorGeneratorResolutionModeTests
{
    public static IEnumerable<object[]> ResolutionModes()
    {
        yield return new object[] { "Exact", string.Empty };
        yield return new object[] { "ExactThenBaseTypes", "TestApp.Views.BaseView" };
        yield return new object[] { "ExactThenInterfaces", "TestApp.Views.ContractView" };
        yield return new object[] { "ExactThenBaseTypesThenInterfaces", "TestApp.Views.BaseView" };
        yield return new object[] { "ExactThenInterfacesThenBaseTypes", "TestApp.Views.ContractView" };
    }

    [Theory]
    [MemberData(nameof(ResolutionModes))]
    public async Task FlattensDiscoveredFallbackMappingsForEveryResolutionMode(
        string mode,
        string expectedViewType)
    {
        var generated = await StaticViewLocatorGeneratorVerifier.GetGeneratedSourcesAsync(
            CreateResolutionSource(mode));
        var locatorSource = generated["ViewLocator_StaticViewLocator.cs"];
        var derivedMappingPrefix = "[typeof(TestApp.ViewModels.DerivedViewModel)] = () => new ";

        if (expectedViewType.Length == 0)
        {
            Assert.DoesNotContain(derivedMappingPrefix, locatorSource, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(derivedMappingPrefix + expectedViewType + "()", locatorSource, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(".BaseType", GetGeneratedAdapterPath(locatorSource), StringComparison.Ordinal);
        Assert.DoesNotContain("GetInterfaces", GetGeneratedAdapterPath(locatorSource), StringComparison.Ordinal);
        Assert.DoesNotContain("IsAssignableFrom", GetGeneratedAdapterPath(locatorSource), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmitsRuntimeTypeTestsInConfiguredPrecedenceOrder()
    {
        var baseFirst = (await StaticViewLocatorGeneratorVerifier.GetGeneratedSourcesAsync(
            CreateResolutionSource("ExactThenBaseTypesThenInterfaces")))["ViewLocator_StaticViewLocator.cs"];
        var interfaceFirst = (await StaticViewLocatorGeneratorVerifier.GetGeneratedSourcesAsync(
            CreateResolutionSource("ExactThenInterfacesThenBaseTypes")))["ViewLocator_StaticViewLocator.cs"];

        Assert.True(
            baseFirst.IndexOf("instance is TestApp.ViewModels.BaseViewModel", StringComparison.Ordinal) <
            baseFirst.IndexOf("instance is TestApp.ViewModels.IContractViewModel", StringComparison.Ordinal));
        Assert.True(
            interfaceFirst.IndexOf("instance is TestApp.ViewModels.IContractViewModel", StringComparison.Ordinal) <
            interfaceFirst.IndexOf("instance is TestApp.ViewModels.BaseViewModel", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UsesNearestMappedInterfaceBeforeItsMappedAncestor()
    {
        const string source = """
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp;

public interface IBaseViewModel { }
public interface IDerivedViewModel : IBaseViewModel { }
public sealed class ConcreteViewModel : IDerivedViewModel { }
public sealed class BaseView : UserControl { }
public sealed class DerivedView : UserControl { }

[StaticViewMapping(typeof(IBaseViewModel), typeof(BaseView))]
[StaticViewMapping(typeof(IDerivedViewModel), typeof(DerivedView))]
[StaticViewLocator(
    GenerateIDataTemplate = true,
    GeneratedAdapterResolutionMode = ViewResolutionMode.ExactThenInterfaces)]
public partial class ViewLocator { }
""";

        var generated = await StaticViewLocatorGeneratorVerifier.GetGeneratedSourcesAsync(source);
        var locatorSource = generated["ViewLocator_StaticViewLocator.cs"];
        Assert.Contains(
            "[typeof(TestApp.ConcreteViewModel)] = () => new TestApp.DerivedView()",
            locatorSource,
            StringComparison.Ordinal);
        Assert.True(
            locatorSource.IndexOf("instance is TestApp.IDerivedViewModel", StringComparison.Ordinal) <
            locatorSource.IndexOf("instance is TestApp.IBaseViewModel", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsAmbiguousMappedInterfacesAndRequiresAnExplicitMapping()
    {
        const string source = """
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp;

public interface IAlphaViewModel { }
public interface IBetaViewModel { }
public sealed class AmbiguousViewModel : IAlphaViewModel, IBetaViewModel { }
public sealed class AlphaView : UserControl { }
public sealed class BetaView : UserControl { }

[StaticViewMapping(typeof(IAlphaViewModel), typeof(AlphaView))]
[StaticViewMapping(typeof(IBetaViewModel), typeof(BetaView))]
[StaticViewLocator(
    GenerateIDataTemplate = true,
    GeneratedAdapterResolutionMode = ViewResolutionMode.ExactThenInterfacesThenBaseTypes)]
public partial class ViewLocator { }
""";

        var result = StaticViewLocatorGeneratorVerifier.RunGenerator(source);
        var diagnostic = Assert.Single(result.GeneratorDiagnostics, static item => item.Id == "SVL0007");

        Assert.Contains("IAlphaViewModel", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("IBetaViewModel", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsOpenGenericMappingWithoutACompiledFallbackContract()
    {
        const string source = """
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp.ViewModels
{
    public sealed class FilterViewModel<T> { }
}

namespace TestApp.Views
{
    public sealed class FilterView : UserControl { }
}

namespace TestApp
{
    [StaticViewLocator(GenerateIDataTemplate = true)]
    public partial class ViewLocator { }
}
""";

        var result = StaticViewLocatorGeneratorVerifier.RunGenerator(source);
        Assert.Single(result.GeneratorDiagnostics, static item => item.Id == "SVL0008");
    }

    [Fact]
    public async Task InfersAnUnambiguousNonGenericBaseContractForAnOpenGenericMapping()
    {
        const string source = """
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp.ViewModels
{
    public abstract class FilterViewModelBase { }
    public sealed class FilterViewModel<T> : FilterViewModelBase { }
}

namespace TestApp.Views
{
    public sealed class FilterView : UserControl { }
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
            "[typeof(TestApp.ViewModels.FilterViewModelBase)] = () => new TestApp.Views.FilterView()",
            locatorSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "instance is TestApp.ViewModels.FilterViewModelBase",
            locatorSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotInferABroadBaseContractWithConflictingDescendantViews()
    {
        const string source = """
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp.ViewModels
{
    public abstract class RootViewModel { }
    public sealed class FilterViewModel<T> : RootViewModel { }
    public sealed class OtherViewModel : RootViewModel { }
}

namespace TestApp.Views
{
    public sealed class FilterView : UserControl { }
    public sealed class OtherView : UserControl { }
}

namespace TestApp
{
    [StaticViewLocator(GenerateIDataTemplate = true)]
    public partial class ViewLocator { }
}
""";

        var result = StaticViewLocatorGeneratorVerifier.RunGenerator(source);

        Assert.Single(result.GeneratorDiagnostics, static item => item.Id == "SVL0008");
        var locatorSource = result.RunResult.GeneratedTrees.Single(tree =>
            tree.FilePath.EndsWith("ViewLocator_StaticViewLocator.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.DoesNotContain(
            "[typeof(TestApp.ViewModels.RootViewModel)] = () =>",
            locatorSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotInferABroadBaseContractWithAnUnresolvedDescendant()
    {
        const string source = """
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp.ViewModels
{
    public abstract class RootViewModel { }
    public sealed class FilterViewModel<T> : RootViewModel { }
    public sealed class UnresolvedViewModel : RootViewModel { }
}

namespace TestApp.Views
{
    public sealed class FilterView : UserControl { }
}

namespace TestApp
{
    [StaticViewLocator(GenerateIDataTemplate = true)]
    public partial class ViewLocator { }
}
""";

        var result = StaticViewLocatorGeneratorVerifier.RunGenerator(source);

        Assert.Single(result.GeneratorDiagnostics, static item => item.Id == "SVL0008");
        var locatorSource = result.RunResult.GeneratedTrees.Single(tree =>
            tree.FilePath.EndsWith("ViewLocator_StaticViewLocator.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.DoesNotContain(
            "[typeof(TestApp.ViewModels.RootViewModel)] = () =>",
            locatorSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAmbiguousOpenGenericFallbackContracts()
    {
        const string source = """
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp;

public interface IAlphaViewModel { }
public interface IBetaViewModel { }
public sealed class GenericViewModel<T> : IAlphaViewModel, IBetaViewModel { }
public sealed class GenericView : UserControl { }
public sealed class AlphaView : UserControl { }
public sealed class BetaView : UserControl { }

[StaticViewMapping(typeof(IAlphaViewModel), typeof(AlphaView))]
[StaticViewMapping(typeof(IBetaViewModel), typeof(BetaView))]
[StaticViewLocator(
    GenerateIDataTemplate = true,
    GeneratedAdapterResolutionMode = ViewResolutionMode.ExactThenInterfaces)]
public partial class ViewLocator { }
""";

        var result = StaticViewLocatorGeneratorVerifier.RunGenerator(source);
        Assert.Single(result.GeneratorDiagnostics, static item => item.Id == "SVL0007");
        Assert.DoesNotContain(result.GeneratorDiagnostics, static item => item.Id == "SVL0008");
    }

    [Fact]
    public void ReportsWhenNearestBaseShadowsTheOpenGenericTarget()
    {
        const string source = """
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp;

public abstract class RootViewModel { }
public abstract class IntermediateViewModel : RootViewModel { }
public sealed class GenericViewModel<T> : IntermediateViewModel { }
public sealed class GenericView : UserControl { }
public sealed class OtherView : UserControl { }

[StaticViewMapping(typeof(RootViewModel), typeof(GenericView))]
[StaticViewMapping(typeof(IntermediateViewModel), typeof(OtherView))]
[StaticViewLocator(
    GenerateIDataTemplate = true,
    GeneratedAdapterResolutionMode = ViewResolutionMode.ExactThenBaseTypes)]
public partial class ViewLocator { }
""";

        var result = StaticViewLocatorGeneratorVerifier.RunGenerator(source);

        Assert.Single(result.GeneratorDiagnostics, static item => item.Id == "SVL0008");
    }

    [Fact]
    public void ReportsWhenPreferredInterfaceShadowsTheOpenGenericTargetBase()
    {
        const string source = """
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp;

public interface IOtherViewModel { }
public abstract class GenericViewModelBase { }
public sealed class GenericViewModel<T> : GenericViewModelBase, IOtherViewModel { }
public sealed class GenericView : UserControl { }
public sealed class OtherView : UserControl { }

[StaticViewMapping(typeof(GenericViewModelBase), typeof(GenericView))]
[StaticViewMapping(typeof(IOtherViewModel), typeof(OtherView))]
[StaticViewLocator(
    GenerateIDataTemplate = true,
    GeneratedAdapterResolutionMode = ViewResolutionMode.ExactThenInterfacesThenBaseTypes)]
public partial class ViewLocator { }
""";

        var result = StaticViewLocatorGeneratorVerifier.RunGenerator(source);

        Assert.Single(result.GeneratorDiagnostics, static item => item.Id == "SVL0008");
    }

    [AvaloniaFact]
    public void GeneratedResolverCoversAllPathsWithoutAllocatingOrConstructingDuringMatch()
    {
        const string source = """
using System;
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp;

public static class ConstructionCounter
{
    public static int Count;
}

public abstract class BaseViewModel { }
public sealed class DerivedViewModel : BaseViewModel { }
public interface IContractViewModel { }
public sealed class ContractViewModel : IContractViewModel { }
public abstract class GenericViewModelBase { }
public sealed class GenericViewModel<T> : GenericViewModelBase { }
public sealed class ExplicitViewModel { }
public sealed class FilteredViewModel { }
public sealed class UnresolvedViewModel { }

public abstract class CountedView : UserControl
{
    protected CountedView() => ConstructionCounter.Count++;
}

public sealed class BaseView : CountedView { }
public sealed class ContractView : CountedView { }
public sealed class GenericView : CountedView { }
public sealed class ExplicitOverrideView : CountedView { }
public sealed class FilteredView : CountedView { }

[StaticViewMapping(typeof(BaseViewModel), typeof(BaseView))]
[StaticViewMapping(typeof(IContractViewModel), typeof(ContractView))]
[StaticViewMapping(typeof(ExplicitViewModel), typeof(ExplicitOverrideView))]
[StaticViewMapping(typeof(FilteredViewModel), typeof(FilteredView))]
[StaticViewLocator(
    GenerateIDataTemplate = true,
    GeneratedAdapterResolutionMode = ViewResolutionMode.ExactThenBaseTypesThenInterfaces,
    DataTemplateMatchTypes = new[]
    {
        typeof(BaseViewModel),
        typeof(IContractViewModel),
        typeof(GenericViewModelBase),
        typeof(ExplicitViewModel),
    })]
public partial class ViewLocator
{
    public static long MeasureMatch(ViewLocator locator, object value)
    {
        for (var index = 0; index < 20_000; index++)
        {
            locator.Match(value);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 100_000; index++)
        {
            locator.Match(value);
        }

        GC.KeepAlive(value);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
""";

        var result = StaticViewLocatorGeneratorVerifier.RunGenerator(source);
        Assert.Empty(result.GeneratorDiagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var locatorSource = result.RunResult.GeneratedTrees.Single(tree =>
            tree.FilePath.EndsWith("ViewLocator_StaticViewLocator.cs", StringComparison.Ordinal)).GetText().ToString();
        var adapterPath = GetGeneratedAdapterPath(locatorSource);
        Assert.DoesNotContain(".BaseType", adapterPath, StringComparison.Ordinal);
        Assert.DoesNotContain("GetInterfaces", adapterPath, StringComparison.Ordinal);
        Assert.DoesNotContain("GetGenericTypeDefinition", adapterPath, StringComparison.Ordinal);
        Assert.DoesNotContain("IsAssignableFrom", adapterPath, StringComparison.Ordinal);
        Assert.Contains("data is global::TestApp.BaseViewModel", adapterPath, StringComparison.Ordinal);

        using var peStream = new MemoryStream();
        var emitResult = result.Compilation.Emit(peStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        var assembly = Assembly.Load(peStream.ToArray());
        var locatorType = assembly.GetType("TestApp.ViewLocator", throwOnError: true)!;
        var locator = Activator.CreateInstance(locatorType)!;
        var match = locatorType.GetMethod("Match", BindingFlags.Public | BindingFlags.Instance)!;
        var build = locatorType.GetMethod("Build", BindingFlags.Public | BindingFlags.Instance)!;
        var measure = locatorType.GetMethod("MeasureMatch", BindingFlags.Public | BindingFlags.Static)!;

        object Create(string name) => Activator.CreateInstance(assembly.GetType("TestApp." + name, true)!)!;

        var exact = Create("ExplicitViewModel");
        var inherited = Create("DerivedViewModel");
        var throughInterface = Create("ContractViewModel");
        var generic = Activator.CreateInstance(
            assembly.GetType("TestApp.GenericViewModel`1", true)!.MakeGenericType(typeof(int)))!;
        var filtered = Create("FilteredViewModel");
        var unresolved = Create("UnresolvedViewModel");

        Assert.True((bool)match.Invoke(locator, new[] { exact })!);
        Assert.True((bool)match.Invoke(locator, new[] { inherited })!);
        Assert.True((bool)match.Invoke(locator, new[] { throughInterface })!);
        Assert.True((bool)match.Invoke(locator, new[] { generic })!);
        Assert.False((bool)match.Invoke(locator, new[] { filtered })!);
        Assert.False((bool)match.Invoke(locator, new[] { unresolved })!);
        Assert.False((bool)match.Invoke(locator, new object?[] { null })!);

        var counterType = assembly.GetType("TestApp.ConstructionCounter", true)!;
        Assert.Equal(0, counterType.GetField("Count")!.GetValue(null));
        Assert.Equal(0L, measure.Invoke(null, new[] { locator, exact }));
        Assert.Equal(0L, measure.Invoke(null, new[] { locator, unresolved }));
        Assert.Equal(0, counterType.GetField("Count")!.GetValue(null));

        Assert.Equal("TestApp.BaseView", ((Control)build.Invoke(locator, new[] { inherited })!).GetType().FullName);
        Assert.Equal("TestApp.ContractView", ((Control)build.Invoke(locator, new[] { throughInterface })!).GetType().FullName);
        Assert.Equal("TestApp.GenericView", ((Control)build.Invoke(locator, new[] { generic })!).GetType().FullName);
        Assert.Equal("TestApp.ExplicitOverrideView", ((Control)build.Invoke(locator, new[] { exact })!).GetType().FullName);
        Assert.IsType<TextBlock>(build.Invoke(locator, new[] { unresolved }));
    }

    private static string CreateResolutionSource(string mode)
    {
        return $$"""
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp.ViewModels
{
    public class BaseViewModel { }
    public interface IContractViewModel { }
    public sealed class DerivedViewModel : BaseViewModel, IContractViewModel { }
}

namespace TestApp.Views
{
    public sealed class BaseView : UserControl { }
    public sealed class ContractView : UserControl { }
}

namespace TestApp
{
    [StaticViewMapping(typeof(ViewModels.BaseViewModel), typeof(Views.BaseView))]
    [StaticViewMapping(typeof(ViewModels.IContractViewModel), typeof(Views.ContractView))]
    [StaticViewLocator(
        GenerateIDataTemplate = true,
        GeneratedAdapterResolutionMode = ViewResolutionMode.{{mode}})]
    public partial class ViewLocator { }
}
""";
    }

    private static string GetGeneratedAdapterPath(string locatorSource)
    {
        var resolverStart = locatorSource.IndexOf("TryGetResolvedViewFactory", StringComparison.Ordinal);
        var legacyStart = locatorSource.IndexOf("TryGetFactory(Type?", StringComparison.Ordinal);
        return legacyStart < 0
            ? locatorSource.Substring(resolverStart)
            : locatorSource.Substring(resolverStart, legacyStart - resolverStart);
    }
}
