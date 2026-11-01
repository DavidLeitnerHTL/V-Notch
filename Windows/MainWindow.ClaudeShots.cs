using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VNotch.Models;
using VNotch.Services;
using VNotch.Services.ClaudeCode;

namespace VNotch;

public partial class MainWindow
{
    #region Screenshot to Claude

    private static readonly TimeSpan SnipTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan SnipPoll = TimeSpan.FromMilliseconds(300);
    private DispatcherTimer? _snipWatch;
    private uint _snipSequence;
    private DateTime _snipUntilUtc;
    private string? _snipSession;

    /// <summary>
    /// A session card's camera button: the notch steps aside, Windows' snip overlay opens, and the picture,
    /// once it lands on the clipboard, comes back attached to a message for that session.
    /// </summary>
    private void OnClaudeScreenshotRequested(string sessionId)
    {
        _snipSession = sessionId;
        _snipSequence = ClaudeAttachments.ClipboardSequence;
        _snipUntilUtc = DateTime.UtcNow + SnipTimeout;
        // The overlay covers the screen; an open notch would end up in the picture.
        if (_isExpanded) CollapseNotch();
        try
        {
            Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
        }
        catch (Win32Exception ex)
        {
            RuntimeLog.Warn("CLAUDE", $"Snip overlay did not open: {ex.Message}");
            _snipSession = null;
            return;
        }
        if (_snipWatch == null)
        {
            _snipWatch = new DispatcherTimer(DispatcherPriority.Background) { Interval = SnipPoll };
            _snipWatch.Tick += (_, _) => CheckSnip();
        }
        _snipWatch.Start();
    }

    private void CheckSnip()
    {
        if (_cleanedUp || _snipSession is not { } session || DateTime.UtcNow > _snipUntilUtc)
        {
            // Cancelled with Esc, or nothing came: give up quietly.
            _snipWatch?.Stop();
            _snipSession = null;
            return;
        }
        uint sequence = ClaudeAttachments.ClipboardSequence;
        if (sequence == _snipSequence) return;

        BitmapSource? image;
        try
        {
            image = Clipboard.ContainsImage() ? Clipboard.GetImage() : null;
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            // The snip tool is still writing the clipboard: next tick.
            return;
        }
        if (image == null)
        {
            _snipSequence = sequence;
            return;
        }

        _snipWatch?.Stop();
        _snipSession = null;
        ClaudeCenter.AttachScreenshot(session, ClaudeAttachments.FromImage(image));
        _transitionCoordinator.RequestView(NotchView.Claude, "ClaudeScreenshot");
    }

    private void DisposeClaudeShots()
    {
        _snipWatch?.Stop();
        _snipSession = null;
    }

    #endregion
}
