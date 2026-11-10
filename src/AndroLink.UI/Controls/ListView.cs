using SkiaSharp;

namespace AndroLink.UI;

/// <summary>Column definition for <see cref="ListView"/> (pixel widths keep header and rows aligned).</summary>
public sealed class ListViewColumn
{
    public string Header { get; init; } = "";
    public float Width { get; init; } = 120;
    public Func<object, UIElement>? CellFactory { get; init; }
    public Func<object, string>? TextFactory { get; init; }
}

/// <summary>
/// Columnar list: sticky header + scrollable rows, selection and double-click
/// activation. Cell content comes from per-column factories.
/// </summary>
public class ListView : UIElement
{
    private const float HeaderHeight = 34f;
    private const float RowHeight = 36f;

    private readonly StackPanel _headerRow = new(Orientation.Horizontal);
    private readonly ScrollViewer _scroll = new();
    private readonly StackPanel _rows = new(Orientation.Vertical);

    public List<ListViewColumn> Columns { get; } = new();
    public List<object> Items { get; } = new();

    private int _selectedIndex = -1;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value) return;
            _selectedIndex = value;
            UpdateRowSelection();
            SelectionChanged?.Invoke();
        }
    }

    public object? SelectedItem => _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : null;

    public event Action? SelectionChanged;
    public event Action<object>? ItemActivated;

    public ListView()
    {
        HAlign = HAlignment.Stretch;
        VAlign = VAlignment.Stretch;
        _scroll.Content = _rows;
        _scroll.SetScrollOffset(0);
    }

    protected internal override IEnumerable<UIElement> VisualChildren => new UIElement[] { _headerRow, _scroll };

    public void Refresh()
    {
        RebuildHeader();
        RebuildRows();
        InvalidateMeasure();
    }

    private void RebuildHeader()
    {
        _headerRow.ClearChildren();
        foreach (var column in Columns)
        {
            var cell = new Border(new TextBlock(column.Header, 12.5f, UiFonts.Weight.SemiBold)
            {
                TextColor = Theme.Current.TextSecondary,
            })
            {
                Width = column.Width,
                Height = HeaderHeight,
                VAlign = VAlignment.Stretch,
            };
            _headerRow.Add(cell);
        }
    }

    private void RebuildRows()
    {
        _rows.ClearChildren();
        for (var i = 0; i < Items.Count; i++)
        {
            var index = i;
            var item = Items[i];

            var cells = new StackPanel(Orientation.Horizontal);
            foreach (var column in Columns)
            {
                UIElement content;
                if (column.CellFactory is not null)
                {
                    content = column.CellFactory(item);
                }
                else
                {
                    var text = column.TextFactory?.Invoke(item) ?? item.ToString() ?? "";
                    content = new TextBlock(text) { Wrapping = false };
                }
                content.HAlign = HAlignment.Left;
                content.VAlign = VAlignment.Center;
                var cell = new Border(content)
                {
                    Width = column.Width,
                    Height = RowHeight,
                    Padding = new Thickness(0, 0, 8, 0),
                };
                cells.Add(cell);
            }

            var row = new Border(cells)
            {
                HAlign = HAlignment.Stretch,
                Height = RowHeight,
                Tag = index,
            };

            row.PointerPressed += _ => SelectedIndex = index;
            row.PointerReleased += args =>
            {
                if (args.IsOverTarget && _lastClickIndex == index && Environment.TickCount64 - _lastClickMs < 420)
                {
                    ItemActivated?.Invoke(item);
                }
                _lastClickIndex = index;
                _lastClickMs = Environment.TickCount64;
            };
            row.PointerEntered += _ => { if (index != SelectedIndex) row.Background = Theme.Current.SurfaceHover; };
            row.PointerExited += _ => { if (index != SelectedIndex) row.Background = null; };

            _rows.Add(row);
        }
        UpdateRowSelection();
    }

    private int _lastClickIndex = -1;
    private long _lastClickMs;

    private void UpdateRowSelection()
    {
        var i = 0;
        foreach (var row in _rows.Children)
        {
            row.Background = i == SelectedIndex ? Theme.Current.Selection : null;
            i++;
        }
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size available)
    {
        var width = float.IsPositiveInfinity(available.Width) ? Columns.Sum(c => c.Width) : available.Width;
        var contentH = HeaderHeight + Items.Count * RowHeight;
        var height = float.IsPositiveInfinity(available.Height) ? contentH : available.Height;
        return new Size(width, height);
    }

    protected override void ArrangeOverride(Rect contentRect)
    {
        _headerRow.MeasureInternal(new Size(contentRect.Width, HeaderHeight));
        _headerRow.ArrangeInternal(new Rect(0, 0, contentRect.Width, HeaderHeight));
        _scroll.MeasureInternal(new Size(contentRect.Width, Math.Max(0, contentRect.Height - HeaderHeight)));
        _scroll.ArrangeInternal(new Rect(0, HeaderHeight, contentRect.Width, Math.Max(0, contentRect.Height - HeaderHeight)));
    }

    protected internal override void OnRender(SKCanvas canvas, Theme theme)
    {
        // Header strip
        UiRenderer.FillRoundRect(canvas, 0, 0, FinalRect.Width, HeaderHeight - 1, 4, theme.SurfaceAlt);
        UiRenderer.DrawLine(canvas, 0, HeaderHeight - 0.5f, FinalRect.Width, HeaderHeight - 0.5f, theme.Border);
    }
}
