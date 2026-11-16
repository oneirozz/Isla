namespace Isla.Core;

/// <summary>Recordatorio ya guardado, en la forma que necesitan las reglas.</summary>
public sealed record ReminderItem(string Text, DateTime? Due, bool Done = false, bool Notified = false);

/// <summary>Lo que se entiende de una linea escrita a mano.</summary>
public readonly record struct ParsedReminder(string Text, DateTime? Due);

/// <summary>
/// Reglas de los recordatorios: que linea se acepta y cual toca. Todo puro, con
/// la fecha de hoy como parametro, para poder probarlo sin reloj ni archivos.
/// </summary>
public static class ReminderRules
{
    /// <summary>
    /// El que toca ahora: primero los que tienen hora mas cercana. Los que no
    /// tienen hora van al final, en orden alfabetico.
    /// </summary>
    public static ReminderItem? Next(IEnumerable<ReminderItem> items) =>
        items
            .Where(r => !r.Done)
            .OrderBy(r => r.Due ?? DateTime.MaxValue)
            .ThenBy(r => r.Text, StringComparer.CurrentCulture)
            .FirstOrDefault();

    /// <summary>
    /// Primer recordatorio vencido que aun no se ha avisado. Los que no tienen
    /// hora nunca vencen.
    /// </summary>
    public static ReminderItem? FirstDue(IEnumerable<ReminderItem> items, DateTime now) =>
        items
            .Where(r => !r.Done && !r.Notified && r.Due is { } due && due <= now)
            .OrderBy(r => r.Due)
            .FirstOrDefault();

    /// <summary>
    /// Acepta "texto" o "texto @18:30" (tambien "@manana" para el dia siguiente
    /// a las 9). Si el '@' no va seguido de una hora, se queda todo como texto.
    /// </summary>
    public static ParsedReminder Parse(string? raw, DateTime today)
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
                due = today.Date.AddDays(1).AddHours(9);
            }

            // Sin texto delante no se recorta nada: "@18:30" se queda tal cual.
            if (due is not null && head.Length > 0) text = head;
        }

        return new ParsedReminder(text, due);
    }

    /// <summary>Texto que se ensena en el panel: "HH:mm · lo que sea", o solo el texto.</summary>
    public static string Label(ReminderItem? item) =>
        item is null ? "ninguno"
        : item.Due is { } due ? $"{due:HH:mm} · {item.Text}"
        : item.Text;
}
