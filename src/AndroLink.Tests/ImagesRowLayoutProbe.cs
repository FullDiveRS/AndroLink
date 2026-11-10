using AndroLink.UI;
using Xunit;

namespace AndroLink.Tests;

/// <summary>
/// Numeric layout probe for the ImagesPage rows: prints FinalRect of every
/// child so vertical centering can be verified exactly.
/// </summary>
public class ImagesRowLayoutProbe
{
    private static string Dump(UIElement e, int depth = 0)
    {
        var pad = new string(' ', depth * 2);
        var line = $"{pad}{e} rect=({e.FinalRect.X:0.##},{e.FinalRect.Y:0.##} {e.FinalRect.Width:0.##}x{e.FinalRect.Height:0.##})";
        foreach (var c in e.VisualChildren) line += "\n" + Dump(c, depth + 1);
        return line;
    }

    [Fact]
    public void Available_row_children_are_vertically_centered()
    {
        var theme = Theme.Current = Theme.Dark();

        var row = new StackPanel(Orientation.Horizontal, 10)
        {
            Background = theme.Surface,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8),
            HAlign = HAlignment.Stretch,
        };
        row.Add(new TextBlock("google_apis", 12.5f, UiFonts.Weight.SemiBold) { VAlign = VAlignment.Center });
        row.Add(new Border(new TextBlock("x86_64", 11.5f, UiFonts.Weight.SemiBold) { TextColor = theme.Success })
        {
            Background = theme.SurfaceAlt,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 3),
            VAlign = VAlignment.Center,
        });
        row.Add(new Border(new TextBlock("recommended", 10.5f) { TextColor = theme.Accent })
        {
            BorderBrush = theme.Accent,
            BorderThickness = 1,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 2),
            VAlign = VAlignment.Center,
        });
        row.Add(new TextBlock("1.6 GB", 12f) { TextColor = theme.TextSecondary, VAlign = VAlignment.Center });
        row.Add(Spacer.Fill());
        row.Add(new Button("Download") { IconName = "download", MinWidth = 128 });

        row.MeasureInternal(new Size(1000, float.PositiveInfinity));
        row.ArrangeInternal(new Rect(0, 0, 1000, row.DesiredSize.Height));

        var dump = Dump(row);
        var rows = dump.Split('\n');

        // Every leaf must be centered within the row's content band.
        // Row content band (padding excluded): FinalRect.Y + Padding.Top .. bottom - Padding.Bottom.
        Assert.Equal(8, row.Padding.Vertical / 2); // sanity: padding (14,8)

        var contentTop = row.Padding.Top;
        var contentHeight = row.FinalRect.Height - row.Padding.Vertical;
        foreach (var child in row.Children)
        {
            var center = child.FinalRect.Y + child.FinalRect.Height / 2;
            var expected = contentTop + contentHeight / 2;
            Assert.True(Math.Abs(center - expected) < 0.6,
                $"{child} center {center:0.##} != row center {expected:0.##}\n{dump}");
        }
    }
}
