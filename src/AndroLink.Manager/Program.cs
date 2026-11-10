using AndroLink.Core;
using AndroLink.UI;

namespace AndroLink.Manager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("program", e.ExceptionObject?.ToString() ?? "unknown");
            Log.Flush();
        };

        try
        {
            AppServices.Initialize();
        }
        catch (Exception ex)
        {
            Log.Error("program", $"startup failed: {ex}");
            Log.Flush();
            NativeFallback.MessageBox($"AndroLink failed to start:\r\n\r\n{ex.Message}", "AndroLink");
            return;
        }

        try
        {
            var window = MainWindow.Create();
            AppServices.MainWindow = window;
            AppServices.StartPolling();
            window.Show();
            AppServices.Platform.Run(window);
        }
        catch (Exception ex)
        {
            Log.Error("program", $"fatal: {ex}");
            Log.Flush();
            NativeFallback.MessageBox($"AndroLink crashed:\r\n\r\n{ex}", "AndroLink");
        }
        finally
        {
            Log.Flush();
        }
    }
}

/// <summary>Last-resort native message box used before/while the UI framework is unavailable.</summary>
internal static class NativeFallback
{
    public static void MessageBox(string text, string caption)
    {
        try
        {
            Interop.MessageBoxW(IntPtr.Zero, text, caption, 0x10);
        }
        catch
        {
            Console.Error.WriteLine($"{caption}: {text}");
        }
    }

    internal static class Interop
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
    }
}
