using System.Text;
using AndroLink.Core;
using AndroLink.UI;
using SkiaSharp;

namespace AndroLink.Manager;

/// <summary>Left navigation rail: vector logo, nav items with vector icons, selection handling.</summary>
public sealed class NavRail : UIElement
{
    public sealed class NavItem
    {
        public string Id { get; init; } = "";
        public string Label { get; init; } = "";
        public string Icon { get; init; } = "phone";
    }

    private const float ItemHeight = 44f;
    private const float LogoHeight = 84f;

    private readonly List<NavItem> _items = new();
    private readonly float[] _itemY = Array.Empty<float>();
    private int _selectedIndex;
    private int _hoverIndex = -1;

    public event Action<int>? SelectionChanged;

    public NavRail(params (string Id, string Label, string Icon)[] items)
    {
        foreach (var (id, label, icon) in items)
        {
            _items.Add(new NavItem { Id = id, Label = label, Icon = IconGlyph.Exists(icon) ? icon : "phone" });
        }
        Width = 208f;
        Background = Theme.Current.Surface;
        ClipToBounds = true;
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value) return;
            _selectedIndex = Math.Clamp(value, 0, Math.Max(0, _items.Count - 1));
            InvalidateVisual();
            SelectionChanged?.Invoke(_selectedIndex);
        }
    }

    public string SelectedId => _items.Count == 0 ? "" : _items[Math.Clamp(_selectedIndex, 0, _items.Count - 1)].Id;

    protected override Size MeasureOverride(Size available)
        => new(Width.Value, float.IsPositiveInfinity(available.Height) ? 600 : available.Height);

    protected override void OnPointerPress(PointerEventArgs args)
    {
        var index = IndexAt(args.Y);
        if (index >= 0)
        {
            SelectedIndex = index;
            args.Handled = true;
        }
    }

    protected override void OnPointerMove(PointerEventArgs args)
    {
        var index = IndexAt(args.Y);
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExit(PointerEventArgs args)
    {
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            InvalidateVisual();
        }
    }

    private int IndexAt(float y)
    {
        var top = LogoHeight + 14;
        for (var i = 0; i < _items.Count; i++)
        {
            if (y >= top + i * ItemHeight && y < top + (i + 1) * ItemHeight) return i;
        }
        return -1;
    }

    protected internal override void OnRender(SKCanvas canvas, Theme theme)
    {
        // Logo block: vector Android robot + wordmark.
        UiRenderer.FillRoundRect(canvas, 0, 0, FinalRect.Width, LogoHeight, 0, theme.SurfaceAlt);
        IconGlyph.Draw(canvas, "android", 22, 24, 30, theme.Accent);
        UiFonts.DrawText(canvas, "AndroLink", 58, 40, 19, theme.TextPrimary, UiFonts.Weight.Bold);
        UiFonts.DrawText(canvas, "Subsystem for Windows", 58, 58, 10, theme.TextSecondary);

        float y = LogoHeight + 14;
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var selected = i == _selectedIndex;
            var hovered = i == _hoverIndex;

            if (selected)
            {
                UiRenderer.FillRoundRect(canvas, 12, y, FinalRect.Width - 24, ItemHeight - 8, 6, theme.Selection);
                UiRenderer.FillRoundRect(canvas, 12, y + 8, 3, ItemHeight - 24, 1.5f, theme.Accent);
            }
            else if (hovered)
            {
                UiRenderer.FillRoundRect(canvas, 12, y, FinalRect.Width - 24, ItemHeight - 8, 6, theme.SurfaceHover);
            }

            var textColor = selected ? theme.TextPrimary : theme.TextSecondary;
            IconGlyph.Draw(canvas, item.Icon, 26, y + ItemHeight / 2f - 9, 18, selected ? theme.Accent : theme.TextSecondary);
            UiFonts.DrawTextCenteredY(canvas, item.Label, 58, y + ItemHeight / 2f, 13.5f, textColor,
                selected ? UiFonts.Weight.SemiBold : UiFonts.Weight.Regular);
            y += ItemHeight;
        }

        // Footer hint
        if (FinalRect.Height > y + 60)
        {
            UiFonts.DrawText(canvas, "userdata folder is user-editable", 24, FinalRect.Height - 28, 10.5f, theme.TextSecondary);
        }
    }
}
