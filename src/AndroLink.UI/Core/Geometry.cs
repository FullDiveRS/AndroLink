namespace AndroLink.UI;

// ---------------------------------------------------------------------------
// Primitive geometry + layout types used across AndroLink.UI.
// ---------------------------------------------------------------------------

public readonly struct Size
{
    public static readonly Size Empty = new(0, 0);
    public Size(float width, float height) { Width = width; Height = height; }
    public float Width { get; }
    public float Height { get; }
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public Size With(float? width = null, float? height = null) => new(width ?? Width, height ?? Height);
    public override string ToString() => $"{Width:0.#}x{Height:0.#}";
}

public readonly struct Rect
{
    public static readonly Rect Empty = new(0, 0, 0, 0);
    public Rect(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public bool Contains(float px, float py) => px >= X && px < Right && py >= Y && py < Bottom;
    public Rect Offset(float dx, float dy) => new(X + dx, Y + dy, Width, Height);
    public Rect Deflate(Thickness t) => new(X + t.Left, Y + t.Top, Math.Max(0, Width - t.Left - t.Right), Math.Max(0, Height - t.Top - t.Bottom));
    public override string ToString() => $"({X:0.#},{Y:0.#}) {Width:0.#}x{Height:0.#}";
}

public readonly struct Thickness
{
    public static readonly Thickness Zero = default;
    public Thickness(float uniform) : this(uniform, uniform, uniform, uniform) { }
    public Thickness(float horizontal, float vertical) : this(horizontal, vertical, horizontal, vertical) { }
    public Thickness(float left, float top, float right, float bottom)
    {
        Left = left; Top = top; Right = right; Bottom = bottom;
    }
    public float Left { get; }
    public float Top { get; }
    public float Right { get; }
    public float Bottom { get; }
    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;
    public static Thickness From(float? l, float? t, float? r, float? b)
        => new(l ?? 0, t ?? 0, r ?? 0, b ?? 0);
}

public readonly struct CornerRadius
{
    public CornerRadius(float uniform) => Uniform = uniform;
    public float Uniform { get; }
    public static readonly CornerRadius None = new(0);
}

public enum Orientation { Horizontal, Vertical }

public enum Dock { Left, Top, Right, Bottom }

public enum HAlignment { Stretch, Left, Center, Right }
public enum VAlignment { Stretch, Top, Center, Bottom }

/// <summary>Auto (size to content), Pixel, or Star (proportional inside a Grid).</summary>
public readonly struct LayoutLength
{
    public enum UnitType { Auto, Pixel, Star }

    public LayoutLength(float value, UnitType type) { Value = value; Type = type; }

    public float Value { get; }
    public UnitType Type { get; }

    public static LayoutLength Auto => default;
    public static LayoutLength Pixels(float v) => new(Math.Max(0, v), UnitType.Pixel);
    public static LayoutLength Star(float v = 1) => new(Math.Max(0.01f, v), UnitType.Star);

    public static implicit operator LayoutLength(float pixels) => Pixels(pixels);

    public override string ToString() => Type switch
    {
        UnitType.Auto => "Auto",
        UnitType.Star => $"{Value}*",
        _ => $"{Value}px",
    };
}
