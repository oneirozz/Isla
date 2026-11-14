using System.IO;
using System.Text.Json;
using Isla.Diagnostics;

namespace Isla.Services;

/// <summary>
/// Estante de archivos: una lista de rutas que el usuario arrastra a la isla para
/// tenerlas a mano. Se guarda como JSON (una ruta por linea) y al arrancar se
/// descartan las que ya no existen.
/// </summary>
internal sealed class ShelfService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Tope del estante: mas alla de esto deja de ser "a mano".</summary>
    private const int MaxItems = 12;

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Isla", "shelf.json");

    private List<string> _items = [];

    internal IReadOnlyList<string> Items => _items;
    internal string SettingsPath => _path;

    internal void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;

            var json = File.ReadAllText(_path);
            var raw = (JsonSerializer.Deserialize<List<string>>(json, Options) ?? [])
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _items = raw.Where(Exists).Take(MaxItems).ToList();
            Log.Write($"estante: {_items.Count} archivo(s)");

            // Si se cayeron rutas muertas, el JSON se queda limpio.
            if (_items.Count != raw.Count) Save();
        }
        catch (Exception ex)
        {
            Log.Write("no se pudo leer el estante", ex);
            _items = [];
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
            Log.Write("no se pudo guardar el estante", ex);
        }
    }

    /// <summary>Añade rutas al final; devuelve cuantas entraron de verdad.</summary>
    internal int Add(IEnumerable<string> paths)
    {
        var added = 0;

        foreach (var path in paths)
        {
            string full;
            try
            {
                full = Path.GetFullPath(path.Trim().Trim('"'));
            }
            catch
            {
                continue;
            }

            if (!Exists(full)) continue;
            if (_items.Contains(full, StringComparer.OrdinalIgnoreCase)) continue;

            _items.Add(full);
            added++;
        }

        if (_items.Count > MaxItems)
        {
            _items.RemoveRange(0, _items.Count - MaxItems);
        }

        if (added > 0) Save();
        return added;
    }

    internal void Remove(string path)
    {
        if (_items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            Save();
        }
    }

    internal void Open(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Write($"no se pudo abrir '{path}'", ex);
        }
    }

    private static bool Exists(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }
}
