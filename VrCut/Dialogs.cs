using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace VrCut;

/// <summary>Small modal dialogs built in code (Avalonia has no built-in message box).</summary>
public static class Dialogs
{
    public static Task<bool> ConfirmAsync(Window owner, string title, string message, string okText, bool danger = false)
    {
        var dialog = CreateWindow(title);
        // For destructive actions Enter must not confirm by accident.
        var ok = new Button { Content = okText, IsDefault = !danger };
        var cancel = new Button { Content = "Cancelar", IsCancel = true };
        ok.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        if (danger)
            ok.Background = new SolidColorBrush(Color.FromRgb(196, 43, 28));
        else
            ok.Classes.Add("accent");

        dialog.Content = Layout(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, cancel, ok);
        dialog.Opened += (_, _) => (danger ? cancel : ok).Focus();
        return dialog.ShowDialog<bool>(owner);
    }

    /// <summary>Asks for a text value. The first <paramref name="selectLength"/> characters start selected.</summary>
    public static Task<string?> PromptAsync(Window owner, string title, string message, string initial, int selectLength)
    {
        var dialog = CreateWindow(title);
        var box = new TextBox { Text = initial };
        var ok = new Button { Content = "Aceptar", IsDefault = true, Classes = { "accent" } };
        var cancel = new Button { Content = "Cancelar", IsCancel = true };
        ok.Click += (_, _) => dialog.Close(box.Text);
        cancel.Click += (_, _) => dialog.Close(null);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                dialog.Close(box.Text);
        };

        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(box);
        dialog.Content = Layout(body, cancel, ok);
        dialog.Opened += (_, _) =>
        {
            box.Focus();
            box.SelectionStart = 0;
            box.SelectionEnd = Math.Clamp(selectLength, 0, initial.Length);
        };
        return dialog.ShowDialog<string?>(owner);
    }

    private static Window CreateWindow(string title) => new()
    {
        Title = title,
        Width = 460,
        SizeToContent = SizeToContent.Height,
        CanResize = false,
        ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner
    };

    private static Control Layout(Control body, params Button[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 16, 0, 0)
        };
        foreach (var b in buttons)
        {
            b.MinWidth = 90;
            b.Margin = new Avalonia.Thickness(0);
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            row.Children.Add(b);
        }

        var root = new StackPanel { Margin = new Avalonia.Thickness(20) };
        root.Children.Add(body);
        root.Children.Add(row);
        return root;
    }
}
