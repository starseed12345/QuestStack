using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace QuestStack;

internal sealed class QuestStackApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());

        Resources["AppBackground"] = new SolidColorBrush(Color.Parse("#0B0E12"));
        Resources["PanelBackground"] = new SolidColorBrush(Color.Parse("#12171D"));
        Resources["PanelRaised"] = new SolidColorBrush(Color.Parse("#171D24"));
        Resources["PanelBorder"] = new SolidColorBrush(Color.Parse("#2A323D"));
        Resources["TextPrimary"] = new SolidColorBrush(Color.Parse("#F2F5F7"));
        Resources["TextSecondary"] = new SolidColorBrush(Color.Parse("#98A4B3"));
        Resources["Accent"] = new SolidColorBrush(Color.Parse("#D7A84B"));
        Resources["Success"] = new SolidColorBrush(Color.Parse("#56C596"));
        Resources["Danger"] = new SolidColorBrush(Color.Parse("#F06A6A"));
        Resources["Info"] = new SolidColorBrush(Color.Parse("#6EA8D9"));
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
