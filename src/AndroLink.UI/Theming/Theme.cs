using SkiaSharp;

namespace AndroLink.UI;

/// <summary>
/// Theme palettes. Controls read every color from <see cref="Theme.Current"/>
/// so switching themes at runtime restyles the whole app.
/// </summary>
public sealed class Theme
{
    public static Theme Current { get; set; } = Dark();

    public string Name { get; init; } = "dark";

    public SKColor WindowBackground { get; init; }
    public SKColor Surface { get; init; }
    public SKColor SurfaceAlt { get; init; }
    public SKColor SurfaceHover { get; init; }
    public SKColor Border { get; init; }
    public SKColor TextPrimary { get; init; }
    public SKColor TextSecondary { get; init; }
    public SKColor Accent { get; init; }
    public SKColor AccentHover { get; init; }
    public SKColor AccentPressed { get; init; }
    public SKColor AccentText { get; init; }
    public SKColor Selection { get; init; }
    public SKColor Danger { get; init; }
    public SKColor Success { get; init; }
    public SKColor Warning { get; init; }
    public SKColor Scrim { get; init; }
    public SKColor ScrollbarThumb { get; init; }

    public float CornerRadius { get; init; } = 6f;
    public float BaseFontSize { get; init; } = 13.5f;
    public string FontFamily { get; init; } = UiFonts.DefaultFamily;

    public static Theme Dark() => new()
    {
        Name = "dark",
        WindowBackground = FromHex("#12151c"),
        Surface = FromHex("#1a1f2a"),
        SurfaceAlt = FromHex("#212836"),
        SurfaceHover = FromHex("#2a3342"),
        Border = FromHex("#323c4d"),
        TextPrimary = FromHex("#e8edf4"),
        TextSecondary = FromHex("#93a0b4"),
        Accent = FromHex("#3cc878"),
        AccentHover = FromHex("#4fdc8c"),
        AccentPressed = FromHex("#2ea562"),
        AccentText = FromHex("#08110b"),
        Selection = FromHex("#2c4a3a"),
        Danger = FromHex("#e2564e"),
        Success = FromHex("#3cc878"),
        Warning = FromHex("#e8b64c"),
        Scrim = new SKColor(6, 8, 12, 160),
        ScrollbarThumb = FromHex("#39445a"),
    };

    public static Theme Light() => new()
    {
        Name = "light",
        WindowBackground = FromHex("#f3f5f8"),
        Surface = FromHex("#ffffff"),
        SurfaceAlt = FromHex("#eef1f5"),
        SurfaceHover = FromHex("#e2e8f0"),
        Border = FromHex("#d0d7e0"),
        TextPrimary = FromHex("#1c2430"),
        TextSecondary = FromHex("#5b6878"),
        Accent = FromHex("#1f9d5b"),
        AccentHover = FromHex("#23b468"),
        AccentPressed = FromHex("#177f47"),
        AccentText = FromHex("#ffffff"),
        Selection = FromHex("#cfe8da"),
        Danger = FromHex("#cc3a32"),
        Success = FromHex("#1f9d5b"),
        Warning = FromHex("#b8842a"),
        Scrim = new SKColor(20, 24, 32, 130),
        ScrollbarThumb = FromHex("#b8c2ce"),
    };

    public static SKColor FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6) hex = "FF" + hex;
        return (SKColor)(uint)Convert.ToUInt32(hex, 16);
    }
}
