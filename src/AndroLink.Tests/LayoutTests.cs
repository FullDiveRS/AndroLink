using AndroLink.UI;

namespace AndroLink.Tests;

/// <summary>Layout engine math: measure/arrange for stack, grid, dock and scroll.</summary>
public class LayoutTests
{
    private sealed class FixedBox : UIElement
    {
        public FixedBox(float width, float height)
        {
            DesiredWidth = width;
            DesiredHeight = height;
            HAlign = HAlignment.Stretch;
            VAlign = VAlignment.Stretch;
        }

        private float DesiredWidth { get; }
        private float DesiredHeight { get; }

        protected override Size MeasureOverride(Size available) => new(DesiredWidth, DesiredHeight);
    }

    [Fact]
    public void StackPanel_vertical_accumulates_children()
    {
        var panel = new StackPanel(Orientation.Vertical, spacing: 6);
        panel.Add(new FixedBox(100, 30));
        panel.Add(new FixedBox(100, 40));
        panel.Add(new FixedBox(100, 50));

        panel.MeasureInternal(Size.Empty.With(width: 400, height: float.PositiveInfinity));
        panel.ArrangeInternal(new Rect(0, 0, 400, 200));

        Assert.Equal(30 + 6 + 40 + 6 + 50, panel.DesiredSize.Height, 0.5f);
        Assert.Equal(0, panel.Children[0].FinalRect.Y, 0.5f);
        Assert.Equal(36, panel.Children[1].FinalRect.Y, 0.5f);
        Assert.Equal(82, panel.Children[2].FinalRect.Y, 0.5f);
    }

    [Fact]
    public void GridPanel_star_columns_split_available_width()
    {
        var grid = new GridPanel()
            .AddColumn(LayoutLength.Star(1))
            .AddColumn(LayoutLength.Star(2));

        var a = grid.Add(new FixedBox(0, 20));
        var b = grid.Add(new FixedBox(0, 20));
        grid.SetCell(a, 0, 0);
        grid.SetCell(b, 1, 0);

        grid.MeasureInternal(new Size(900, 100));
        grid.ArrangeInternal(new Rect(0, 0, 900, 100));

        Assert.Equal(300, a.FinalRect.Width, 0.5f);
        Assert.Equal(600, b.FinalRect.Width, 0.5f);
        Assert.Equal(0, a.FinalRect.X, 0.5f);
        Assert.Equal(300, b.FinalRect.X, 0.5f);
    }

    [Fact]
    public void GridPanel_pixel_and_auto_columns_resolve()
    {
        var grid = new GridPanel()
            .AddColumn(120)
            .AddColumn(LayoutLength.Auto)
            .AddColumn(LayoutLength.Star());

        var pixel = grid.Add(new FixedBox(60, 20));
        var auto = grid.Add(new FixedBox(180, 20));
        var star = grid.Add(new FixedBox(0, 20));
        grid.SetCell(pixel, 0, 0);
        grid.SetCell(auto, 1, 0);
        grid.SetCell(star, 2, 0);

        grid.MeasureInternal(new Size(1000, 100));
        grid.ArrangeInternal(new Rect(0, 0, 1000, 100));

        Assert.Equal(120, pixel.FinalRect.Width, 0.5f);
        Assert.Equal(180, auto.FinalRect.Width, 0.5f);
        Assert.Equal(120, auto.FinalRect.X, 0.5f);
        Assert.Equal(700, star.FinalRect.Width, 0.5f);
        Assert.Equal(300, star.FinalRect.X, 0.5f);
    }

    [Fact]
    public void DockPanel_slices_edges_and_fills_last_child()
    {
        var dock = new DockPanel();
        var left = dock.Add(new FixedBox(80, 0));
        var top = dock.Add(new FixedBox(0, 40));
        var fill = dock.Add(new FixedBox(0, 0));
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(top, Dock.Top);

        dock.MeasureInternal(new Size(500, 300));
        dock.ArrangeInternal(new Rect(0, 0, 500, 300));

        Assert.Equal(80, left.FinalRect.Width, 0.5f);
        Assert.Equal(300, left.FinalRect.Height, 0.5f);
        Assert.Equal(40, top.FinalRect.Height, 0.5f);
        Assert.Equal(420, top.FinalRect.Width, 0.5f);
        Assert.Equal(420, fill.FinalRect.Width, 0.5f);
        Assert.Equal(260, fill.FinalRect.Height, 0.5f);
        Assert.Equal(80, fill.FinalRect.X, 0.5f);
        Assert.Equal(40, fill.FinalRect.Y, 0.5f);
    }

    [Fact]
    public void ScrollViewer_clamps_offsets_and_reports_scroll_need()
    {
        var content = new StackPanel(Orientation.Vertical);
        for (var i = 0; i < 20; i++)
        {
            content.Add(new FixedBox(200, 30) { Height = 30 });
        }

        var scroll = new ScrollViewer { Content = content };
        scroll.MeasureInternal(new Size(300, 200));
        scroll.ArrangeInternal(new Rect(0, 0, 300, 200));

        Assert.True(scroll.NeedsScroll);

        scroll.SetScrollOffset(10_000);
        Assert.Equal(20 * 30 - 200, scroll.ScrollOffsetY, 0.5f);

        scroll.ScrollToTop();
        Assert.Equal(0, scroll.ScrollOffsetY, 0.5f);
    }

    [Fact]
    public void Border_applies_padding_to_child()
    {
        var child = new FixedBox(100, 40);
        var border = new Border(child) { Padding = new Thickness(10, 8) };

        border.MeasureInternal(new Size(400, 200));
        border.ArrangeInternal(new Rect(0, 0, 400, 200));

        Assert.Equal(120, border.DesiredSize.Width, 0.5f);
        Assert.Equal(56, border.DesiredSize.Height, 0.5f);
        Assert.Equal(10, child.FinalRect.X, 0.5f);
        Assert.Equal(8, child.FinalRect.Y, 0.5f);
    }

    [Fact]
    public void Theme_palettes_cover_required_roles()
    {
        foreach (var theme in new[] { Theme.Dark(), Theme.Light() })
        {
            Assert.NotEqual(default, theme.WindowBackground);
            Assert.NotEqual(default, theme.Accent);
            Assert.NotEqual(theme.TextPrimary, theme.WindowBackground);
        }
    }
}
