using AndroLink.UI;
using SkiaSharp;

namespace AndroLink.Tests;

/// <summary>
/// Headless render smoke tests: buttons must actually paint their content
/// (regression for the "empty buttons" bug), every vector icon must produce
/// pixels, and the alignment rules (stretch / center / MaxWidth) must hold.
/// </summary>
public class RenderSmokeTests
{
    private static (SKSurface Surface, SKCanvas Canvas) Surface(int w, int h)
    {
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        var surface = SKSurface.Create(info);
        surface.Canvas.Clear(new SKColor(20, 20, 24));
        return (surface, surface.Canvas);
    }

    private static int CountPixelsNotOf(SKSurface surface, SKColor background)
    {
        using var image = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(image);
        var count = 0;
        for (var y = 0; y < bmp.Height; y++)
        {
            for (var x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (Math.Abs(c.Red - background.Red) > 24
                    || Math.Abs(c.Green - background.Green) > 24
                    || Math.Abs(c.Blue - background.Blue) > 24)
                {
                    count++;
                }
            }
        }
        return count;
    }

    [Fact]
    public void Button_renders_its_text_content()
    {
        var theme = Theme.Dark();
        Theme.Current = theme;

        var (surface, canvas) = Surface(140, 44);
        var button = new Button("Start") { IconName = "play" };
        button.MeasureInternal(new Size(140, 44));
        button.ArrangeInternal(new Rect(0, 0, 140, 44));

        // RenderTree is the production entry point - it must walk Button content.
        UiRenderer.RenderTree(button, canvas);
        surface.Flush();

        var painted = CountPixelsNotOf(surface, new SKColor(20, 20, 24));
        // Background fill + border + icon + text: well over a hundred pixels.
        Assert.True(painted > 150, $"button painted only {painted} pixels - text/icon content is missing");
    }

    [Fact]
    public void Button_without_content_paints_much_less_than_with_content()
    {
        Theme.Current = Theme.Dark();

        var (surface, canvas) = Surface(140, 44);
        var empty = new Button();
        empty.MeasureInternal(new Size(140, 44));
        empty.ArrangeInternal(new Rect(0, 0, 140, 44));
        UiRenderer.RenderTree(empty, canvas);
        surface.Flush();
        var emptyPixels = CountPixelsNotOf(surface, new SKColor(20, 20, 24));

        var (surface2, canvas2) = Surface(140, 44);
        var full = new Button("Start") { IconName = "play" };
        full.MeasureInternal(new Size(140, 44));
        full.ArrangeInternal(new Rect(0, 0, 140, 44));
        UiRenderer.RenderTree(full, canvas2);
        surface2.Flush();
        var fullPixels = CountPixelsNotOf(surface2, new SKColor(20, 20, 24));

        Assert.True(fullPixels > emptyPixels + 80,
            $"content button ({fullPixels}px) should paint clearly more than empty ({emptyPixels}px)");
    }

    [Fact]
    public void IconGlyph_draws_every_icon()
    {
        foreach (var name in IconGlyph.Names)
        {
            var (surface, canvas) = Surface(32, 32);
            IconGlyph.Draw(canvas, name, 4, 4, 24, SKColors.White);
            surface.Flush();
            var pixels = CountPixelsNotOf(surface, new SKColor(20, 20, 24));
            Assert.True(pixels > 10, $"icon '{name}' produced no visible pixels");
            surface.Dispose();
        }
    }

    [Fact]
    public void StackPanel_vertical_stretch_children_fill_width()
    {
        var panel = new StackPanel(Orientation.Vertical, 6);
        var stretch = panel.Add(new TextBlock("fills") { HAlign = HAlignment.Stretch });
        var centered = panel.Add(new TextBlock("centered", 13f) { HAlign = HAlignment.Center, Width = 100 });

        panel.MeasureInternal(new Size(500, float.PositiveInfinity));
        panel.ArrangeInternal(new Rect(0, 0, 500, 200));

        Assert.Equal(500, stretch.FinalRect.Width, 0.5f);
        Assert.Equal(200, centered.FinalRect.X, 0.5f); // (500 - 100) / 2
    }

    [Fact]
    public void Spacer_fill_expands_in_horizontal_panel()
    {
        var panel = new StackPanel(Orientation.Horizontal, 8);
        var button = panel.Add(new Button("Left"));
        var filler = panel.Add(Spacer.Fill());
        var right = panel.Add(new Button("Right"));

        panel.MeasureInternal(new Size(600, float.PositiveInfinity));
        panel.ArrangeInternal(new Rect(0, 0, 600, 40));

        Assert.True(filler.FinalRect.Width > 100, $"spacer only {filler.FinalRect.Width}px wide");
        Assert.True(right.FinalRect.Right <= 600.5f, "right button pushed out of the panel");
        Assert.True(button.FinalRect.Width < 150);
    }

    [Fact]
    public void MaxWidth_centers_page_columns()
    {
        var page = new UIElementWithCenteredChild();
        page.MeasureInternal(new Size(1200, 600));
        page.ArrangeInternal(new Rect(0, 0, 1200, 600));

        var child = page.VisualChildren.First();
        Assert.Equal(800, child.FinalRect.Width, 0.5f);
        Assert.Equal(200, child.FinalRect.X, 0.5f); // (1200 - 800) / 2
    }

    private sealed class UIElementWithCenteredChild : UIElement
    {
        public UIElementWithCenteredChild()
        {
            Add(new FixedContent { HAlign = HAlignment.Center, VAlign = VAlignment.Stretch, MaxWidth = 800 });
        }

        private sealed class FixedContent : UIElement
        {
            protected override Size MeasureOverride(Size available) => new(800, 300);
        }
    }
}
