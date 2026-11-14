using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Isla.Interop;

/// <summary>
/// Llamadas Win32 que WPF no expone: estilo de ventana y atajos globales.
/// El cristal es simulado con gradientes y sombra (ver README): el acrilico de
/// DWM se aplica al rectangulo entero de la ventana y con la isla animandose
/// obligaria a recolocar la region en cada fotograma.
/// </summary>
internal static class NativeMethods
{
    // ---- estilos de ventana ----
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;   // fuera de Alt+Tab

    // ---- atajos globales ----
    internal const int WM_HOTKEY = 0x0312;
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ---- aviso sonoro del sistema ----
    private const uint SND_ALIAS = 0x0001;
    private const uint SND_ASYNC = 0x0080;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(string sound, IntPtr module, uint flags);

    /// <summary>Suena el aviso de Windows sin bloquear el hilo de la interfaz.</summary>
    internal static void PlayNotificationSound()
    {
        try
        {
            PlaySound("SystemAsterisk", IntPtr.Zero, SND_ALIAS | SND_ASYNC);
        }
        catch
        {
            // Sin tarjeta de sonido o sin esquema, el aviso visual ya basta.
        }
    }

    // ---- posicion del cursor (para asomarse al borde) ----
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT point);

    // ---- reproductores con API de Winamp (AIMP, Winamp, QCD) ----
    // AIMP no publica SMTC, pero emula esta API: una ventana "Winamp v1.x" que
    // responde a WM_USER para leer el estado y a WM_COMMAND para los botones.
    internal const int WM_COMMAND = 0x0111;
    internal const int WM_USER = 0x0400;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr FindWindowW(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SendMessageW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Igual que SendMessageW, pero abandona si la ventana de destino esta
    /// colgada: la isla sondea cada segundo y no puede quedarse esperando.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SendMessageTimeoutW(
        IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassNameW(IntPtr hWnd, System.Text.StringBuilder name, int maxCount);

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    internal static void HideFromAltTab(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var style = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW);
    }
}
