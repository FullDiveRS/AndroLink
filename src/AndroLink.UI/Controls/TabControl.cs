using SkiaSharp;

namespace AndroLink.UI;

public sealed class TabItem
{
    public string Header { get; init; } = "";
    public UIElement? Content { get; init; }
    public object? Tag { get; init; }
}

/// <summary>
/// Top tab strip with animated content switching. Headers are custom-rendered
/// (text + accent underline for the selected tab).
/// </summary>
public class TabControl : UIElement
{
    private const float HeaderHeight = 42f;
    private const float TabPadding = 16f;

    private readonly Border _host = new() { HAlign = HAlignment.Stretch, VAlign = VAlignment.Stretch };
    private readonly List<(TabItem Item, float Width)> _headerWidths = new();
    private int _selectedIndex;
    private float _hoverIndex = -1;

    public List<TabItem> Tabs { get; } = new();

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, Math.Max(0, Tabs.Count - 1));
            if (_selectedIndex == clamped && _host.Child is not null) return;
            _selectedIndex = clamped;
            ApplySelection();
        }
    }

    public TabItem? SelectedTab => Tabs.Count == 0 ? null : Tabs[Math.Clamp(_selectedIndex, 0, Tabs.Count - 1)];

    public event Action? SelectionChanged;

    public TabControl()
    {
        ClipToBounds = true;
    }

    protected internal override IEnumerable<UIElement> VisualChildren => new[] { _host };

    public TabItem AddTab(string header, UIElement content, object? tag = null)
    {
        var item = new TabItem { Header = header, Content = content, Tag = tag };
        Tabs.Add(item);
        if (Tabs.Count == 1)
        {
            _selectedIndex = 0;
            ApplySelection();
        }
        InvalidateMeasure();
        return item;
    }

    private void ApplySelection()
    {
        var tab = SelectedTab;
        _host.Child = tab?.Content;
        _host.Opacity = 0;
        var owner = this;
        Animator.Animate(0, 1, TimeSpan.FromMilliseconds(160), v => _host.Opacity = (float)v, Easing.EaseOut, owner: owner);
        InvalidateMeasure();
        SelectionChanged?.Invoke();
    }

    protected override Size MeasureOverride(Size available)
    {
        _headerWidths.Clear();
        float headerTotal = 0;
        foreach (var tab in Tabs)
        {
            var w = UiFonts.MeasureWidth(tab.Header, 13.5f, UiFonts.Weight.SemiBold) + TabPadding * 2;
            _headerWidths.Add((tab, w));
            headerTotal += w;
        }

        float contentW = 0, contentH = 0;
        var content = SelectedTab?.Content;
        if (content is not null)
        {
            content.MeasureInternal(new Size(
                float.IsPositiveInfinity(available.Width) ? float.PositiveInfinity : Math.Max(0, available.Width),
                float.IsPositiveInfinity(available.Height) ? float.PositiveInfinity : Math.Max(0, available.Height - HeaderHeight)));
            contentW = content.DesiredSize.Width;
            contentH = content.DesiredSize.Height;
        }

        var width = float.IsPositiveInfinity(available.Width) ? Math.Max(headerTotal, contentW) : available.Width;
        var height = float.IsPositiveInfinity(available.Height)
            ? HeaderHeight + contentH
            : available.Height;

        return new Size(width, height);
    }

    protected override void ArrangeOverride(Rect contentRect)
    {
        _host.MeasureInternal(new Size(contentRect.Width, Math.Max(0, contentRect.Height - HeaderHeight)));
        _host.ArrangeInternal(new Rect(0, HeaderHeight, contentRect.Width,
            Math.Max(0, contentRect.Height - HeaderHeight)));
    }

    protected override void OnPointerPress(PointerEventArgs args)
    {
        // Only the tab strip belongs to this control; content clicks must fall
        // through to the page hosted below the headers.
        if (args.Y > HeaderHeight) return;
        var index = IndexAt(args.X);
        if (index >= 0)
        {
            SelectedIndex = index;
            args.Handled = true;
        }
    }

    protected override void OnPointerMove(PointerEventArgs args)
    {
        var index = (float)IndexAt(args.X);
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExit(PointerEventArgs args)
    {
        _hoverIndex = -1;
        InvalidateVisual();
    }

    private int IndexAt(float x)
    {
        float cursor = 0;
        for (var i = 0; i < _headerWidths.Count; i++)
        {
            var w = _headerWidths[i].Width;
            if (x >= cursor && x < cursor + w) return i;
            cursor += w;
        }
        return -1;
    }

    protected internal override void OnRender(SKCanvas canvas, Theme theme)
    {
        // Header background
        UiRenderer.FillRoundRect(canvas, 0, 0, FinalRect.Width, HeaderHeight, 0, theme.Surface);

        float cursor = 0;
        for (var i = 0; i < _headerWidths.Count; i++)
        {
            var (tab, w) = _headerWidths[i];
            var selected = i == _selectedIndex;
            var hovered = i == (int)_hoverIndex && !selected;
            var color = selected ? theme.TextPrimary : hovered ? theme.TextPrimary : theme.TextSecondary;

            UiFonts.DrawTextCenteredY(canvas, tab.Header, cursor + TabPadding, HeaderHeight / 2f, 13.5f,
                color, UiFonts.Weight.SemiBold);

            if (selected)
            {
                var textW = UiFonts.MeasureWidth(tab.Header, 13.5f, UiFonts.Weight.SemiBold);
                UiRenderer.FillRoundRect(canvas, cursor + TabPadding, HeaderHeight - 3, textW, 3, 1.5f, theme.Accent);
            }
            cursor += w;
        }

        UiRenderer.DrawLine(canvas, 0, HeaderHeight - 0.5f, FinalRect.Width, HeaderHeight - 0.5f, theme.Border);
    }
}
