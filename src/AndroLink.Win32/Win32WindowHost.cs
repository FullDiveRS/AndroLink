using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using AndroLink.UI;
using SkiaSharp;
using static AndroLink.Win32.NativeMethods;

namespace AndroLink.Win32;

/// <summary>
/// Bridges one framework Window to one Win32 HWND: owns the DIB-backed
/// SkiaSharp render surface, translates window messages into framework input
/// events, and pumps layout/repaint requests.
/// </summary>
internal sealed class Win32WindowHost : IWindowHandle
{
    public const string WindowClassName = "AndroLinkUiWindow";
    internal const uint AnimTimerId = 1;

    /// <summary>When true, minimizing hides the window (tray click restores).</summary>
    public static bool MinimizeToTray;

    private static readonly Dictionary<IntPtr, Win32WindowHost> Hosts = new();
    private static WndProcDelegate? _wndProc; // keep delegate alive
    private static readonly ConcurrentQueue<Action> DispatchQueue = new();

    public IntPtr Hwnd { get; private set; }
    public Window Window { get; }

    private IntPtr _memoryDc;
    private IntPtr _dib;
    private IntPtr _dibBits;
    private int _pixelWidth;
    private int _pixelHeight;
    private SKSurface? _surface;
    private bool _mouseTracked;
    private bool _destroyed;

    public float ClientWidth => _pixelWidth / Math.Max(0.01f, Scale);
    public float ClientHeight => _pixelHeight / Math.Max(0.01f, Scale);
    public float Scale { get; private set; } = 1f;

    internal static void EnsureWindowClass()
    {
        if (_wndProc is not null) return;
        _wndProc = WndProc;

        var hInstance = GetModuleHandleW(null);
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            style = CS_HREDRAW | CS_VREDRAW | CS_DBLCLKS,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            hCursor = LoadCursorW(IntPtr.Zero, new IntPtr(32512)), // IDC_ARROW
            hbrBackground = IntPtr.Zero,
            lpszClassName = WindowClassName,
        };
        if (RegisterClassExW(ref wc) == 0)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 0 && error != 1410) // 1410 = class already exists
            {
                throw new InvalidOperationException($"RegisterClassExW failed ({error})");
            }
        }
    }

    public static Win32WindowHost Create(Window window)
    {
        EnsureWindowClass();
        var hInstance = GetModuleHandleW(null);

        // Desired client size -> window size
        var rect = new RECT { Left = 0, Top = 0, Right = (int)window.InitialClientSize.Width, Bottom = (int)window.InitialClientSize.Height };
        AdjustWindowRectEx(ref rect, WS_OVERLAPPEDWINDOW, false, 0);

        var hwnd = CreateWindowExW(0, WindowClassName, window.Title, WS_OVERLAPPEDWINDOW,
            CW_USEDEFAULT, CW_USEDEFAULT,
            rect.Right - rect.Left, rect.Bottom - rect.Top,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateWindowExW failed ({Marshal.GetLastWin32Error()})");
        }

        var host = new Win32WindowHost(window, hwnd);
        Hosts[hwnd] = host;
        window.AttachHandle(host);
        host.ApplyDpi();
        return host;
    }

    private Win32WindowHost(Window window, IntPtr hwnd)
    {
        Window = window;
        Hwnd = hwnd;
        window.FileDropHandler = null; // set via platform EnableFileDrop
        DragAcceptFiles(hwnd, true);
    }

    private void ApplyDpi()
    {
        var dpi = GetDpiForWindow(Hwnd);
        if (dpi == 0) dpi = 96;
        Scale = dpi / 96f;
        Window.DpiScale = Scale;
    }

    // ------------------------------------------------------------- rendering

    private void EnsureSurface(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        if (_surface is not null && _pixelWidth == width && _pixelHeight == height) return;

        DestroySurface();
        _pixelWidth = width;
        _pixelHeight = height;

        _memoryDc = CreateCompatibleDC(IntPtr.Zero);
        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
            },
        };
        _dib = CreateDIBSection(_memoryDc, ref bmi, DIB_RGB_COLORS, out _dibBits, IntPtr.Zero, 0);
        if (_dib == IntPtr.Zero || _dibBits == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateDIBSection failed");
        }
        SelectObject(_memoryDc, _dib);

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        _surface = SKSurface.Create(info, _dibBits, width * 4);
    }

    private void DestroySurface()
    {
        _surface?.Dispose();
        _surface = null;
        if (_dib != IntPtr.Zero) { DeleteObject(_dib); _dib = IntPtr.Zero; }
        if (_memoryDc != IntPtr.Zero) { DeleteDC(_memoryDc); _memoryDc = IntPtr.Zero; }
    }

    private void RenderIfDirty()
    {
        GetClientRect(Hwnd, out var rc);
        var width = Math.Max(1, rc.Right - rc.Left);
        var height = Math.Max(1, rc.Bottom - rc.Top);
        EnsureSurface(width, height);
        if (_surface is null) return;

        Window.ResizeFromPlatform(ClientWidth, ClientHeight);
        var canvas = _surface.Canvas;
        Window.RenderFrame(canvas, Scale);
        _surface.Canvas.Flush();
    }

    private void Blit()
    {
        var hdc = BeginPaint(Hwnd, out var ps);
        BitBlt(hdc, 0, 0, _pixelWidth, _pixelHeight, _memoryDc, 0, 0, SRCCOPY);
        EndPaint(Hwnd, ref ps);
    }

    public void Invalidate() => InvalidateRect(Hwnd, IntPtr.Zero, false);

    // ------------------------------------------------------------ wnd proc

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_APP_DISPATCH)
        {
            while (DispatchQueue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { AndroLink.Core.Log.Error("win32", $"dispatched action failed: {ex}"); }
            }
            return IntPtr.Zero;
        }

        if (!Hosts.TryGetValue(hWnd, out var host))
        {
            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        try
        {
            return host.HandleMessage(msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            AndroLink.Core.Log.Error("win32", $"unhandled exception in WndProc: {ex}");
            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }
    }

    private IntPtr HandleMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_PAINT:
                RenderIfDirty();
                Blit();
                return IntPtr.Zero;

            case WM_ERASEBKGND:
                return new IntPtr(1); // we always paint fully; avoid flicker

            case WM_SIZE:
            {
                if (wParam != new IntPtr(1)) // ignore SIZE_MINIMIZED
                {
                    GetClientRect(Hwnd, out var rc);
                    var w = Math.Max(1, rc.Right - rc.Left);
                    var h = Math.Max(1, rc.Bottom - rc.Top);
                    EnsureSurface(w, h);
                    Window.ResizeFromPlatform(ClientWidth, ClientHeight);
                    Invalidate();
                }
                return IntPtr.Zero;
            }

            case WM_DPICHANGED:
                ApplyDpi();
                Invalidate();
                return IntPtr.Zero;

            case WM_MOUSEMOVE:
            {
                var (x, y) = ClientPoint(lParam);
                TrackMouseLeave();
                Window.DispatchPointerMove(x / Scale, y / Scale);
                return IntPtr.Zero;
            }

            case WM_LBUTTONDOWN or WM_LBUTTONDBLCLK or WM_RBUTTONDOWN:
            {
                var (x, y) = ClientPoint(lParam);
                SetCapture(Hwnd);
                Window.DispatchPointerPress(x / Scale, y / Scale);
                Invalidate(); // hover/press visuals
                return IntPtr.Zero;
            }

            case WM_LBUTTONUP or WM_RBUTTONUP:
            {
                var (x, y) = ClientPoint(lParam);
                ReleaseCapture();
                Window.DispatchPointerRelease(x / Scale, y / Scale);
                Invalidate();
                return IntPtr.Zero;
            }

            case WM_MOUSEWHEEL:
            {
                var pt = new POINT { X = LoWord(lParam), Y = HiWord(lParam) };
                ScreenToClient(Hwnd, ref pt);
                var delta = (float)HiWord(wParam) / WHEEL_DELTA;
                Window.DispatchPointerWheel(pt.X / Scale, pt.Y / Scale, delta);
                return IntPtr.Zero;
            }

            case WM_MOUSELEAVE:
                _mouseTracked = false;
                Window.DispatchPointerMove(-1000, -1000); // force leave transitions
                return IntPtr.Zero;

            case WM_KEYDOWN or WM_SYSKEYDOWN:
            {
                var vk = (int)wParam & 0xFF;
                var key = MapKey(vk);
                var args = new KeyEventArgs
                {
                    Key = key,
                    Ctrl = (GetKeyState(VK_CONTROL) & 0x8000) != 0,
                    Shift = (GetKeyState(VK_SHIFT) & 0x8000) != 0,
                    Alt = msg == WM_SYSKEYDOWN || (GetKeyState(VK_MENU) & 0x8000) != 0,
                    Character = char.IsLetterOrDigit((char)vk) ? char.ToLowerInvariant((char)vk) : null,
                };
                Window.DispatchKey(args);
                return args.Handled ? IntPtr.Zero : DefWindowProcW(Hwnd, msg, wParam, lParam);
            }

            case WM_CHAR:
            {
                var ch = (char)(long)wParam;
                if (ch >= ' ' && ch != 127) // printable
                {
                    Window.DispatchText(new TextInputEventArgs { Text = ch.ToString() });
                }
                return IntPtr.Zero;
            }

            case WM_DROPFILES:
            {
                var files = QueryDroppedFiles(wParam);
                DragFinish(wParam);
                Window.DispatchFileDrop(files);
                return IntPtr.Zero;
            }

            case WM_COMMAND:
                return IntPtr.Zero;

            case WM_APP_TRAY:
                Win32Platform.HandleTrayMessage(lParam);
                return IntPtr.Zero;

            case WM_SYSCOMMAND:
            {
                var cmd = (long)wParam & 0xFFF0;
                if (cmd == 0xF020 /* SC_MINIMIZE */ && MinimizeToTray)
                {
                    ShowWindow(Hwnd, 0 /* SW_HIDE */);
                    return IntPtr.Zero;
                }
                return DefWindowProcW(Hwnd, msg, wParam, lParam);
            }

            case WM_TIMER:
                if ((uint)wParam == AnimTimerId)
                {
                    Animator.Tick();
                    if (Animator.HasActiveAnimations())
                    {
                        Invalidate();
                    }
                }
                return IntPtr.Zero;

            case WM_GETMINMAXINFO:
            {
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                mmi.ptMinTrackSize.X = 560;
                mmi.ptMinTrackSize.Y = 380;
                Marshal.StructureToPtr(mmi, lParam, false);
                return IntPtr.Zero;
            }

            case WM_CLOSE:
                DestroyWindow(Hwnd);
                return IntPtr.Zero;

            case WM_DESTROY:
                _destroyed = true;
                Hosts.Remove(Hwnd);
                DestroySurface();
                if (Hosts.Count == 0)
                {
                    PostQuitMessage(0);
                }
                return IntPtr.Zero;
        }

        return DefWindowProcW(Hwnd, msg, wParam, lParam);
    }

    private (int X, int Y) ClientPoint(IntPtr lParam) => (LoWord(lParam), HiWord(lParam));

    private void TrackMouseLeave()
    {
        if (_mouseTracked) return;
        _mouseTracked = true;
        var tme = new TRACKMOUSEEVENT
        {
            cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
            dwFlags = TME_LEAVE,
            hwndTrack = Hwnd,
        };
        TrackMouseEvent(ref tme);
    }

    private static string[] QueryDroppedFiles(IntPtr hDrop)
    {
        var count = DragQueryFileW(hDrop, uint.MaxValue, null, 0);
        var files = new List<string>((int)count);
        var sb = new System.Text.StringBuilder(1024);
        for (uint i = 0; i < count; i++)
        {
            var len = DragQueryFileW(hDrop, i, sb, (uint)sb.Capacity);
            if (len > 0) files.Add(sb.ToString(0, (int)len));
        }
        return files.ToArray();
    }

    private static VirtualKey MapKey(int vk) => vk switch
    {
        VK_BACK => VirtualKey.Back,
        VK_TAB => VirtualKey.Tab,
        VK_RETURN => VirtualKey.Enter,
        VK_ESCAPE => VirtualKey.Escape,
        VK_HOME => VirtualKey.Home,
        VK_END => VirtualKey.End,
        VK_LEFT => VirtualKey.Left,
        VK_UP => VirtualKey.Up,
        VK_RIGHT => VirtualKey.Right,
        VK_DOWN => VirtualKey.Down,
        VK_PRIOR => VirtualKey.PageUp,
        VK_NEXT => VirtualKey.PageDown,
        VK_DELETE => VirtualKey.Delete,
        _ => VirtualKey.None,
    };

    // --------------------------------------------------------- IWindowHandle

    public void SetTitle(string title) => SetWindowTextW(Hwnd, title);

    public void Close()
    {
        if (!_destroyed) PostMessageW(Hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    internal void StartAnimationTimer() => SetTimer(Hwnd, new IntPtr(AnimTimerId), 15, IntPtr.Zero);

    internal static void Enqueue(Action action) => DispatchQueue.Enqueue(action);

    internal bool HasHwnd(IntPtr hwnd) => Hwnd == hwnd;

    internal IntPtr WindowHandle => Hwnd;

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowTextW(IntPtr hWnd, string lpString);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr ReleaseCapture();
}
