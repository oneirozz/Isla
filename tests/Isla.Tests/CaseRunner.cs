namespace Isla.Tests;

/// <summary>
/// Runner minimo: agrupa los casos por area, los ejecuta y devuelve 1 si alguno
/// falla. Sin xUnit ni NuGet, para que el proyecto siga sin dependencias.
/// </summary>
internal static class CaseRunner
{
    private static int _passed;
    private static int _failed;

    /// <summary>Ejecuta un caso. Si falla, se anota y se sigue con el resto.</summary>
    internal static void Case(string name, Action body)
    {
        try
        {
            body();
            _passed++;
            Console.WriteLine($"  ok    {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"  FALLO {name}");
            Console.WriteLine($"        {ex.Message}");
        }
    }

    internal static void Area(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
    }

    /// <summary>Codigo de salida del proceso: 0 todo bien, 1 hay fallos.</summary>
    internal static int Summary()
    {
        Console.WriteLine();
        Console.WriteLine($"{_passed} bien, {_failed} mal");
        return _failed == 0 ? 0 : 1;
    }
}
