namespace AndroLink.UI;

/// <summary>Non-character keys we care about; characters arrive via TextInput.</summary>
public enum VirtualKey
{
    None = 0,
    Back, Delete, Enter, Escape, Tab, Space,
    Left, Right, Up, Down, Home, End,
    PageUp, PageDown,
}

public class PointerEventArgs
{
    /// <summary>Pointer position in the receiving element's local coordinates.</summary>
    public float X { get; internal set; }
    public float Y { get; internal set; }
    /// <summary>Pointer position in window-client coordinates (DIPs).</summary>
    public float WindowX { get; internal set; }
    public float WindowY { get; internal set; }
    public bool Handled { get; set; }
    public UIElement Source { get; internal set; } = null!;
    /// <summary>True when the pointer is within the receiving element's bounds.</summary>
    public bool IsOverTarget { get; internal set; }
}

public class WheelEventArgs : PointerEventArgs
{
    /// <summary>Positive = scroll up/away from user.</summary>
    public float Delta { get; internal set; }
}

public class KeyEventArgs
{
    public VirtualKey Key { get; internal set; }
    /// <summary>Character form of the key (letters/digits), when applicable - used for Ctrl combos.</summary>
    public char? Character { get; internal set; }
    public bool Ctrl { get; internal set; }
    public bool Shift { get; internal set; }
    public bool Alt { get; internal set; }
    public bool Handled { get; set; }
}

public class TextInputEventArgs
{
    public string Text { get; internal set; } = "";
    public bool Handled { get; set; }
}

public static class FocusManagerExtensions
{
    /// <summary>Moves keyboard focus to the element (must be Focusable and attached to a window).</summary>
    public static void Focus(this UIElement element) => element.FindWindow()?.FocusElement(element);

    public static Window? FindWindow(this UIElement element)
    {
        var current = element;
        while (current is not null)
        {
            if (current is Window w) return w;
            current = current.Parent;
        }
        return null;
    }

    /// <summary>Absolute (window-client) position of the element's top-left corner.</summary>
    public static (float X, float Y) AbsolutePosition(this UIElement element)
    {
        float x = 0, y = 0;
        var current = element;
        while (current is not null)
        {
            x += current.FinalRect.X;
            y += current.FinalRect.Y;
            current = current.Parent;
        }
        return (x, y);
    }

    public static Rect AbsoluteBounds(this UIElement element)
    {
        var (x, y) = element.AbsolutePosition();
        return new Rect(x, y, element.FinalRect.Width, element.FinalRect.Height);
    }
}
