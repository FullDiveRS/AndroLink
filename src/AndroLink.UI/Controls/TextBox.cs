using SkiaSharp;

namespace AndroLink.UI;

/// <summary>
/// Single-line text input: caret, selection (shift+arrows / drag), clipboard
/// (Ctrl+C/X/V), Home/End navigation, placeholder text and a blinking caret.
/// </summary>
public class TextBox : UIElement
{
    private string _text = "";
    private int _caret;
    private int _anchor;
    private float _scrollX;
    private bool _caretVisible;
    private bool _dragging;

    public const float ControlHeight = 34f;

    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (_text == value) return;
            _text = value;
            _caret = Math.Clamp(_caret, 0, _text.Length);
            _anchor = _caret;
            _scrollX = 0;
            InvalidateMeasure();
            TextChanged?.Invoke();
        }
    }

    public string? Placeholder { get; set; }
    public bool ReadOnly { get; set; }

    public event Action? TextChanged;

    /// <summary>Text editing owns every click inside its bounds.</summary>
    protected internal override bool IsHitTestLeaf => true;

    public TextBox(string? placeholder = null)
    {
        Placeholder = placeholder;
        Focusable = true;
        ClipToBounds = true;
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(10, 0);
        HAlign = HAlignment.Stretch;
    }

    // -------------------------------------------------------------- editing

    private (int Start, int End) SelectionRange
        => _anchor <= _caret ? (_anchor, _caret) : (_caret, _anchor);

    public void SelectAll()
    {
        _anchor = 0;
        _caret = _text.Length;
        InvalidateVisual();
    }

    private void DeleteSelection()
    {
        var (s, e) = SelectionRange;
        if (s == e) return;
        _text = _text[..s] + _text[e..];
        _caret = _anchor = s;
        InvalidateMeasure();
        TextChanged?.Invoke();
    }

    private void InsertText(string insert)
    {
        if (ReadOnly) return;
        DeleteSelection();
        var (s, _) = SelectionRange;
        _text = _text[..s] + insert + _text[s..];
        _caret = _anchor = s + insert.Length;
        InvalidateMeasure();
        TextChanged?.Invoke();
    }

    private void SetCaret(int index, bool extend)
    {
        _caret = Math.Clamp(index, 0, _text.Length);
        if (!extend) _anchor = _caret;
        EnsureCaretVisible();
        InvalidateVisual();
        RestartBlink();
    }

    // ----------------------------------------------------------- clipboard

    private void CopySelection(bool cut)
    {
        var (s, e) = SelectionRange;
        if (s == e) return;
        var platform = Window.Platform;
        if (platform is null) return;
        platform.SetClipboardText(_text[s..e]);
        if (cut && !ReadOnly) DeleteSelection();
    }

    private void Paste()
    {
        var platform = Window.Platform;
        if (platform is null) return;
        var text = platform.GetClipboardText();
        if (!string.IsNullOrEmpty(text)) InsertText(text.Replace("\r", "").Replace("\n", " "));
    }

    // --------------------------------------------------------------- input

    protected override void OnGotFocus()
    {
        RestartBlink();
        InvalidateVisual();
    }

    protected override void OnLostFocus()
    {
        _caretVisible = false;
        InvalidateVisual();
    }

    private void RestartBlink()
    {
        _caretVisible = true;
        InvalidateVisual();
        var owner = this;
        Animator.Animate(0, 1, TimeSpan.FromMilliseconds(1100), v =>
        {
            var visible = v < 0.5;
            if (visible != _caretVisible)
            {
                _caretVisible = visible;
                owner.InvalidateVisual();
            }
        }, Easing.Linear, completed: () =>
        {
            if (IsFocused && Visible) RestartBlink();
        }, owner: owner);
    }

    protected override void OnTextInput(TextInputEventArgs args)
    {
        if (ReadOnly || string.IsNullOrEmpty(args.Text)) return;
        InsertText(args.Text);
        args.Handled = true;
    }

    protected override void OnKey(KeyEventArgs args)
    {
        var extend = args.Shift;
        switch (args.Key)
        {
            case VirtualKey.Left:
                SetCaret(_caret - 1, extend);
                args.Handled = true;
                break;
            case VirtualKey.Right:
                SetCaret(_caret + 1, extend);
                args.Handled = true;
                break;
            case VirtualKey.Home:
                SetCaret(0, extend);
                args.Handled = true;
                break;
            case VirtualKey.End:
                SetCaret(_text.Length, extend);
                args.Handled = true;
                break;
            case VirtualKey.Back:
                if (SelectionRange.Start != SelectionRange.End)
                {
                    DeleteSelection();
                }
                else if (_caret > 0)
                {
                    _text = _text[..(_caret - 1)] + _text[_caret..];
                    _caret = _anchor = _caret - 1;
                    InvalidateMeasure();
                    TextChanged?.Invoke();
                }
                EnsureCaretVisible();
                InvalidateVisual();
                args.Handled = true;
                break;
            case VirtualKey.Delete:
                if (SelectionRange.Start != SelectionRange.End)
                {
                    DeleteSelection();
                }
                else if (_caret < _text.Length)
                {
                    _text = _text[.._caret] + _text[(_caret + 1)..];
                    InvalidateMeasure();
                    TextChanged?.Invoke();
                }
                InvalidateVisual();
                args.Handled = true;
                break;
        }

        if (!args.Handled && args.Ctrl && args.Character is { } ch)
        {
            switch (char.ToUpperInvariant(ch))
            {
                case 'C':
                    CopySelection(cut: false);
                    args.Handled = true;
                    break;
                case 'X':
                    CopySelection(cut: true);
                    args.Handled = true;
                    break;
                case 'V':
                    Paste();
                    args.Handled = true;
                    break;
                case 'A':
                    SelectAll();
                    args.Handled = true;
                    break;
            }
        }
    }

    protected override void OnPointerPress(PointerEventArgs args)
    {
        this.Focus();
        _dragging = true;
        var index = HitTestCaret(args.X);
        SetCaret(index, extend: false);
        args.Handled = true;
    }

    protected override void OnPointerMove(PointerEventArgs args)
    {
        if (!_dragging) return;
        var index = HitTestCaret(args.X);
        SetCaret(index, extend: true);
        args.Handled = true;
    }

    protected override void OnPointerRelease(PointerEventArgs args)
    {
        _dragging = false;
        args.Handled = true;
    }

    // ------------------------------------------------------------- layout

    protected override Size MeasureOverride(Size available)
    {
        return new Size(
            float.IsPositiveInfinity(available.Width) ? 140 : available.Width,
            ControlHeight);
    }

    private float TextAreaWidth => FinalRect.Width - Padding.Horizontal;

    private int HitTestCaret(float localX)
    {
        var textX = localX - Padding.Left + _scrollX;
        for (var i = 0; i <= _text.Length; i++)
        {
            var w = UiFonts.MeasureWidth(_text[..i], Theme.Current.BaseFontSize);
            if (w >= textX) return i;
        }
        return _text.Length;
    }

    private void EnsureCaretVisible()
    {
        var before = UiFonts.MeasureWidth(_text[..Math.Min(_caret, _text.Length)], Theme.Current.BaseFontSize);
        if (before - _scrollX > TextAreaWidth - 6)
        {
            _scrollX = before - TextAreaWidth + 6;
        }
        if (before - _scrollX < 0)
        {
            _scrollX = Math.Max(0, before - 6);
        }
        var total = UiFonts.MeasureWidth(_text, Theme.Current.BaseFontSize);
        _scrollX = Math.Clamp(_scrollX, 0, Math.Max(0, total - TextAreaWidth));
    }

    // ------------------------------------------------------------ rendering

    protected internal override void OnRender(SKCanvas canvas, Theme theme)
    {
        var fill = theme.SurfaceAlt;
        UiRenderer.FillRoundRect(canvas, 0, 0, FinalRect.Width, FinalRect.Height, CornerRadius.Uniform, fill);
        UiRenderer.StrokeRoundRect(canvas, 0, 0, FinalRect.Width, FinalRect.Height, CornerRadius.Uniform,
            IsFocused ? theme.Accent : theme.Border, IsFocused ? 1.6f : 1f);

        canvas.Save();
        canvas.ClipRect(new SKRect(Padding.Left - 2, 0, FinalRect.Width - Padding.Right + 2, FinalRect.Height));

        var fontSize = theme.BaseFontSize;
        // Text ink is centered where the caret and selection bar already are.
        using var textFont = UiFonts.CreateFont(fontSize);
        var textY = UiFonts.BaselineForCentered(textFont, FinalRect.Height / 2f);

        if (_text.Length == 0 && !IsFocused && !string.IsNullOrEmpty(Placeholder))
        {
            UiFonts.DrawText(canvas, Placeholder!, Padding.Left - _scrollX, textY, fontSize, theme.TextSecondary);
            canvas.Restore();
            return;
        }

        var (selStart, selEnd) = SelectionRange;
        if (selStart != selEnd)
        {
            var x1 = UiFonts.MeasureWidth(_text[..selStart], fontSize) - _scrollX;
            var x2 = UiFonts.MeasureWidth(_text[..selEnd], fontSize) - _scrollX;
            UiRenderer.FillRoundRect(canvas, Padding.Left + x1, (FinalRect.Height - 20) / 2, x2 - x1, 20, 3, theme.Selection);
        }

        UiFonts.DrawText(canvas, _text, Padding.Left - _scrollX, textY, fontSize, theme.TextPrimary);

        if (IsFocused && _caretVisible)
        {
            var cx = Padding.Left + UiFonts.MeasureWidth(_text[..Math.Min(_caret, _text.Length)], fontSize) - _scrollX;
            UiRenderer.DrawLine(canvas, cx, (FinalRect.Height - 18) / 2, cx, (FinalRect.Height + 18) / 2, theme.Accent, 1.6f);
        }

        canvas.Restore();
    }
}
