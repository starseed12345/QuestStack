using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace QuestStack;

internal sealed class DestructiveActionDialog : Window
{
    public DestructiveActionDialog(DeviceIdentity device)
    {
        Title = "Confirm Quest 1 unlock";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.Background;

        var title = Ui.Text("Unlock and erase this headset?", 22, Ui.PrimaryText, FontWeight.SemiBold);
        var intro = Ui.Text(
            $"This will unlock {device.Serial} and erase its user data.",
            14,
            Ui.SecondaryText);

        var warning = Ui.Card(
            Ui.Text("Keep the USB cable connected until the headset reboots.", 13, Ui.Danger, FontWeight.SemiBold),
            new Thickness(14));
        warning.BorderBrush = Ui.Danger;

        var cancel = Ui.SecondaryButton("Cancel");
        cancel.Click += (_, _) => Close(false);
        var confirm = Ui.PrimaryButton("Unlock and erase");
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
            Spacing = 18
        };
        content.Children.Add(title);
        content.Children.Add(intro);
        content.Children.Add(warning);
        content.Children.Add(actions);
        Content = content;

        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
                Close(false);
        };
    }
}
