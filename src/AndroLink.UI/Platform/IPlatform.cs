namespace AndroLink.UI;

/// <summary>
/// Platform abstraction implemented by host projects (AndroLink.Win32 ships
/// the production Windows implementation). The framework itself stays
/// platform-agnostic, which also keeps it testable headlessly.
/// </summary>
public interface IPlatform
{
    /// <summary>Creates the OS window backing the framework Window. Called by Window.Show().</summary>
    IWindowHandle CreateWindow(Window window);

    /// <summary>Blocks, running the message loop and animation pump until Quit.</summary>
    void Run(Window main);

    void Quit();

    /// <summary>Marshals an action onto the UI thread.</summary>
    void Post(Action action);

    string? PickOpenFile(string title, string filter);
    string? PickFolder(string title);

    string GetClipboardText();
    void SetClipboardText(string text);

    /// <summary>Opens Explorer with the item selected (folders/files).</summary>
    void RevealInFileManager(string path);

    void OpenUrl(string url);

    /// <summary>Subscribes OS file drag-drop for the window (used for .apk installs).</summary>
    void EnableFileDrop(Window window, Action<string[]> handler);
}

public interface IWindowHandle
{
    void SetTitle(string title);
    void Close();
    void Invalidate();
    float ClientWidth { get; }
    float ClientHeight { get; }
}

/// <summary>Entry-point helper: builds the window, shows it, runs the loop.</summary>
public static class UIApplication
{
    public static IPlatform Platform => Window.Platform!;

    public static void Run(Window window)
    {
        if (Window.Platform is null)
        {
            throw new InvalidOperationException("Window.Platform must be set before UIApplication.Run (the Win32 host sets it in its static initializer).");
        }
        window.Show();
        Window.Platform.Run(window);
    }

    public static void Post(Action action) => Window.Platform?.Post(action);
}
