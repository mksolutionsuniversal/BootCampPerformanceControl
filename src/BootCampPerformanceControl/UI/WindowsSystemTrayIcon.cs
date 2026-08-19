using DrawingIcon = System.Drawing.Icon;
using SystemIcons = System.Drawing.SystemIcons;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;
using ToolStripSeparator = System.Windows.Forms.ToolStripSeparator;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;

namespace BootCampPerformanceControl.UI;

internal sealed class WindowsSystemTrayIcon : IDisposable
{
    private const string ApplicationName = "BootCamp Performance Control";

    private readonly DrawingIcon _icon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _openMenuItem;
    private readonly ToolStripMenuItem _exitMenuItem;
    private readonly NotifyIcon _notifyIcon;

    private bool _hasShownMinimizedNotification;
    private bool _isDisposed;

    public WindowsSystemTrayIcon()
    {
        _icon = LoadApplicationIcon();
        _openMenuItem = new ToolStripMenuItem("Open BootCamp Performance Control");
        _exitMenuItem = new ToolStripMenuItem("Exit");
        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add(_openMenuItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(_exitMenuItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = ApplicationName,
            ContextMenuStrip = _contextMenu,
            Visible = false
        };

        _openMenuItem.Click += OnOpenRequested;
        _exitMenuItem.Click += OnExitRequested;
        _notifyIcon.DoubleClick += OnOpenRequested;
        _notifyIcon.BalloonTipClicked += OnOpenRequested;
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _notifyIcon.Visible = true;

        if (_hasShownMinimizedNotification)
        {
            return;
        }

        _hasShownMinimizedNotification = true;
        _notifyIcon.ShowBalloonTip(
            timeout: 2500,
            tipTitle: ApplicationName,
            tipText: "The application is still running in the system tray.",
            tipIcon: ToolTipIcon.Info);
    }

    public void Hide()
    {
        if (_isDisposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _notifyIcon.Visible = false;
        _openMenuItem.Click -= OnOpenRequested;
        _exitMenuItem.Click -= OnExitRequested;
        _notifyIcon.DoubleClick -= OnOpenRequested;
        _notifyIcon.BalloonTipClicked -= OnOpenRequested;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _icon.Dispose();
    }

    private static DrawingIcon LoadApplicationIcon()
    {
        try
        {
            var executablePath = Environment.ProcessPath;

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                using var executableIcon = DrawingIcon.ExtractAssociatedIcon(executablePath);

                if (executableIcon is not null)
                {
                    return (DrawingIcon)executableIcon.Clone();
                }
            }
        }
        catch
        {
            // A built-in icon keeps the tray control usable if icon extraction fails.
        }

        return (DrawingIcon)SystemIcons.Application.Clone();
    }

    private void OnOpenRequested(object? sender, EventArgs e)
    {
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }
}
