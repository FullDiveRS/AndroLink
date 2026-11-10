using SkiaSharp;

namespace AndroLink.UI;

/// <summary>
/// Modal message box rendered on the window overlay with a scrim, fade-in
/// animation and Escape/Enter keyboard shortcuts. Non-blocking: returns a
/// Task with the index of the pressed button.
/// </summary>
public static class MessageBox
{
    public static Task<int> Show(Window owner, string title, string message, params string[] buttons)
    {
        if (buttons.Length == 0) buttons = new[] { "OK" };
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var theme = Theme.Current;

        var buttonsRow = new StackPanel(Orientation.Horizontal, 8)
        {
            HAlign = HAlignment.Right,
        };

        var body = new StackPanel(Orientation.Vertical, 14);
        body.Add(new TextBlock(title, 16, UiFonts.Weight.SemiBold));
        body.Add(new TextBlock(message, 13.5f) { Wrapping = true, TextColor = theme.TextSecondary, HAlign = HAlignment.Stretch });
        body.Add(buttonsRow);

        var dialog = new Border(body)
        {
            Background = theme.Surface,
            BorderBrush = theme.Border,
            BorderThickness = 1,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(22, 18),
            HAlign = HAlignment.Center,
            VAlign = VAlignment.Center,
            MinWidth = 420f,
        };

        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var isLast = i == buttons.Length - 1;
            var button = new Button(buttons[i], primary: isLast);
            button.Click += () =>
            {
                owner.Overlay.Remove(dialog);
                tcs.TrySetResult(index);
            };
            buttonsRow.Add(button);
        }

        // Scrim absorbs clicks outside the dialog and handles Escape/Enter.
        var scrim = new Border(dialog)
        {
            Background = theme.Scrim,
            HAlign = HAlignment.Stretch,
            VAlign = VAlignment.Stretch,
            Focusable = true,
        };

        void Finish(int result)
        {
            owner.Overlay.Remove(scrim);
            tcs.TrySetResult(result);
        }

        scrim.KeyDown += args =>
        {
            if (args.Key == VirtualKey.Escape)
            {
                args.Handled = true;
                Finish(0);
            }
            else if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                Finish(buttons.Length - 1);
            }
        };

        scrim.Padding = new Thickness(24);
        owner.Overlay.Add(scrim);
        scrim.Focus();

        // Fade in.
        scrim.Opacity = 0;
        Animator.Animate(0, 1, TimeSpan.FromMilliseconds(130), v => scrim.Opacity = (float)v, Easing.EaseOut, owner: scrim);

        return tcs.Task;
    }
}
