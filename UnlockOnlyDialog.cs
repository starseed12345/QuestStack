using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace QuestStack;

internal sealed class UnlockOnlyDialog : Window
{
    public UnlockOnlyDialog()
    {
        Title = "Unlock only";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.Background;

        var cancel = Ui.SecondaryButton("Cancel");
        cancel.Click += (_, _) => Close(false);
        var confirm = Ui.PrimaryButton("Unlock");
        confirm.Click += (_, _) => Close(true);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };
        actions.Children.Add(cancel);
        actions.Children.Add(confirm);

        var content = new StackPanel
        {
            Margin = new Thickness(26),
            Spacing = 16
        };
        content.Children.Add(Ui.Text("Unlock bootloader?", 22, Ui.PrimaryText, FontWeight.SemiBold));
        content.Children.Add(Ui.Text(
            "Use this only when the headset is already in vulnerable fastboot. This mode does not flash, switch slots, wipe, or reboot.",
            14,
            Ui.SecondaryText));
        content.Children.Add(actions);
        Content = content;

        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
                Close(false);
        };
    }
}
