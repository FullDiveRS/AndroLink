using AndroLink.UI;
using SkiaSharp;
using Xunit;

namespace AndroLink.Tests;

/// <summary>
/// Reproduces the Manager shell structure (nav rail + page host with toolbar
/// buttons inside a centered column) and verifies hit-testing reaches the
/// inner buttons.
/// </summary>
public class HitTestProbeTests
{
    private sealed class ProbePage : UIElement
    {
        public Button ToolbarButton = null!;
        public ScrollViewer Scroll = null!;

        public ProbePage()
        {
            ClipToBounds = true;

            var toolbar = new StackPanel(Orientation.Horizontal, 10)
            {
                Padding = new Thickness(0, 0, 0, 14),
            };
            ToolbarButton = new Button("Create device", null, primary: true);
            toolbar.Add(ToolbarButton);
            toolbar.Add(new Button("Start"));
            toolbar.Add(Spacer.Fill());

            var header = new StackPanel(Orientation.Vertical, 4) { Padding = new Thickness(0, 0, 0, 16) };
            header.Add(new TextBlock("Devices", 21, UiFonts.Weight.Bold));

            Scroll = new ScrollViewer { HAlign = HAlignment.Stretch, VAlign = VAlignment.Stretch };
            var cards = new StackPanel(Orientation.Vertical, 10);
            for (var i = 0; i < 3; i++)
            {
                var card = new Border
                {
                    Background = Theme.Current.Surface,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(18, 14),
                    HAlign = HAlignment.Stretch,
                };
                var row = new StackPanel(Orientation.Horizontal, 12);
                row.Add(new TextBlock($"Card {i}", 14.5f, UiFonts.Weight.SemiBold) { VAlign = VAlignment.Center });
                row.Add(Spacer.Fill());
                row.Add(new Button("Start") { MinWidth = 96 });
                row.Add(new Button("Stop") { MinWidth = 96 });
                card.Child = row;
                cards.Add(card);
            }
            Scroll.Content = cards;

            var content = new StackPanel(Orientation.Vertical)
            {
                Padding = new Thickness(26, 22, 26, 16),
                HAlign = HAlignment.Stretch,
                VAlign = VAlignment.Stretch,
            };
            content.Add(header);
            content.Add(toolbar);
            content.Add(Scroll);

            var centered = new Border(content)
            {
                HAlign = HAlignment.Center,
                VAlign = VAlignment.Stretch,
                MaxWidth = 1120,
            };
            Add(centered);
        }
    }

    private static (Window Window, ProbePage Page, Button NavProbe) BuildShell()
    {
        var theme = Theme.Current = Theme.Dark();
        var window = new Window { InitialClientSize = new Size(1200, 780) };

        var page = new ProbePage();

        var host = new Border
        {
            Background = theme.WindowBackground,
            HAlign = HAlignment.Stretch,
            VAlign = VAlignment.Stretch,
        };
        host.Child = page;

        // Nav rail stand-in: same docking as the real shell.
        var nav = new StackPanel(Orientation.Vertical) { Width = 208, Background = theme.Surface };

        var statusText = new TextBlock("status", 11.5f);
        var statusBar = new Border(statusText)
        {
            Background = theme.Surface,
            Height = 30,
            Padding = new Thickness(16, 0),
            VAlign = VAlignment.Stretch,
        };

        var root = new DockPanel();
        DockPanel.SetDock(nav, Dock.Left);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Add(nav);
        root.Add(statusBar);
        root.Add(host);

        window.Content = root;
        return (window, page, new Button("nav", null));
    }

    private static string Describe(UIElement? e)
    {
        var chain = new List<string>();
        var cur = e;
        while (cur is not null)
        {
            chain.Add($"{cur} @({cur.FinalRect.X:0},{cur.FinalRect.Y:0} {cur.FinalRect.Width:0}x{cur.FinalRect.Height:0}) abs={cur.AbsoluteBounds()}");
            cur = cur.Parent;
        }
        return string.Join(" <- ", chain);
    }

    [Fact]
    public void Toolbar_button_is_hit_at_its_absolute_center()
    {
        var (window, page, _) = BuildShell();
        window.ResizeFromPlatform(1200, 780);
        window.DoLayout();

        var b = page.ToolbarButton;
        Assert.True(b.FinalRect.Width > 0, $"button not arranged: {Describe(b)}");

        var (ax, ay) = b.AbsolutePosition();
        var hit = window.HitTest(ax + b.FinalRect.Width / 2, ay + b.FinalRect.Height / 2);

        Assert.True(ReferenceEquals(hit, b), $"hit {Describe(hit)}\nexpected {Describe(b)}");
    }

    [Fact]
    public void Card_button_inside_scrollviewer_is_hit()
    {
        var (window, page, _) = BuildShell();
        window.ResizeFromPlatform(1200, 780);
        window.DoLayout();

        var cardButton = page.Scroll.Content!.Children.OfType<Border>().First()
            .Child!.Children.OfType<Button>().First();
        Assert.True(cardButton.FinalRect.Width > 0);

        var (ax, ay) = cardButton.AbsolutePosition();
        var hit = window.HitTest(ax + cardButton.FinalRect.Width / 2, ay + cardButton.FinalRect.Height / 2);

        Assert.True(ReferenceEquals(hit, cardButton), $"hit {Describe(hit)}\nexpected {Describe(cardButton)}");
    }

    [Fact]
    public void Click_pipeline_fires_for_toolbar_button()
    {
        var (window, page, _) = BuildShell();
        window.ResizeFromPlatform(1200, 780);
        window.DoLayout();

        var b = page.ToolbarButton;
        var (ax, ay) = b.AbsolutePosition();
        var cx = ax + b.FinalRect.Width / 2;
        var cy = ay + b.FinalRect.Height / 2;

        var clicked = false;
        b.Click += () => clicked = true;

        window.DispatchPointerMove(cx, cy);
        window.DispatchPointerPress(cx, cy);
        window.DispatchPointerRelease(cx, cy);

        Assert.True(clicked, "Click did not fire");
    }

    [Fact]
    public void Hovering_button_over_its_label_marks_it_pointer_over()
    {
        var (window, page, _) = BuildShell();
        window.ResizeFromPlatform(1200, 780);
        window.DoLayout();

        var b = page.ToolbarButton;
        var (ax, ay) = b.AbsolutePosition();
        // Point near the left edge but still over the label area.
        window.DispatchPointerMove(ax + 20, ay + b.FinalRect.Height / 2);

        Assert.True(b.IsPointerOver, "Button does not receive hover when the pointer is over its label");
    }

    [Fact]
    public void Clicking_directly_on_button_label_still_fires_click()
    {
        var (window, page, _) = BuildShell();
        window.ResizeFromPlatform(1200, 780);
        window.DoLayout();

        var b = page.ToolbarButton;
        var (ax, ay) = b.AbsolutePosition();

        var clicked = false;
        b.Click += () => clicked = true;

        // (20, mid) is over the label text, not the bare button chrome.
        window.DispatchPointerPress(ax + 20, ay + b.FinalRect.Height / 2);
        window.DispatchPointerRelease(ax + 20, ay + b.FinalRect.Height / 2);

        Assert.True(clicked, "Click on label did not fire");
    }
}
