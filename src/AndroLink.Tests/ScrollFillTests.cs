using AndroLink.UI;
using Xunit;

namespace AndroLink.Tests;

/// <summary>
/// Regression tests for scrolling in page layouts: a fill control (ScrollViewer)
/// placed after fixed headers must receive exactly the remaining height, both
/// pages stay inside the window on small sizes, and overflowed content becomes
/// reachable (scroll to end + hit-test the last control).
/// </summary>
public class ScrollFillTests
{
    private sealed class FillPage : UIElement
    {
        public StackPanel ContentStack = null!;
        public ScrollViewer Scroll = null!;
        public Button LastButton = null!;

        public FillPage(int listRows)
        {
            ClipToBounds = true;

            var header = new StackPanel(Orientation.Vertical, 4) { Padding = new Thickness(0, 0, 0, 12) };
            header.Add(new TextBlock("Android images", 21, UiFonts.Weight.Bold));

            var toolbar = new StackPanel(Orientation.Horizontal, 10) { Padding = new Thickness(0, 0, 0, 6) };
            toolbar.Add(new Button("Refresh catalog", null, primary: true));

            Scroll = new ScrollViewer { HAlign = HAlignment.Stretch, VAlign = VAlignment.Stretch };
            var list = new StackPanel(Orientation.Vertical, 8);
            Button? last = null;
            for (var i = 0; i < listRows; i++)
            {
                var row = new StackPanel(Orientation.Horizontal, 10)
                {
                    Background = Theme.Current.Surface,
                    Padding = new Thickness(14, 8),
                    HAlign = HAlignment.Stretch,
                };
                row.Add(new TextBlock($"Image row {i}", 12.5f) { VAlign = VAlignment.Center });
                row.Add(Spacer.Fill());
                last = new Button($"Action {i}") { MinWidth = 110 };
                row.Add(last);
                list.Add(row);
            }
            LastButton = last!;
            Scroll.Content = list;

            ContentStack = new StackPanel(Orientation.Vertical)
            {
                Padding = new Thickness(26, 22, 26, 16),
                HAlign = HAlignment.Stretch,
                VAlign = VAlignment.Stretch,
            };
            ContentStack.Add(header);
            ContentStack.Add(toolbar);
            ContentStack.Add(Scroll);

            Add(new Border(ContentStack)
            {
                HAlign = HAlignment.Center,
                VAlign = VAlignment.Stretch,
                MaxWidth = 1120,
            });
        }
    }

    private static (Window Window, FillPage Page, Border Host) BuildShell(float width, float height)
    {
        var theme = Theme.Current = Theme.Dark();
        var window = new Window { InitialClientSize = new Size(width, height) };

        var page = new FillPage(listRows: 24);
        var host = new Border
        {
            Background = theme.WindowBackground,
            HAlign = HAlignment.Stretch,
            VAlign = VAlignment.Stretch,
        };
        host.Child = page;

        var nav = new StackPanel(Orientation.Vertical) { Width = 208, Background = theme.Surface };
        var statusBar = new Border(new TextBlock("status", 11.5f))
        {
            Background = theme.Surface,
            Height = 30,
            VAlign = VAlignment.Stretch,
        };

        var root = new DockPanel();
        DockPanel.SetDock(nav, Dock.Left);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Add(nav);
        root.Add(statusBar);
        root.Add(host);

        window.Content = root;
        window.ResizeFromPlatform(width, height);
        window.DoLayout();
        return (window, page, host);
    }

    [Fact]
    public void Scrollviewer_receives_exactly_the_remaining_height()
    {
        var (window, page, host) = BuildShell(1200, 780);

        var stack = page.ContentStack;

        // The stack (including its own padding) must fit inside the page area.
        Assert.True(stack.DesiredSize.Height <= host.FinalRect.Height + 0.5f,
            $"stack desired {stack.DesiredSize.Height:0.#} exceeds page area {host.FinalRect.Height:0.#}");

        // Scroll = full remaining height after header + toolbar.
        var contentBand = stack.FinalRect.Height - stack.Padding.Vertical;
        var fixedHeight = stack.Children[0].DesiredSize.Height + stack.Children[1].DesiredSize.Height;
        var expectedViewport = contentBand - fixedHeight;
        Assert.Equal(expectedViewport, page.Scroll.FinalRect.Height, 0.5f);
        Assert.Equal(expectedViewport, page.Scroll.ViewportHeight, 0.5f);
        Assert.True(page.Scroll.NeedsScroll, "tall list should be scrollable");
    }

    [Fact]
    public void Minimum_window_size_keeps_toolbar_buttons_inside_and_clickable()
    {
        var (window, page, _) = BuildShell(560, 380);

        var toolbarButton = (Button)page.ContentStack.Children[1].Children[0];
        var (ax, ay) = toolbarButton.AbsolutePosition();
        var bottom = ay + toolbarButton.FinalRect.Height;
        Assert.True(bottom <= window.FinalRect.Height + 0.5f,
            $"toolbar bottom {bottom:0.#} falls outside the {window.FinalRect.Height:0.#} high client area");

        var cx = ax + toolbarButton.FinalRect.Width / 2;
        var cy = ay + toolbarButton.FinalRect.Height / 2;
        var hit = window.HitTest(cx, cy);
        Assert.True(ReferenceEquals(hit, toolbarButton), $"hit {hit} instead of the toolbar button");

        var clicked = false;
        toolbarButton.Click += () => clicked = true;
        window.DispatchPointerPress(cx, cy);
        window.DispatchPointerRelease(cx, cy);
        Assert.True(clicked, "toolbar button click did not fire at minimum window size");
    }

    [Fact]
    public void Scrolling_to_end_reaches_and_clicks_the_last_row_button()
    {
        var (window, page, _) = BuildShell(900, 600);

        page.Scroll.ScrollToEnd();
        window.DoLayout();

        var (ax, ay) = page.LastButton.AbsolutePosition();
        var cx = ax + page.LastButton.FinalRect.Width / 2;
        var cy = ay + page.LastButton.FinalRect.Height / 2;

        Assert.True(cy >= 0 && cy <= window.FinalRect.Height,
            $"last button center {cy:0.#} is outside the window after scrolling to the end");

        var clicked = false;
        page.LastButton.Click += () => clicked = true;
        window.DispatchPointerMove(cx, cy);
        window.DispatchPointerPress(cx, cy);
        window.DispatchPointerRelease(cx, cy);
        Assert.True(clicked, "last row button was not clickable after scrolling to the end");
    }

    [Fact]
    public void Wheel_over_the_list_scrolls_the_page_scrollviewer()
    {
        var (window, page, _) = BuildShell(900, 600);

        var before = page.Scroll.ScrollOffsetY;

        // Wheel over the middle of the visible list area (the scroll viewport).
        var (sx, sy) = page.Scroll.AbsolutePosition();
        var cx = sx + page.Scroll.FinalRect.Width / 2;
        var cy = sy + Math.Min(page.Scroll.FinalRect.Height / 2, 200);
        Assert.True(page.Scroll.AbsoluteBounds().Contains(cx, cy), "wheel point is not inside the scroll viewport");

        window.DispatchPointerWheel(cx, cy, -3); // wheel down
        Assert.True(page.Scroll.ScrollOffsetY > before,
            $"wheel over the list did not scroll (offset {before:0.#} -> {page.Scroll.ScrollOffsetY:0.#})");
    }
}
