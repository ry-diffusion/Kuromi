using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kuromi.Logging;
using Kuromi.Models;

namespace Kuromi.Services;

/// <summary>Loads and saves <see cref="KuromiConfig"/> under XDG config dir.</summary>
public class ConfigService
{
    private readonly ILog _log = Log.For<ConfigService>();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ConfigDir
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var baseDir = string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
                : xdg;
            return Path.Combine(baseDir, "kuromi");
        }
    }

    public static string CacheDir
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            var baseDir = string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache")
                : xdg;
            var dir = Path.Combine(baseDir, "kuromi");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public string ConfigPath => Path.Combine(ConfigDir, "config.json");
    public string RemindersPath => Path.Combine(ConfigDir, "reminders.json");

    public KuromiConfig Load()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            EnsureScreenshotHelper();
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<KuromiConfig>(json, JsonOpts);
                if (cfg is { Widgets.Count: > 0 } && cfg.Version >= KuromiConfig.CurrentVersion)
                {
                    bool changed = MigrateQuickActionGlyphs(cfg);
                    changed |= SeedScreenshotAction(cfg);
                    changed |= MigrateScreenshotCommand(cfg);
                    changed |= CompactMediaLayout(cfg);
                    if (changed) Save(cfg);
                    _log.Info($"config loaded ({cfg.Widgets.Count} widgets)");
                    return cfg;
                }
            }
        }
        catch (Exception ex) { _log.Warn("config load failed, falling back to default", ex); }

        _log.Info("creating default config");
        var def = KuromiConfig.CreateDefault();
        Save(def);
        return def;
    }

    /// <summary>
    /// Older configs stored Nerd Font glyphs for quick actions; we now use Lucide
    /// icon names. Replace any non-ascii glyph with a sensible Lucide name.
    /// </summary>
    private static bool MigrateQuickActionGlyphs(KuromiConfig cfg)
    {
        bool changed = false;
        foreach (var qa in cfg.QuickActions)
        {
            bool isLucide = !string.IsNullOrEmpty(qa.Glyph) &&
                            System.Text.RegularExpressions.Regex.IsMatch(qa.Glyph, "^[a-z0-9-]+$");
            if (isLucide) continue;

            var label = qa.Label.ToLowerInvariant();
            qa.Glyph =
                label.Contains("bloque") || label.Contains("lock") ? "lock" :
                label.Contains("config") || label.Contains("settings") ? "settings" :
                label.Contains("termin") ? "terminal" :
                label.Contains("suspend") || label.Contains("desliga") || label.Contains("power") ? "power" :
                label.Contains("rede") || label.Contains("wifi") ? "wifi" :
                "zap";
            changed = true;
        }
        return changed;
    }

    /// <summary>Add a screenshot quick action once (guarded so it isn't re-added).</summary>
    private static bool SeedScreenshotAction(KuromiConfig cfg)
    {
        if (cfg.ScreenshotActionSeeded) return false;
        cfg.ScreenshotActionSeeded = true;
        cfg.QuickActions.Add(new QuickAction
        {
            Label = "Captura", Glyph = "camera", Command = KuromiConfig.ScreenshotCommand, Accent = "#FF8AB0",
        });
        return true;
    }

    /// <summary>Replace the old spectacle-based screenshot seed with the GNOME portal command.</summary>
    private static bool MigrateScreenshotCommand(KuromiConfig cfg)
    {
        bool changed = false;
        foreach (var qa in cfg.QuickActions)
        {
            if (qa.Command == "spectacle")
            {
                qa.Command = KuromiConfig.ScreenshotCommand;
                changed = true;
            }
        }
        return changed;
    }

    public void Save(KuromiConfig cfg)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, JsonOpts));
        }
        catch (Exception ex) { _log.Warn("config save failed", ex); }
    }

    /// <summary>
    /// The media widget is now a single row. One-time: shrink it to height 1 and give
    /// the freed rows to the widget directly below it in the same column.
    /// </summary>
    private static bool CompactMediaLayout(KuromiConfig cfg)
    {
        if (cfg.MediaCompacted) return false;
        cfg.MediaCompacted = true;

        var media = cfg.Widgets.FirstOrDefault(w => w.Kind == WidgetKind.Media);
        if (media is null || media.RowSpan <= 1) return true;

        int freed = media.RowSpan - 1;
        int belowRow = media.Row + media.RowSpan;
        media.RowSpan = 1;

        var below = cfg.Widgets.FirstOrDefault(w => w.Col == media.Col && w.Row == belowRow);
        if (below != null)
        {
            below.Row -= freed;
            below.RowSpan += freed;
        }
        return true;
    }

    /// <summary>
    /// Writes the screenshot helper used by the "Captura" quick action: captures the
    /// screen via the XDG desktop portal (works on GNOME/Wayland) and copies the PNG
    /// straight to the clipboard with wl-copy, also saving a copy to ~/Pictures/Screenshots.
    /// </summary>
    private static void EnsureScreenshotHelper()
    {
        const string script = """
#!/usr/bin/env python3
import os, sys, subprocess, datetime, shutil
from urllib.parse import urlparse, unquote
import gi
gi.require_version('Gio', '2.0')
from gi.repository import Gio, GLib

bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
loop = GLib.MainLoop()
state = {'uri': None}

def on_response(conn, sender, path, iface, signal, params):
    code, results = params.unpack()
    if code == 0:
        state['uri'] = results.get('uri')
    loop.quit()

# Subscribe before calling so we never miss the Response.
bus.signal_subscribe('org.freedesktop.portal.Desktop',
                     'org.freedesktop.portal.Request', 'Response',
                     None, None, Gio.DBusSignalFlags.NONE, on_response)

bus.call_sync('org.freedesktop.portal.Desktop',
              '/org/freedesktop/portal/desktop',
              'org.freedesktop.portal.Screenshot', 'Screenshot',
              GLib.Variant('(sa{sv})', ('', {'interactive': GLib.Variant('b', False)})),
              GLib.VariantType('(o)'), Gio.DBusCallFlags.NONE, -1, None)

GLib.timeout_add_seconds(30, loop.quit)
loop.run()

uri = state['uri']
if not uri:
    sys.exit(1)

src = unquote(urlparse(uri).path)

# The portal captures the whole desktop (all monitors). Crop to the primary
# monitor so we don't get both screens. (xrandr marks the primary with '*'.)
import re
target = src
try:
    out = subprocess.run(['xrandr', '--listmonitors'], capture_output=True, text=True).stdout
    geo = None
    for line in out.splitlines():
        if '*' in line:
            m = re.search(r'(\d+)/\d+x(\d+)/\d+([+-]\d+)([+-]\d+)', line)
            if m:
                geo = m.groups()
            break
    if geo:
        w, h, x, y = geo
        cropped = src + '.primary.png'
        r = subprocess.run(['magick', src, '-crop', f'{w}x{h}{x}{y}', '+repage', cropped])
        if r.returncode == 0 and os.path.exists(cropped):
            target = cropped
except Exception:
    pass

with open(target, 'rb') as f:
    subprocess.run(['wl-copy', '--type', 'image/png'], stdin=f)
src = target

dest_dir = os.path.expanduser('~/Pictures/Screenshots')
try:
    os.makedirs(dest_dir, exist_ok=True)
    dest = os.path.join(dest_dir, 'Kuromi-' + datetime.datetime.now().strftime('%Y%m%d-%H%M%S') + '.png')
    shutil.copy(src, dest)
except Exception:
    pass

subprocess.run(['notify-send', '-a', 'Kuromi', 'Captura de tela',
                'Copiada para a area de transferencia'])
""";
        try
        {
            var path = Path.Combine(ConfigDir, "screenshot-clipboard.py");
            File.WriteAllText(path, script);
        }
        catch { /* ignore */ }
    }

    public T LoadJson<T>(string path, Func<T> fallback)
    {
        try
        {
            if (File.Exists(path))
            {
                var v = JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOpts);
                if (v != null) return v;
            }
        }
        catch { /* ignore */ }
        return fallback();
    }

    public void SaveJson<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOpts));
        }
        catch { /* ignore */ }
    }
}
