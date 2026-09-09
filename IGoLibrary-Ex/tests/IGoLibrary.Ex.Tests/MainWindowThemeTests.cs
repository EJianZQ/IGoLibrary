using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using IGoLibrary.Ex.Desktop;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class MainWindowThemeTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MainWindow_UsesReadableThemeColors_OnStartupAndAfterSwitching(bool initiallyDark)
    {
        var app = Avalonia.Application.Current!;
        var originalResources = app.Resources;
        var originalVariant = app.RequestedThemeVariant;
        var originalStyleCount = app.Styles.Count;

        var productionApp = new App();
        productionApp.Initialize();
        var resources = productionApp.Resources;
        productionApp.Resources = new ResourceDictionary();
        var styles = productionApp.Styles.ToArray();
        productionApp.Styles.Clear();

        MainWindow? window = null;
        try
        {
            app.Resources = resources;
            foreach (var style in styles)
                app.Styles.Add(style);

            app.RequestedThemeVariant = initiallyDark ? ThemeVariant.Dark : ThemeVariant.Light;
            window = new MainWindow();
            window.Show();

            foreach (var dark in new[] { initiallyDark, !initiallyDark, initiallyDark })
            {
                app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(app.RequestedThemeVariant, window.ActualThemeVariant);
                AssertThemeColors(window, dark);
            }
        }
        finally
        {
            window?.Close();
            while (app.Styles.Count > originalStyleCount)
                app.Styles.RemoveAt(app.Styles.Count - 1);
            app.Resources = originalResources;
            app.RequestedThemeVariant = originalVariant;
        }
    }

    private static void AssertThemeColors(MainWindow window, bool dark)
    {
        var root = Assert.IsType<Grid>(window.Content);
        var content = root.Children.OfType<Grid>().Single(grid => Grid.GetColumn(grid) == 1);
        var pageColor = dark ? "#0F172A" : "#F5F6F8";
        var cardColor = dark ? "#111827" : "#FFFFFF";
        var borderColor = dark ? "#243244" : "#EAEAEA";
        var primaryTextColor = dark ? "#F8FAFC" : "#1D2129";

        var backgrounds = content.Children.OfType<Border>()
            .Where(border => border.Child is null).ToArray();
        Assert.Equal(2, backgrounds.Length);
        foreach (var background in backgrounds)
            AssertColor(pageColor, background.Background);

        var carousel = content.Children.OfType<Carousel>().Single();
        carousel.PageTransition = null;
        carousel.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        var home = Assert.IsType<ScrollViewer>(carousel.Items[0]);
        AssertColor(pageColor, home.Background);
        var cards = home.GetLogicalDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("home-minimal-card")).ToArray();
        Assert.Equal(6, cards.Length);
        foreach (var card in cards)
        {
            AssertColor(cardColor, card.Background);
            AssertColor(borderColor, card.BorderBrush);
        }

        var icons = home.GetLogicalDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("dashboard-reservation-icon")).ToArray();
        Assert.NotEmpty(icons);
        foreach (var icon in icons)
        {
            AssertColor(dark ? "#162132" : "#F5F6F8", icon.Background);
            AssertColor(borderColor, icon.BorderBrush);
        }

        var greeting = window.FindControl<TextBlock>("HomeGreetingTitleTextBlock")!;
        AssertColor(primaryTextColor, greeting.Foreground);

        for (var index = 1; index < carousel.Items.Count; index++)
        {
            carousel.SelectedIndex = index;
            Dispatcher.UIThread.RunJobs();
            var page = Assert.IsAssignableFrom<Control>(carousel.Items[index]);
            foreach (var title in page.GetLogicalDescendants().OfType<TextBlock>()
                         .Where(text => text.Classes.Contains("page-title")))
                AssertColor(primaryTextColor, title.Foreground);
            foreach (var card in page.GetLogicalDescendants().OfType<Border>()
                         .Where(border => border.Classes.Contains("card") || border.Classes.Contains("semi-card")))
                AssertColor(cardColor, card.Background);
        }
    }

    private static void AssertColor(string expected, IBrush? brush)
        => Assert.Equal(Color.Parse(expected), Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);
}
