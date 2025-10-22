// BI.Windows.Shell — Tema OSCURO para los menús clásicos que dibuja la shell
// (el truco de las propias apps de Microsoft: ordinales NO documentados de uxtheme.dll).
// Mejor-efuerzo declarado: si el build del OS no obedece, queda como siempre y lo dice.
// Pitfalls YA PROBADOS: delegados NOMBRADOS (el marshal rechaza genéricos — el candado
// ordErr lo declaró); en NT 10.0.26200 el ordinal 135 existe y su llamada retorna >=0 (sonda).
// El retorno de SetPreferredAppMode es el MODO ANTERIOR, no un HRESULT (sonda: f(2)→0, f(0)→2).
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BI.Windows.Shell;

/// <summary>Cómo quedó la aplicación del modo oscuro — medible, no prometido.</summary>
public sealed record DarkModeResult(bool Applied, int PreviousMode, int OrdinalUsed, string? Error);

[SupportedOSPlatform("windows10.0.17763")]
public static class WindowsDarkMode
{
    /// <summary>Si el anfitrión quiere evidencia, lo engancha aquí; null = nada escribe.</summary>
    public static Action<string>? Telemetry;

    delegate int IAMode(int mode);   // SetPreferredAppMode — NO genérico (el marshal lo rechaza)
    delegate void Nk();              // FlushMenuThemes — ídem

    /// <summary>Aplica ForceDark para TODO lo que la shell dibuje a partir de ahora en este proceso + barra de título DWM (best-effort).</summary>
    public static DarkModeResult Apply(IntPtr hwnd, bool titleBar = true) => Set(hwnd, true, titleBar);

    /// <summary>Fuerza el tema oscuro o el claro (2 = ForceDark, 3 = ForceLight — la vuelta también existe,
    /// paridad con la edición Java). hwnd = la ventana cuya barra de título cambia; Zero si solo importan los menús.</summary>
    public static DarkModeResult Set(IntPtr hwnd, bool dark, bool titleBar)
    {
        try
        {
            if (hwnd != IntPtr.Zero && titleBar)
            {
                var v = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref v, 4) != 0)      // attr 20 en builds modernos
                    DwmSetWindowAttribute(hwnd, 19, ref v, 4);           // fallback Win10 viejo
            }
            var h = LoadLibrary("uxtheme.dll");
            if (h == IntPtr.Zero) return new(false, 0, 0, "uxtheme.dll no cargó");
            var p = GetProcAddress(h, (IntPtr)135);                      // SetPreferredAppMode (enumerado vivo en 26200)
            if (p == IntPtr.Zero) return new(false, 0, 0, "ordinal 135 no existe en este build del OS");
            var mode = Marshal.GetDelegateForFunctionPointer(p, typeof(IAMode)) as IAMode;
            if (mode is null) return new(false, 0, 0, "delegado inaccesible para el ordinal 135");
            var prev = mode(dark ? 2 : 3);
            FlushMenus();
            Emit(new { app = "BI.Windows.Shell", op = "DarkMode.Apply", os = Environment.OSVersion.VersionString, dark, applied = prev >= 0, previousMode = prev, ordinal = 135 });
            return new(prev >= 0, prev, 135, null);
        }
        catch (Exception ex) { return new(false, 0, 0, ex.GetType().Name + ": " + ex.Message); }
    }

    /// <summary>La shell invalida la caché del tema a veces — llamar JUSTO ANTES de cada popup (público, es el mismo truco para TrackPopupMenu propios).</summary>
    public static void FlushMenus()
    {
        try
        {
            var h = LoadLibrary("uxtheme.dll");
            if (h != IntPtr.Zero && GetProcAddress(h, (IntPtr)136) is var p && p != IntPtr.Zero)
                Marshal.GetDelegateForFunctionPointer(p, typeof(Nk)).DynamicInvoke();
        }
        catch { }
    }

    static void Emit(object o)
    { try { Telemetry?.Invoke(System.Text.Json.JsonSerializer.Serialize(o)); } catch { } }

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)] static extern IntPtr LoadLibrary(string name);
    [DllImport("kernel32", SetLastError = true)] static extern IntPtr GetProcAddress(IntPtr h, IntPtr ordinal);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
}
