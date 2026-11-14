using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Isla.Diagnostics;

namespace Isla.Services;

internal sealed class LauncherItem
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Ejecutable, documento o URI de shell (ms-settings:, https://...).</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Caracter de Segoe MDL2 Assets. Si falta, se usa la inicial.</summary>
    public string? Glyph { get; set; }
}

/// <summary>
/// Accesos rapidos del lanzador. La primera vez escribe un JSON de ejemplo que el
/// usuario puede editar a mano; no hay interfaz de configuracion a proposito.
/// </summary>
internal sealed class LauncherService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Isla", "launcher.json");

    private List<LauncherItem> _items = [];

    internal IReadOnlyList<LauncherItem> Items => _items;
    internal string SettingsPath => _path;

    internal void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                _items = Defaults();
                Save();
                return;
            }

            var json = File.ReadAllText(_path);
            _items = JsonSerializer.Deserialize<List<LauncherItem>>(json, Options) ?? Defaults();
        }
        catch (Exception ex)
        {
            Log.Write("no se pudo leer el lanzador", ex);
            _items = Defaults();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_items, Options));
        }
        catch (Exception ex)
        {
            Log.Write("no se pudo guardar el lanzador", ex);
        }
    }

    internal void Open(LauncherItem item)
    {
        try
        {
            Process.Start(new ProcessStartInfo(item.Target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write($"no se pudo abrir '{item.Target}'", ex);
        }
    }

    /// <summary>Solo se ofrecen los que existen en esta maquina.</summary>
    private static List<LauncherItem> Defaults()
    {
        var candidates = new List<LauncherItem>
        {
            new() { Name = "Notas", Target = "notepad.exe", Glyph = "\uE70F" },
            new() { Name = "Calculadora", Target = "calc.exe", Glyph = "\uE8EF" },
            new() { Name = "Archivos", Target = "explorer.exe", Glyph = "\uE8B7" },
            new() { Name = "Terminal", Target = "powershell.exe", Glyph = "\uE756" },
            new() { Name = "Ajustes", Target = "ms-settings:", Glyph = "\uE713" },
        };

        var result = new List<LauncherItem>();
        foreach (var item in candidates)
        {
            // Los URI de shell no son rutas de archivo: se aceptan tal cual.
            var isShellUri = item.Target.Contains(':') && item.Target.IndexOf(':') < 3;
            if (isShellUri || File.Exists(item.Target) || FindOnPath(item.Target) is not null)
            {
                result.Add(item);
            }
        }

        return result.Count > 0 ? result : candidates;
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
                // una entrada de PATH invalida no debe romper el lanzador
            }
        }
        return null;
    }
}
