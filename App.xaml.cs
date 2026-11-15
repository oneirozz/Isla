using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Isla.Diagnostics;
using Isla.Interop;
using Forms = System.Windows.Forms;

namespace Isla;

public partial class App : Application
{
    private const int HotkeyToggle = 1;
    private const int HotkeyReminder = 2;
    private const int HotkeyHide = 3;
    private const int HotkeyPlayPause = 4;

    private Mutex? _instance;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private Forms.NotifyIcon? _tray;
    private IslaWindow? _window;
    private HwndSource? _source;

    protected override void OnStartup(StartupEventArgs e)
    {
        Log.Write("--- arranque ---");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Write("excepcion no controlada", (Exception)args.ExceptionObject);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write("excepcion en el dispatcher", args.Exception);
            args.Handled = true;    // una isla que falla no debe cerrarse sola
        };

        // Una sola copia: la segunda invocacion avisa a la primera y se va.
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Isla.Mostrar");
        _instance = new Mutex(true, "Isla.SingleInstance", out var created);
        if (!created)
        {
            Log.Write("ya habia una instancia; le pido que se muestre");
            _showEvent.Set();
            Shutdown();
            return;
        }

        base.OnStartup(e);

        _window = new IslaWindow();
        MainWindow = _window;
        _window.SourceInitialized += (_, _) => AttachHooks(_window);
        _window.Show();

        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(new Action(() => _window?.ShowIsland())),
            null, Timeout.Infinite, executeOnlyOnce: false);

        CreateTray();
        Log.Write("isla lista");
    }

    private void AttachHooks(IslaWindow window)
    {
        _source = (HwndSource?)PresentationSource.FromVisual(window);
        _source?.AddHook(WndProc);

        var handle = new WindowInteropHelper(window).Handle;
        Register(handle, HotkeyToggle, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, 0x4E /* N */);
        Register(handle, HotkeyReminder, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, 0x52 /* R */);
        Register(handle, HotkeyHide, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, 0x48 /* H */);
        Register(handle, HotkeyPlayPause, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, 0x50 /* P */);
    }

    private static void Register(IntPtr handle, int id, uint modifiers, uint key)
    {
        var ok = NativeMethods.RegisterHotKey(handle, id, modifiers | NativeMethods.MOD_NOREPEAT, key);
        if (!ok)
        {
            Log.Write($"no se pudo registrar el atajo id={id} (probablemente ya lo usa otra app)");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_HOTKEY) return IntPtr.Zero;

        switch (wParam.ToInt32())
        {
            case HotkeyToggle:
                _window?.ToggleVisible();
                break;
            case HotkeyReminder:
                _window?.ShowReminderInput();
                break;
            case HotkeyHide:
                _window?.HideIsland();
                break;
            case HotkeyPlayPause:
                _window?.TogglePlayback();
                break;
        }

        handled = true;
        return IntPtr.Zero;
    }

    // =================================================================== bandeja
    private void CreateTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Isla",
            Visible = true,
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Mostrar / ocultar isla\tCtrl+Alt+N", null, (_, _) => _window?.ToggleVisible());
        menu.Items.Add("Nuevo recordatorio\tCtrl+Alt+R", null, (_, _) => _window?.ShowReminderInput());
        menu.Items.Add("Añadir archivos al estante…", null, (_, _) => Run(() => _window?.PickFilesForShelf()));
        menu.Items.Add(new Forms.ToolStripSeparator());

        // ---- como se muestra la isla ----
        var reveal = new Forms.ToolStripMenuItem("Asomarse al pasar el cursor")
        {
            Checked = _window?.EdgeReveal ?? true,
        };
        reveal.Click += (_, _) =>
        {
            var enabled = !reveal.Checked;
            _window?.SetEdgeReveal(enabled);
            reveal.Checked = enabled;
        };
        menu.Items.Add(reveal);

        var effects = new Forms.ToolStripMenuItem("Efectos visuales");
        var fxAuto = new Forms.ToolStripMenuItem("Automático");
        var fxOn = new Forms.ToolStripMenuItem("Siempre");
        var fxOff = new Forms.ToolStripMenuItem("Nunca");

        void SyncEffects()
        {
            var mode = _window?.EffectsMode ?? Services.EffectsMode.Auto;
            fxAuto.Checked = mode == Services.EffectsMode.Auto;
            fxOn.Checked = mode == Services.EffectsMode.On;
            fxOff.Checked = mode == Services.EffectsMode.Off;
        }

        fxAuto.Click += (_, _) => { _window?.ApplyEffectsMode(Services.EffectsMode.Auto); SyncEffects(); };
        fxOn.Click += (_, _) => { _window?.ApplyEffectsMode(Services.EffectsMode.On); SyncEffects(); };
        fxOff.Click += (_, _) => { _window?.ApplyEffectsMode(Services.EffectsMode.Off); SyncEffects(); };

        effects.DropDownItems.Add(fxAuto);
        effects.DropDownItems.Add(fxOn);
        effects.DropDownItems.Add(fxOff);
        menu.Items.Add(effects);
        SyncEffects();

        menu.Items.Add(new Forms.ToolStripSeparator());

        var startup = new Forms.ToolStripMenuItem("Iniciar con Windows") { Checked = StartupEnabled() };
        startup.Click += (_, _) =>
        {
            SetStartup(!startup.Checked);
            startup.Checked = StartupEnabled();
        };
        menu.Items.Add(startup);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Abrir carpeta de datos", null, (_, _) => OpenDataFolder());
        menu.Items.Add("Editar lanzador (launcher.json)", null, (_, _) =>
        {
            var path = Path.Combine(DataFolder, "launcher.json");
            if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Acerca de Isla", null, (_, _) => ShowAbout());
        menu.Items.Add("Salir", null, (_, _) => Shutdown());

        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => _window?.ToggleVisible();
    }

    private static string DataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Isla");

    private void Run(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.Invoke(action);
    }

    // =================================================================== inicio automatico
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static bool StartupEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("Isla") is string;
        }
        catch
        {
            return false;
        }
    }

    private static void SetStartup(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (exe is not null)
                {
                    key.SetValue("Isla", $"\"{exe}\"");
                    Log.Write($"inicio automatico activado: {exe}");
                }
            }
            else
            {
                key.DeleteValue("Isla", false);
                Log.Write("inicio automatico desactivado");
            }
        }
        catch (Exception ex)
        {
            Log.Write("no se pudo cambiar el inicio automatico", ex);
        }
    }

    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            Process.Start(new ProcessStartInfo(DataFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write("no se pudo abrir la carpeta de datos", ex);
        }
    }

    private static void ShowAbout()
    {
        var version = typeof(App).Assembly.GetName().Version;
        var numero = version is null ? "1.0.0" : version.ToString(3);

        MessageBox.Show(
            $"Isla {numero}\n\n" +
            "Una isla dinámica para Windows 10 y 11.\n" +
            "Actividad, música y recordatorios en una píldora.\n\n" +
            "Licencia MIT, 2026 Oneirozz.\n" +
            "Sin cuentas, sin telemetría y sin conexiones de red.\n\n" +
            "github.com/oneirozz/Isla",
            "Acerca de Isla",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>
    /// Icono de la bandeja: el mismo Isla.ico del ejecutable, elegido al tamaño
    /// nativo para que no salga borroso. Si algo falla, se dibuja en memoria.
    /// </summary>
    private static Icon LoadTrayIcon()
    {
        try
        {
            var stream = GetResourceStream(new Uri("pack://application:,,,/Isla.ico"))?.Stream;
            if (stream is not null)
            {
                using (stream)
                {
                    return new Icon(stream, Forms.SystemInformation.SmallIconSize);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("no se pudo cargar Isla.ico, se dibuja el icono", ex);
        }

        return BuildIcon();
    }

    /// <summary>Icono dibujado en memoria: una pildora oscura con el punto lavanda.</summary>
    private static Icon BuildIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var body = new SolidBrush(Color.FromArgb(255, 18, 18, 24));
            using var path = RoundedRect(new RectangleF(2, 9, 28, 14), 7);
            g.FillPath(body, path);

            using var dot = new SolidBrush(Color.FromArgb(255, 180, 156, 255));
            g.FillEllipse(dot, 7, 13, 6, 6);

            using var bar = new SolidBrush(Color.FromArgb(200, 247, 247, 250));
            g.FillRectangle(bar, 17, 14, 8, 2);
            g.FillRectangle(bar, 17, 18, 5, 2);
        }

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }

        var handle = _window is null ? IntPtr.Zero : new WindowInteropHelper(_window).Handle;
        if (handle != IntPtr.Zero)
        {
            for (var id = HotkeyToggle; id <= HotkeyPlayPause; id++)
            {
                NativeMethods.UnregisterHotKey(handle, id);
            }
        }

        Log.Write($"--- salida (codigo {e.ApplicationExitCode}) ---");
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
