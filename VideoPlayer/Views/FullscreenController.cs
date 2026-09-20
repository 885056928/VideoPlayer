using System.Windows;

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
    private FullscreenState? _saved;

    public FullscreenController(Window window)
    {
        _window = window;
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

        _saved = FullscreenState.From(
            _window.WindowStyle,
            _window.ResizeMode,
            _window.WindowState,
            new Rect(_window.Left, _window.Top, _window.Width, _window.Height));
        _window.WindowStyle = WindowStyle.None;
        _window.ResizeMode = ResizeMode.NoResize;
        _window.WindowState = WindowState.Maximized;
    }

    public void Exit()
    {
        if (_saved is not { } state)
        {
            return;
        }

        _window.WindowStyle = state.Style;
        _window.ResizeMode = state.ResizeMode;
        _window.WindowState = state.WindowState;
        _window.Left = state.Bounds.Left;
        _window.Top = state.Bounds.Top;
        _window.Width = state.Bounds.Width;
        _window.Height = state.Bounds.Height;
        _saved = null;
    }
}
