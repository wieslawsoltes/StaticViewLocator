using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using ReactiveUI.Avalonia;
using StaticViewLocatorReactiveUIDemo.ViewModels;

namespace StaticViewLocatorReactiveUIDemo.Views;

public sealed class MainWindow : Window
{
    public MainWindow()
    {
        Title = "StaticViewLocator + ReactiveUI";
        Width = 960;
        Height = 560;
        MinWidth = 720;
        MinHeight = 440;

        var homeButton = new Button { Content = "Home" };
        homeButton.Click += (_, _) => ViewModel?.ShowHome();

        var settingsButton = new Button { Content = "Settings" };
        settingsButton.Click += (_, _) => ViewModel?.ShowSettings();

        var wrappedButton = new Button { Content = "Fallback wrapper" };
        wrappedButton.Click += (_, _) => ViewModel?.ShowWrappedSettings();

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                homeButton,
                settingsButton,
                wrappedButton,
            },
        };

        var dataTemplateContent = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        dataTemplateContent.Bind(
            ContentControl.ContentProperty,
            new Binding(nameof(MainWindowViewModel.CurrentContent)));

        var reactiveViewHost = new ViewModelViewHost
        {
            ViewLocator = new ViewLocator(),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        reactiveViewHost.Bind(
            ViewModelViewHost.ViewModelProperty,
            new Binding(nameof(MainWindowViewModel.CurrentViewModel)));

        var dataTemplateSection = CreateSection("Avalonia IDataTemplate", dataTemplateContent);
        var reactiveSection = CreateSection("ReactiveUI IViewLocator", reactiveViewHost);
        Grid.SetColumn(reactiveSection, 1);

        var contentGrid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,*"),
            ColumnSpacing = 16,
            Children =
            {
                dataTemplateSection,
                reactiveSection,
            },
        };

        Content = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            Margin = new Thickness(24),
            RowSpacing = 16,
            Children =
            {
                toolbar,
                contentGrid,
            },
        };
        Grid.SetRow(contentGrid, 1);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private static Control CreateSection(string title, Control content)
    {
        var header = new TextBlock
        {
            Text = title,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
        };

        var grid = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            RowSpacing = 12,
            Children =
            {
                header,
                content,
            },
        };
        Grid.SetRow(content, 1);

        return new Border
        {
            Padding = new Thickness(16),
            BorderThickness = new Thickness(1),
            Child = grid,
        };
    }
}