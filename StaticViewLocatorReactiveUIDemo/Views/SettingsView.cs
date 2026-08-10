using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using ReactiveUI.Avalonia;
using StaticViewLocatorReactiveUIDemo.ViewModels;

namespace StaticViewLocatorReactiveUIDemo.Views;

public sealed class SettingsView : ReactiveUserControl<SettingsViewModel>
{
    public SettingsView()
    {
        var title = new TextBlock { FontSize = 28, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        title.Bind(TextBlock.TextProperty, new Binding(nameof(SettingsViewModel.Title)));

        var description = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        description.Bind(TextBlock.TextProperty, new Binding(nameof(SettingsViewModel.Description)));

        Content = new Border
        {
            Padding = new Thickness(28),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 12,
                Children = { title, description },
            },
        };
    }
}
