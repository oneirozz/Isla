using System.Diagnostics;
using System.Text;
using Isla.Interop;

namespace Isla.Services;

/// <summary>Operacion larga que el Explorador esta haciendo en este momento.</summary>
internal enum ShellActivityKind
{
    Copy,
    Move,
    Delete,
    Compress,
    Extract,
}

/// <summary>
/// Actividad detectada en el Explorador. El titulo de su ventana de progreso es
/// lo unico que Windows expone publicamente (no hay API de progreso global).
/// </summary>
internal sealed record ShellActivity(ShellActivityKind Kind, string Verb, string Title)
{
    /// <summary>Icono de Segoe MDL2 Assets para la pildora recogida.</summary>
    internal string Glyph => Kind switch
    {
        ShellActivityKind.Copy => "\uE8C8",
        ShellActivityKind.Move => "\uE8DE",
        ShellActivityKind.Delete => "\uE74D",
        ShellActivityKind.Compress => "\uE8B7",
        ShellActivityKind.Extract => "\uE896",
        _ => "\uE8C8",
    };
}

/// <summary>
/// Detecta copias, movimientos, borrados, compresiones y extracciones del
/// Explorador mirando sus ventanas de progreso. Se limita a explorer.exe y a
/// titulos conocidos (espanol e ingles) para no confundir cualquier ventana
/// llamada "Copying..." con una operacion real.
/// </summary>
internal sealed class ActivityService
{
    private static readonly (string Stem, ShellActivityKind Kind, string Verb)[] Patterns =
    {
        ("copiando", ShellActivityKind.Copy, "Copiando"),
        ("copying", ShellActivityKind.Copy, "Copiando"),
        ("moviendo", ShellActivityKind.Move, "Moviendo"),
        ("moving", ShellActivityKind.Move, "Moviendo"),
        ("eliminando", ShellActivityKind.Delete, "Eliminando"),
        ("deleting", ShellActivityKind.Delete, "Eliminando"),
        ("comprimiendo", ShellActivityKind.Compress, "Comprimiendo"),
        ("compressing", ShellActivityKind.Compress, "Comprimiendo"),
        ("extrayendo", ShellActivityKind.Extract, "Extrayendo"),
        ("extracting", ShellActivityKind.Extract, "Extrayendo"),
    };

    private readonly Dictionary<uint, string> _processNames = new();
    private ShellActivity? _last;
    private DateTime _lastScan = DateTime.MinValue;

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
        var title = new StringBuilder(512);

        NativeMethods.EnumWindows((window, _) =>
        {
            if (!NativeMethods.IsWindowVisible(window)) return true;

            NativeMethods.GetWindowThreadProcessId(window, out var pid);
            if (pid == 0 || ProcessName(pid) != "explorer") return true;

            title.Clear();
            if (NativeMethods.GetWindowTextW(window, title, title.Capacity) <= 0) return true;

            var text = title.ToString().Trim();
            foreach (var (stem, kind, verb) in Patterns)
            {
                if (!text.Contains(stem, StringComparison.OrdinalIgnoreCase)) continue;

                found = new ShellActivity(kind, verb, text);
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return found;
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
