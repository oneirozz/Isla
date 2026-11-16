namespace Isla.Tests;

/// <summary>Comprobaciones que lanzan si no se cumple lo esperado.</summary>
internal static class Check
{
    internal static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{what}: esperaba <{expected}>, vino <{actual}>");
    }

    internal static void True(bool value, string what)
    {
        if (!value) throw new InvalidOperationException($"{what}: era falso");
    }

    internal static void Near(double expected, double actual, double margin, string what)
    {
        if (Math.Abs(expected - actual) > margin)
            throw new InvalidOperationException($"{what}: esperaba {expected}±{margin}, vino {actual}");
    }

    internal static void Null<T>(T? value, string what) where T : class
    {
        if (value is not null) throw new InvalidOperationException($"{what}: esperaba null, vino <{value}>");
    }

    internal static void Null<T>(T? value, string what) where T : struct
    {
        if (value.HasValue) throw new InvalidOperationException($"{what}: esperaba null, vino <{value}>");
    }
}
