using System.Windows;
using System.Windows.Shell;

namespace VideoPlayer.Views;

public sealed record FullscreenState(
    WindowStyle Style,
    ResizeMode ResizeMode,
    WindowState WindowState,
    Rect Bounds)
{
    public static FullscreenState From(
        WindowStyle style,
        ResizeMode resizeMode,
        WindowState windowState,
        Rect bounds)
        => new(style, resizeMode, windowState, bounds);
}

public sealed class FullscreenController
{
    private readonly Window _window;
    private readonly Action<bool>? _fullscreenChanged;
    private FullscreenState? _saved;
    private WindowChrome? _savedChrome;

    public FullscreenController(Window window, Action<bool>? fullscreenChanged = null)
    {
        _window = window;
        _fullscreenChanged = fullscreenChanged;
    }

    public bool IsFullscreen => _saved is not null;

    public void Toggle()
    {
        if (IsFullscreen)
        {
            Exit();
        }
        else
        {
            Enter();
        }
    }

    public void Enter()
    {
        if (IsFullscreen)
        {
            return;
        }

        var bounds = _window.WindowState == WindowState.Normal || _window.RestoreBounds.IsEmpty
            ? new Rect(_window.Left, _window.Top, _window.Width, _window.Height)
            : _window.RestoreBounds;
        _saved = FullscreenState.From(
            _window.WindowStyle,
            _window.ResizeMode,
            _window.WindowState,
            bounds);
        _savedChrome = WindowChrome.GetWindowChrome(_window);
        // 全屏移除标题命中区和工作区裁剪；先还原可让已最大化窗口重新计算全屏边界。
        _window.WindowState = WindowState.Normal;
        WindowChrome.SetWindowChrome(_window, null);
        _window.WindowStyle = WindowStyle.None;
        _window.ResizeMode = ResizeMode.NoResize;
        _window.WindowState = WindowState.Maximized;
        _fullscreenChanged?.Invoke(true);
    }

    public void Exit()
    {
        if (_saved is not { } state)
        {
            return;
        }

        // 先恢复普通窗口参数，再恢复原状态，保留最大化前的位置和尺寸。
        _window.WindowState = WindowState.Normal;
        _window.WindowStyle = state.Style;
        _window.ResizeMode = state.ResizeMode;
        WindowChrome.SetWindowChrome(_window, _savedChrome);
        _window.Left = state.Bounds.Left;
        _window.Top = state.Bounds.Top;
        _window.Width = state.Bounds.Width;
        _window.Height = state.Bounds.Height;
        _window.WindowState = state.WindowState;
        _savedChrome = null;
        _saved = null;
        _fullscreenChanged?.Invoke(false);
    }
}
