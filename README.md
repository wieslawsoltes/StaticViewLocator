# StaticViewLocator

[![CI](https://github.com/wieslawsoltes/StaticViewLocator/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/StaticViewLocator/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/StaticViewLocator.svg)](https://www.nuget.org/packages/StaticViewLocator)
[![NuGet](https://img.shields.io/nuget/dt/StaticViewLocator.svg)](https://www.nuget.org/packages/StaticViewLocator)

A C# source generator that automatically implements static view locator for Avalonia without using reflection.

## Usage

Add NuGet package reference to project.

```xml
<PackageReference Include="StaticViewLocator" Version="0.0.1">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

Annotate view locator class with `[StaticViewLocator]` attribute, make class `partial` and imlement `Build` using `s_views` dictionary to retrieve views for `data` objects.

```csharp
[StaticViewLocator]
public partial class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
        {
            return null;
        }

        var type = data.GetType();

        if (s_views.TryGetValue(type, out var func))
        {
            return func.Invoke();
        }

        throw new Exception($"Unable to create view for type: {type}");
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
```

## License

StaticViewLocator is licensed under the MIT license. See [LICENSE](LICENSE.TXT) file for details.
