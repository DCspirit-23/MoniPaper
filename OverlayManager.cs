using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PaperCare;

internal sealed class OverlayManager : IDisposable
{
    private readonly List<Overlay> _overlays = new();
    private string? _screenSignature;

    public bool IsShowing => _overlays.Count > 0;

    public bool Apply(Settings settings, bool paused, out string? error)
    {
        error = null;
        try
        {
            if (!settings.Enabled || paused)
            {
                Clear();
                return true;
            }

            var tile = TextureRenderer.Tile(settings);
            if (TextureRenderer.IsFullyTransparent(tile))
            {
                Clear();
                return true;
            }

            var screens = GetTargetScreens(settings.AllScreens);
            if (screens.Length == 0)
                throw new InvalidOperationException("没有可用显示器。");

            var signature = BuildSignature(screens);
            if (_screenSignature != signature || _overlays.Count != screens.Length)
            {
                Clear();
                foreach (var screen in screens)
                    _overlays.Add(new Overlay(screen.Bounds));
                _screenSignature = signature;
            }

            foreach (var overlay in _overlays)
                overlay.Render(tile);
            return true;
        }
        catch (Exception)
        {
            Clear();
            error = "桌面覆盖暂时无法显示，请检查显示器配置后重试。";
            return false;
        }
    }

    public void RaiseAll()
    {
        foreach (var overlay in _overlays.ToArray())
        {
            try { overlay.Raise(); }
            catch (ObjectDisposedException) { }
        }
    }

    public void Clear()
    {
        foreach (var overlay in _overlays)
        {
            try
            {
                overlay.Hide();
                overlay.Dispose();
            }
            catch (ObjectDisposedException) { }
        }
        _overlays.Clear();
        _screenSignature = null;
    }

    public void Dispose() => Clear();

    private static Screen[] GetTargetScreens(bool allScreens)
    {
        if (allScreens)
            return Screen.AllScreens.OrderBy(s => s.DeviceName, StringComparer.OrdinalIgnoreCase).ToArray();

        var primary = Screen.PrimaryScreen;
        return primary is null ? Array.Empty<Screen>() : new[] { primary };
    }

    private static string BuildSignature(IEnumerable<Screen> screens)
    {
        var value = new StringBuilder();
        foreach (var screen in screens)
        {
            var bounds = screen.Bounds;
            value.Append(screen.DeviceName).Append(':')
                .Append(bounds.X).Append(',').Append(bounds.Y).Append(',')
                .Append(bounds.Width).Append('x').Append(bounds.Height).Append(';');
        }
        return value.ToString();
    }
}
