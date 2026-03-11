using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using StaticViewLocator;
using Xunit;

namespace StaticViewLocator.Tests;

public class StaticViewLocatorGeneratorRuntimeTests
{
    [AvaloniaFact]
    public async Task CreatesRegisteredViewInstances()
    {
        const string source = @"
using System;
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp
{
    [StaticViewLocator]
    public partial class ViewLocator
    {
    }
}

namespace TestApp.ViewModels
{
    public class SampleViewModel
    {
    }

    public class MissingViewModel
    {
    }
}

namespace TestApp.Views
{
    public class SampleView : UserControl
    {
    }
}
";

        var compilation = await CreateCompilationAsync(source);
        var sourceGenerator = new StaticViewLocatorGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(new[] { sourceGenerator }, parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        using var peStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(peStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        peStream.Seek(0, SeekOrigin.Begin);
        var assembly = Assembly.Load(peStream.ToArray());

        var locatorType = assembly.GetType("TestApp.ViewLocator") ?? throw new InvalidOperationException("Generated locator type not found.");
        var buildMethod = locatorType.GetMethod("Build", BindingFlags.Public | BindingFlags.Instance) ?? throw new InvalidOperationException("Build method not found.");
        var locator = Activator.CreateInstance(locatorType) ?? throw new InvalidOperationException("Unable to instantiate generated locator.");
        var sampleViewModel = CreateInstance(assembly, "TestApp.ViewModels.SampleViewModel");
        var missingViewModel = CreateInstance(assembly, "TestApp.ViewModels.MissingViewModel");

        _ = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(StaticViewLocatorGeneratorRuntimeTests).Assembly);

        var sampleControl = (Control)buildMethod.Invoke(locator, new[] { sampleViewModel })!;
        var missingControl = (Control)buildMethod.Invoke(locator, new[] { missingViewModel })!;

        Assert.Equal("TestApp.Views.SampleView", sampleControl.GetType().FullName);
        Assert.Equal("Avalonia.Controls.TextBlock", missingControl.GetType().FullName);
    }

    [AvaloniaFact]
    public async Task ResolvesMultipleViewModelsAndRespectsOrdering()
    {
        const string source = @"
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using StaticViewLocator;

namespace Portal
{
    [StaticViewLocator]
    public partial class PortalViewLocator
    {
    }
}

namespace Portal.ViewModels
{
    public abstract class ViewModelBase
    {
    }

    public class HomeViewModel : ViewModelBase
    {
    }

    public class ReportsViewModel : ViewModelBase
    {
    }

    public class SettingsViewModel : ViewModelBase
    {
    }

    public abstract class WorkspaceViewModel : ViewModelBase
    {
    }
}

namespace Portal.Views
{
    public class HomeView : UserControl
    {
    }

    public class ReportsView : UserControl
    {
    }
}
";

        var compilation = await CreateCompilationAsync(source);
        var sourceGenerator = new StaticViewLocatorGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(new[] { sourceGenerator }, parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        using var peStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(peStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        peStream.Seek(0, SeekOrigin.Begin);
        var assembly = Assembly.Load(peStream.ToArray());

        var locatorType = assembly.GetType("Portal.PortalViewLocator") ?? throw new InvalidOperationException("Generated locator type not found.");
        var buildMethod = locatorType.GetMethod("Build", BindingFlags.Public | BindingFlags.Instance) ?? throw new InvalidOperationException("Build method not found.");
        var locator = Activator.CreateInstance(locatorType) ?? throw new InvalidOperationException("Unable to instantiate generated locator.");
        var dictionaryField = locatorType.GetField("s_views", BindingFlags.NonPublic | BindingFlags.Static) ?? throw new InvalidOperationException("Dictionary field not found.");
        var viewsMap = (Dictionary<Type, Func<Control>>)dictionaryField.GetValue(null)!;

        var expectedOrder = new[]
        {
            "Portal.ViewModels.HomeViewModel",
            "Portal.ViewModels.ReportsViewModel",
        };

        Assert.Equal(expectedOrder.Length, viewsMap.Count);
        Assert.Equal(expectedOrder, viewsMap.Keys.Select(key => key.FullName).ToArray());

        var homeViewModel = CreateInstance(assembly, "Portal.ViewModels.HomeViewModel");
        var reportsViewModel = CreateInstance(assembly, "Portal.ViewModels.ReportsViewModel");
        var settingsViewModel = CreateInstance(assembly, "Portal.ViewModels.SettingsViewModel");

        var homeControl = (Control)buildMethod.Invoke(locator, new[] { homeViewModel })!;
        var reportsControl = (Control)buildMethod.Invoke(locator, new[] { reportsViewModel })!;
        var settingsControl = (Control)buildMethod.Invoke(locator, new[] { settingsViewModel })!;

        Assert.Equal("Portal.Views.HomeView", homeControl.GetType().FullName);
        Assert.Equal("Portal.Views.ReportsView", reportsControl.GetType().FullName);

        var fallback = Assert.IsType<TextBlock>(settingsControl);
        Assert.Equal("Not Found: Portal.Views.SettingsView", fallback.Text);
        Assert.DoesNotContain(viewsMap.Keys, key => key.FullName?.Contains("WorkspaceViewModel", StringComparison.Ordinal) == true);
    }

    [AvaloniaFact]
    public async Task ResolvesGenericViewModelsUsingGenericTypeDefinition()
    {
        const string source = @"
using System;
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp
{
    [StaticViewLocator]
    public partial class ViewLocator
    {
    }
}

namespace TestApp.ViewModels
{
    public class WidgetViewModel<T>
    {
    }
}

namespace TestApp.Views
{
    public class WidgetView : UserControl
    {
    }
}
";

        var compilation = await CreateCompilationAsync(source);
        var sourceGenerator = new StaticViewLocatorGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(
            new[] { sourceGenerator },
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>
            {
                ["build_property.StaticViewLocatorNamespaceReplacementRules"] = "ViewModels=Views",
                ["build_property.StaticViewLocatorTypeNameReplacementRules"] = "ViewModel=View",
            }));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        using var peStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(peStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        peStream.Seek(0, SeekOrigin.Begin);
        var assembly = Assembly.Load(peStream.ToArray());

        var locatorType = assembly.GetType("TestApp.ViewLocator") ?? throw new InvalidOperationException("Generated locator type not found.");
        var buildMethod = locatorType.GetMethod("Build", BindingFlags.Public | BindingFlags.Instance) ?? throw new InvalidOperationException("Build method not found.");
        var widgetVmType = assembly.GetType("TestApp.ViewModels.WidgetViewModel`1", throwOnError: true) ?? throw new InvalidOperationException("Generic VM type not found.");
        var closedVm = Activator.CreateInstance(widgetVmType.MakeGenericType(typeof(int))) ?? throw new InvalidOperationException("Unable to instantiate closed generic VM.");
        var locator = Activator.CreateInstance(locatorType) ?? throw new InvalidOperationException("Unable to instantiate generated locator.");

        _ = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(StaticViewLocatorGeneratorRuntimeTests).Assembly);

        var control = (Control?)buildMethod.Invoke(locator, new[] { closedVm });

        Assert.NotNull(control);
        Assert.Equal("TestApp.Views.WidgetView", control!.GetType().FullName);
    }

    [AvaloniaFact]
    public async Task ResolvesUsingBaseClassBeforeInterfaceFallback()
    {
        const string source = @"
using System;
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp
{
    [StaticViewLocator]
    public partial class ViewLocator
    {
    }
}

namespace TestApp.ViewModels
{
    public abstract class BaseViewModel
    {
    }

    public interface IAlternateViewModel
    {
    }

    public sealed class ConcreteViewModel : BaseViewModel, IAlternateViewModel
    {
    }
}

namespace TestApp.Views
{
    public class BaseView : UserControl
    {
    }

    public class AlternateView : UserControl
    {
    }
}
";

        var compilation = await CreateCompilationAsync(source);
        var sourceGenerator = new StaticViewLocatorGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(new[] { sourceGenerator }, parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        using var peStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(peStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        peStream.Seek(0, SeekOrigin.Begin);
        var assembly = Assembly.Load(peStream.ToArray());

        var locatorType = assembly.GetType("TestApp.ViewLocator") ?? throw new InvalidOperationException("Generated locator type not found.");
        var buildMethod = locatorType.GetMethod("Build", BindingFlags.Public | BindingFlags.Instance) ?? throw new InvalidOperationException("Build method not found.");
        var concreteViewModel = CreateInstance(assembly, "TestApp.ViewModels.ConcreteViewModel");
        var locator = Activator.CreateInstance(locatorType) ?? throw new InvalidOperationException("Unable to instantiate generated locator.");

        _ = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(StaticViewLocatorGeneratorRuntimeTests).Assembly);

        var control = (Control?)buildMethod.Invoke(locator, new[] { concreteViewModel });

        Assert.NotNull(control);
        Assert.Equal("TestApp.Views.BaseView", control!.GetType().FullName);
    }

    [AvaloniaFact]
    public async Task ResolvesInterfaceMappingsByStrippingConfiguredPrefix()
    {
        const string source = @"
using System;
using Avalonia.Controls;
using StaticViewLocator;

namespace TestApp
{
    [StaticViewLocator]
    public partial class ViewLocator
    {
    }
}

namespace TestApp.ViewModels
{
    public interface IDetailsViewModel
    {
    }

    public sealed class ConcreteViewModel : IDetailsViewModel
    {
    }
}

namespace TestApp.Views
{
    public class DetailsView : UserControl
    {
    }
}
";

        var compilation = await CreateCompilationAsync(source);
        var sourceGenerator = new StaticViewLocatorGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(new[] { sourceGenerator }, parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        using var peStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(peStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        peStream.Seek(0, SeekOrigin.Begin);
        var assembly = Assembly.Load(peStream.ToArray());

        var locatorType = assembly.GetType("TestApp.ViewLocator") ?? throw new InvalidOperationException("Generated locator type not found.");
        var buildMethod = locatorType.GetMethod("Build", BindingFlags.Public | BindingFlags.Instance) ?? throw new InvalidOperationException("Build method not found.");
        var concreteViewModel = CreateInstance(assembly, "TestApp.ViewModels.ConcreteViewModel");
        var locator = Activator.CreateInstance(locatorType) ?? throw new InvalidOperationException("Unable to instantiate generated locator.");

        _ = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(StaticViewLocatorGeneratorRuntimeTests).Assembly);

        var control = (Control?)buildMethod.Invoke(locator, new[] { concreteViewModel });

        Assert.NotNull(control);
        Assert.Equal("TestApp.Views.DetailsView", control!.GetType().FullName);
    }

    private static Task<CSharpCompilation> CreateCompilationAsync(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            assemblyName: "RuntimeTestAssembly",
            syntaxTrees: new[] { syntaxTree },
            references: ResolveReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return Task.FromResult(compilation);
    }

    private static IReadOnlyCollection<MetadataReference> ResolveReferences()
    {
        var references = new List<MetadataReference>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;

        foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                continue;
            }

            if (unique.Add(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        foreach (var assembly in GetAdditionalAssemblies())
        {
            if (string.IsNullOrEmpty(assembly.Location) || !unique.Add(assembly.Location))
            {
                continue;
            }

            references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        return references;
    }

    private static IEnumerable<Assembly> GetAdditionalAssemblies()
    {
        yield return typeof(Control).Assembly;
        yield return typeof(UserControl).Assembly;
        yield return typeof(StaticViewLocatorGenerator).Assembly;
    }

    private static object CreateInstance(Assembly assembly, string typeName)
    {
        var type = assembly.GetType(typeName, throwOnError: true) ??
            throw new InvalidOperationException($"Unable to locate type '{typeName}'.");

        return Activator.CreateInstance(type) ??
               throw new InvalidOperationException($"Unable to instantiate type '{typeName}'.");
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions EmptyOptions = new TestAnalyzerConfigOptions(new Dictionary<string, string>());
        private readonly AnalyzerConfigOptions _globalOptions;

        public TestAnalyzerConfigOptionsProvider(IReadOnlyDictionary<string, string> globalOptions)
        {
            _globalOptions = new TestAnalyzerConfigOptions(globalOptions);
        }

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => EmptyOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => EmptyOptions;
    }

    private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> _options;

        public TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string> options)
        {
            _options = options;
        }

        public override bool TryGetValue(string key, out string value)
        {
            return _options.TryGetValue(key, out value!);
        }
    }
}
