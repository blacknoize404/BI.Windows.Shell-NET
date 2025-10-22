// BI.Windows.Shell — HWNDs que WPF/WinForms no deberían obligarte a buscar a mano:
// la ventana propia por título (FindWindowExW sobre las de nivel superior, filtradas
// por PID propio) y la que está en primer plano. Paridad con la edición Java.
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BI.Windows.Shell;

[SupportedOSPlatform("windows10.0.17763")]
public static class WindowHandles
{
    /// <summary>La ventana de nivel superior de ESTE proceso con ese título exacto, o Zero si aún no existe (o no es su título).</summary>
    public static IntPtr FindOwnWindow(string title)
    {
        if (title is null) return IntPtr.Zero;
        uint self = (uint)Environment.ProcessId;
        var prev = IntPtr.Zero;
        while (true)
        {
            var hwnd = FindWindowExW(IntPtr.Zero, prev, null, title);
            if (hwnd == IntPtr.Zero) return IntPtr.Zero;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == self) return hwnd;
            prev = hwnd;
        }
    }

    /// <summary>La ventana en primer plano del sistema, o Zero.</summary>
    public static IntPtr Foreground() => GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string? cls, string title);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
}
