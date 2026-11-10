using System.Diagnostics;
using System.Runtime.InteropServices;
using AndroLink.UI;
using static AndroLink.Win32.NativeMethods;

namespace AndroLink.Win32;

/// <summary>
/// Production IPlatform over Win32: windows, message loop + animation timer,
/// Post() marshalling, file dialogs, clipboard, Explorer integration, URLs and
/// drag-drop. No WinForms/WPF involvement - pure P/Invoke, which keeps the
/// AndroLink.UI framework fully in charge of rendering.
/// </summary>
public sealed class Win32Platform : IPlatform
{
    private Win32WindowHost? _mainHost;
    private static uint _trayId = 1;
    private readonly Dictionary<Window, (Action<string[]> Handler, IntPtr Hwnd)> _dropHandlers = new();

    public Win32Platform()
    {
        try
        {
            SetProcessDpiAwarenessContext(new IntPtr(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2));
        }
        catch
        {
            // older Windows - fine without per-monitor DPI
        }
        Window.Platform = this;
    }

    // -------------------------------------------------------------- windows

    public IWindowHandle CreateWindow(Window window)
    {
        var host = Win32WindowHost.Create(window);
        _mainHost ??= host;
        host.StartAnimationTimer();
        return host;
    }

    public void Run(Window main)
    {
        ShowWindow(((Win32WindowHost)main.Handle!).WindowHandle, 1 /* SW_SHOWNORMAL */);

        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0))
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    public void Quit()
    {
        PostQuitMessage(0);
    }

    public void Post(Action action)
    {
        Win32WindowHost.Enqueue(action);
        var hwnd = _mainHost?.WindowHandle ?? IntPtr.Zero;
        if (hwnd != IntPtr.Zero)
        {
            PostMessageW(hwnd, WM_APP_DISPATCH, IntPtr.Zero, IntPtr.Zero);
        }
    }

    // -------------------------------------------------------------- dialogs

    public string? PickOpenFile(string title, string filter)
    {
        var buffer = new char[1024];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var ofn = new OPENFILENAMEW
            {
                lStructSize = Marshal.SizeOf<OPENFILENAMEW>(),
                hwndOwner = _mainHost?.WindowHandle ?? IntPtr.Zero,
                lpstrFilter = filter,
                lpstrFile = handle.AddrOfPinnedObject(),
                nMaxFile = buffer.Length,
                lpstrTitle = title,
                Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST,
            };
            if (!GetOpenFileNameW(ref ofn))
            {
                return null;
            }
            var text = new string(buffer);
            var zero = text.IndexOf('\0');
            return zero >= 0 ? text[..zero] : text;
        }
        finally
        {
            handle.Free();
        }
    }

    public string? PickFolder(string title)
    {
        // Minimal browse-for-folder (legacy but dependency-free).
        var pidl = SHBrowseForFolderW(title);
        if (pidl == IntPtr.Zero) return null;
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            return SHGetPathFromIDListW(pidl, sb) ? sb.ToString() : null;
        }
        finally
        {
            CoTaskMemFree(pidl);
        }
    }

    // ------------------------------------------------------------ clipboard

    public string GetClipboardText()
    {
        if (!OpenClipboard(_mainHost?.WindowHandle ?? IntPtr.Zero)) return "";
        try
        {
            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == IntPtr.Zero) return "";
            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero) return "";
            try
            {
                return Marshal.PtrToStringUni(pointer) ?? "";
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public void SetClipboardText(string text)
    {
        if (!OpenClipboard(_mainHost?.WindowHandle ?? IntPtr.Zero)) return;
        try
        {
            EmptyClipboard();
            var bytes = (text.Length + 1) * 2;
            var handle = GlobalAlloc(GMEM_MOVEABLE, (uint)bytes);
            var pointer = GlobalLock(handle);
            if (pointer != IntPtr.Zero)
            {
                Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
                Marshal.WriteInt16(pointer, text.Length * 2, 0);
                GlobalUnlock(handle);
                SetClipboardData(CF_UNICODETEXT, handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    // -------------------------------------------------------------- system

    public void RevealInFileManager(string path)
    {
        if (File.Exists(path))
        {
            var pidl = ILCreateFromPathW(path);
            if (pidl != IntPtr.Zero)
            {
                try { SHOpenFolderAndSelectItems(pidl, 0, null, 0); }
                finally { ILFree(pidl); }
                return;
            }
        }
        var target = File.Exists(path) ? path : Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(target))
        {
            ShellExecuteW(IntPtr.Zero, "open", "explorer.exe", File.Exists(target) ? $"/select,\"{target}\"" : $"\"{target}\"", null, 1);
        }
    }

    public void OpenUrl(string url)
    {
        ShellExecuteW(IntPtr.Zero, "open", url, null, null, 1);
    }

    /// <summary>Restores and focuses the main window (tray "Open" action).</summary>
    public void ShowMainWindow()
    {
        var hwnd = _mainHost?.WindowHandle ?? IntPtr.Zero;
        if (hwnd != IntPtr.Zero)
        {
            ShowWindow(hwnd, 9 /* SW_RESTORE */);
        }
    }

    public void EnableFileDrop(Window window, Action<string[]> handler)
    {
        if (window.Handle is Win32WindowHost host)
        {
            _dropHandlers[window] = (handler, host.WindowHandle);
            window.FileDropHandler = files =>
            {
                var apks = files.Where(f => f.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) ||
                                            f.EndsWith(".xapk", StringComparison.OrdinalIgnoreCase) ||
                                            f.EndsWith(".apkm", StringComparison.OrdinalIgnoreCase)).ToArray();
                handler(apks.Length > 0 ? apks : files);
            };
            DragAcceptFiles(host.WindowHandle, true);
        }
    }

    // ----------------------------------------------------------------- tray

    /// <summary>Extracts the application icon (resource #1) for the tray.</summary>
    public IntPtr ExtractAppIcon()
    {
        try
        {
            // IMAGE_ICON = 1, LR_DEFAULTSIZE = 0x40
            var icon = LoadImageW(GetModuleHandleW(null), "#1", 1, 32, 32, 0x40);
            if (icon != IntPtr.Zero) return icon;
        }
        catch { }
        return LoadIconW(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION
    }

    /// <summary>Adds a notification-area icon. Returns its id (pass to RemoveTrayIcon).</summary>
    public uint AddTrayIcon(string tooltip, IntPtr icon, Action activated, Action quitRequested)
    {
        var id = _trayId++;
        var hwnd = _mainHost?.WindowHandle ?? IntPtr.Zero;
        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = hwnd,
            uID = id,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_APP_TRAY,
            hIcon = icon,
            szTip = tooltip,
        };
        Shell_NotifyIconW(NIM_ADD, ref nid);
        _trayActions[id] = (activated, quitRequested);
        _trayHwnd = hwnd;
        return id;
    }

    public void RemoveTrayIcon(uint id)
    {
        var hwnd = _trayHwnd;
        if (hwnd == IntPtr.Zero) return;
        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = hwnd,
            uID = id,
        };
        Shell_NotifyIconW(NIM_DELETE, ref nid);
        _trayActions.Remove(id);
    }

    private readonly Dictionary<uint, (Action Activated, Action Quit)> _trayActions = new();
    private IntPtr _trayHwnd;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    /// <summary>Called by the host window when the tray callback message arrives.</summary>
    internal static void HandleTrayMessage(IntPtr lParam)
    {
        var platform = Window.Platform as Win32Platform;
        if (platform is null || platform._trayActions.Count == 0) return;
        var id = platform._trayActions.Keys.First();

        if (lParam == 0x0202) // WM_LBUTTONUP
        {
            platform._trayActions[id].Activated();
        }
        else if (lParam == 0x0205) // WM_RBUTTONUP
        {
            var menu = CreatePopupMenu();
            AppendMenuW(menu, 0, new IntPtr(1), "Open AndroLink");
            AppendMenuW(menu, 0, new IntPtr(2), "Quit");
            GetCursorPos(out var pt);
            var cmd = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, IntPtr.Zero, platform._trayHwnd, IntPtr.Zero);
            DestroyMenu(menu);
            if (cmd == 1) platform._trayActions[id].Activated();
            if (cmd == 2) platform._trayActions[id].Quit();
        }
    }

    // ---------------------------------------------------------- interop (local)

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolderW(string title);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHGetPathFromIDListW(IntPtr pidl, System.Text.StringBuilder pszPath);

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, uint dwBytes);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;
}
