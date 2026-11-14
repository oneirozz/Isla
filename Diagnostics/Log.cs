using System.IO;

namespace Isla.Diagnostics;

/// <summary>
/// Log minimo a archivo: sin dependencias y sin consola, para diagnosticar el
/// arranque en una app WinExe. Se rota solo cuando pasa de 1 MB.
/// </summary>
internal static class Log
{
    private static readonly object Gate = new();

    internal static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Isla", "isla.log");

    internal static void Write(string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);

            var file = new FileInfo(FilePath);
            if (file.Exists && file.Length > 1_000_000)
            {
                file.Delete();
            }

            lock (Gate)
            {
                File.AppendAllText(FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Un log que falla no debe tumbar la app.
        }
    }

    internal static void Write(string message, Exception error) =>
        Write($"{message}: {error.GetType().Name}: {error.Message}{Environment.NewLine}{error.StackTrace}");
}
