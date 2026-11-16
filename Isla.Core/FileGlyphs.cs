namespace Isla.Core;

/// <summary>
/// Icono de Segoe MDL2 Assets para un archivo del estante, segun su extension.
/// Solo estetica: si la extension no se reconoce, sale el folio generico.
/// </summary>
public static class FileGlyphs
{
    public const string Folder = "\uE8B7";
    public const string Unknown = "\uE7C3";

    public static string For(string? extension, bool isFolder)
    {
        if (isFolder) return Folder;

        return (extension ?? string.Empty).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" => "\uE91B",
            ".mp3" or ".wav" or ".flac" or ".m4a" or ".ogg" => "\uE8D6",
            ".mp4" or ".mkv" or ".webm" or ".avi" or ".mov" => "\uE714",
            ".zip" or ".7z" or ".rar" or ".tar" or ".gz" => "\uE7B8",
            ".pdf" or ".doc" or ".docx" or ".txt" or ".md" or ".rtf" or ".odt" => "\uE8A5",
            _ => Unknown,
        };
    }
}
