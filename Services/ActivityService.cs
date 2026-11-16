using System.Diagnostics;
using System.Text;
using System.Windows.Automation;
using Isla.Core;
using Isla.Diagnostics;
using Isla.Interop;

namespace Isla.Services;

/// <summary>
/// Detecta copias, movimientos, borrados, compresiones y extracciones del
/// Explorador mirando sus ventanas de progreso. Se limita a explorer.exe para no
/// confundir cualquier ventana llamada "Copying..." con una operacion real.
///
/// En Windows 10 el titulo de esa ventana es solo el porcentaje ("84%
/// completado") y el texto de la operacion vive dentro de sus controles, asi que
/// ahi se lee con UI Automation, en un hilo aparte: hablar con explorer.exe con
/// el disco a tope puede tardar y la interfaz no debe esperar.
/// </summary>
internal sealed class ActivityService
{
    private const string ProgressWindowClass = "OperationStatusWindow";

    private readonly Dictionary<uint, string> _processNames = new();
    private ShellActivity? _last;
    private DateTime _lastScan = DateTime.MinValue;

    // ---- lectura de la ventana de progreso, en segundo plano ----
    private IntPtr _dialogWindow;
    private volatile bool _dialogReading;
    private volatile ShellActivity? _dialogActivity;

    internal ShellActivity? Detect()
    {
        // Las ventanas de progreso duran segundos: no hace falta mirar en cada tick.
        if (DateTime.UtcNow - _lastScan < TimeSpan.FromMilliseconds(700)) return _last;
        _lastScan = DateTime.UtcNow;
        _last = Scan();
        return _last;
    }

    private ShellActivity? Scan()
    {
        if (_processNames.Count > 256) _processNames.Clear();

        ShellActivity? found = null;
        var dialog = IntPtr.Zero;
        var title = new StringBuilder(512);
        var className = new StringBuilder(128);

        NativeMethods.EnumWindows((window, _) =>
        {
            if (!NativeMethods.IsWindowVisible(window)) return true;

            NativeMethods.GetWindowThreadProcessId(window, out var pid);
            if (pid == 0 || ProcessName(pid) != "explorer") return true;

            className.Clear();
            NativeMethods.GetClassNameW(window, className, className.Capacity);
            if (className.ToString() == ProgressWindowClass)
            {
                dialog = window;
                return false;
            }

            // Respaldo: en otras versiones el titulo si nombra la operacion.
            title.Clear();
            if (NativeMethods.GetWindowTextW(window, title, title.Capacity) <= 0) return true;

            if (ShellActivities.Match(title.ToString()) is { } activity)
            {
                found = activity;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        if (dialog != IntPtr.Zero)
        {
            WatchDialog(dialog);
            return _dialogActivity;
        }

        StopWatchingDialog();
        return found;
    }

    /// <summary>
    /// Pide el texto de la ventana de progreso si aun no se conoce. Una lectura
    /// por ventana: mientras dura la operacion el texto se reutiliza.
    /// </summary>
    private void WatchDialog(IntPtr window)
    {
        if (window != _dialogWindow)
        {
            Volatile.Write(ref _dialogWindow, window);
            _dialogActivity = null;
        }

        if (_dialogReading || _dialogActivity is not null) return;
        _dialogReading = true;

        var handle = window;
        var thread = new Thread(() =>
        {
            try
            {
                var activity = ReadDialog(handle);
                if (Volatile.Read(ref _dialogWindow) == handle) _dialogActivity = activity;
            }
            catch (Exception ex)
            {
                Log.Write("no se pudo leer la ventana de progreso", ex);
            }
            finally
            {
                _dialogReading = false;
            }
        })
        {
            IsBackground = true,
            Name = "isla-actividad",
        };

        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    private void StopWatchingDialog()
    {
        if (Volatile.Read(ref _dialogWindow) == IntPtr.Zero && _dialogActivity is null) return;
        Volatile.Write(ref _dialogWindow, IntPtr.Zero);
        _dialogActivity = null;
    }

    /// <summary>
    /// Nombres de los controles de la ventana, de fuera adentro. La operacion es
    /// el primero que la describe ("Copiando 20.001 elementos de copia a destino").
    /// </summary>
    private static ShellActivity? ReadDialog(IntPtr window)
    {
        var root = AutomationElement.FromHandle(window);
        if (root is null) return null;

        var elements = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        foreach (AutomationElement element in elements)
        {
            string name;
            try
            {
                name = element.Current.Name;
            }
            catch (ElementNotAvailableException)
            {
                // La ventana se esta redibujando: puede faltar algun control.
                continue;
            }

            if (ShellActivities.Match(name) is { } activity) return activity;
        }

        return null;
    }

    /// <summary>Nombre del proceso, cacheado: se consulta una vez por PID.</summary>
    private string ProcessName(uint pid)
    {
        if (_processNames.TryGetValue(pid, out var cached)) return cached;

        string name;
        try
        {
            name = Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            // Proceso protegido o que acaba de cerrarse: no es el Explorador.
            name = string.Empty;
        }

        _processNames[pid] = name;
        return name;
    }
}
