using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Isla.Diagnostics;

namespace Isla.Services;

internal sealed class Reminder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..8];
    public string Text { get; set; } = string.Empty;
    public DateTime? Due { get; set; }
    public bool Done { get; set; }

    /// <summary>Ya se avisó de que ha vencido (para no repetirlo cada 20 s).</summary>
    public bool Notified { get; set; }
}

/// <summary>
/// Recordatorios en un JSON local. Sin servidores y sin cuenta: es una lista de
/// texto con una hora opcional, pensada para verse de un vistazo en la isla.
/// </summary>
internal sealed class ReminderService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Isla", "reminders.json");

    private List<Reminder> _items = [];

    internal IReadOnlyList<Reminder> Pending =>
        _items.Where(r => !r.Done).ToList();

    internal void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            _items = JsonSerializer.Deserialize<List<Reminder>>(json, Options) ?? [];
        }
        catch (Exception ex)
        {
            Log.Write("no se pudieron leer los recordatorios", ex);
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
            Log.Write("no se pudieron guardar los recordatorios", ex);
        }
    }

    /// <summary>El que toca ahora: primero los que tienen hora mas cercana.</summary>
    internal Reminder? Next()
    {
        var pending = Pending;
        if (pending.Count == 0) return null;

        return pending
            .OrderBy(r => r.Due ?? DateTime.MaxValue)
            .ThenBy(r => r.Text, StringComparer.CurrentCulture)
            .First();
    }

    /// <summary>
    /// Acepta "texto" o "texto @18:30" (tambien "@manana" para el dia siguiente).
    /// </summary>
    internal Reminder Add(string raw)
    {
        var text = (raw ?? string.Empty).Trim();
        DateTime? due = null;

        var at = text.LastIndexOf('@');
        if (at > 0)
        {
            var tail = text[(at + 1)..].Trim();
            var head = text[..at].Trim();

            if (DateTime.TryParse(tail, out var parsed))
            {
                due = parsed;
            }
            else if (tail.Equals("manana", StringComparison.OrdinalIgnoreCase) ||
                     tail.Equals("mañana", StringComparison.OrdinalIgnoreCase))
            {
                due = DateTime.Today.AddDays(1).AddHours(9);
            }

            if (due is not null && head.Length > 0) text = head;
        }

        var reminder = new Reminder { Text = text, Due = due };
        _items.Add(reminder);
        Save();
        return reminder;
    }

    /// <summary>
    /// Devuelve el primer recordatorio vencido que aun no se ha avisado, y lo
    /// marca para no repetir el aviso. Los que no tienen hora nunca vencen.
    /// </summary>
    internal Reminder? TakeDue(DateTime now)
    {
        var item = _items
            .Where(r => !r.Done && !r.Notified && r.Due is { } due && due <= now)
            .OrderBy(r => r.Due)
            .FirstOrDefault();

        if (item is null) return null;

        item.Notified = true;
        Save();
        return item;
    }

    internal void Complete(string id)
    {
        var item = _items.FirstOrDefault(r => r.Id == id);
        if (item is null) return;
        item.Done = true;
        Save();
    }

    internal int PendingCount => _items.Count(r => !r.Done);
}
