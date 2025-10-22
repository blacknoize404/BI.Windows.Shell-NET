// BI.Windows.Shell — estilos de ventana que WPF no expone: dejar pasar el ratón
// (WS_EX_TRANSPARENT, para widgets y superposiciones) y desaparecer de la barra de
// tareas/Alt+Tab (WS_EX_TOOLWINDOW). El estado se LEE DE VUELTA del sistema tras el
// cambio — applied solo es true si Windows obedeció. Paridad con la edición Java.
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BI.Windows.Shell;

/// <summary>Cómo quedó la ventana tras el cambio de estilo — el segundo campo es el estado leído de vuelta (clickThrough o hidden, según la llamada).</summary>
public sealed record ClickThroughResult(bool Applied, bool ClickThrough, string? Error);

[SupportedOSPlatform("windows10.0.17763")]
public static class WindowStyles
{
    /// <summary>Si el anfitrión quiere evidencia (una línea JSON por cambio), lo engancha aquí; null = nada escribe.</summary>
    public static Action<string>? Telemetry;

    const int GWL_EXSTYLE = -20;
    const long WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x08000000;
    const long WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
    const int SW_HIDE = 0, SW_SHOWNA = 8;

    /// <summary>La ventana deja pasar el ratón (TRANSPARENT + LAYERED + NOACTIVATE para no robar el foco) o vuelve a recibirlo.
    /// Una ventana translúcida solo recibe clics donde sus píxeles no son alfa 0 — normal en zonas vacías aunque el modo esté apagado.</summary>
    public static ClickThroughResult SetClickThrough(IntPtr hwnd, bool enabled)
    {
        if (hwnd == IntPtr.Zero) return new(false, false, "no hay ventana (¿todavía no se mostró?)");
        try
        {
            long style = GetStyle(hwnd).ToInt64();
            long wanted = enabled
                ? style | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE
                : style & ~(WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
            if (wanted != style) SetStyle(hwnd, (IntPtr)wanted);
            bool now = IsClickThrough(hwnd);
            Emit(new { app = "BI.Windows.Shell", op = "WindowStyles.ClickThrough", requested = enabled, applied = now == enabled, styleBefore = style, styleAfter = wanted });
            return new(now == enabled, now, null);
        }
        catch (Exception ex) { return new(false, false, ex.GetType().Name + ": " + ex.Message); }
    }

    /// <summary>Si la ventana deja pasar el ratón ahora mismo; false si no existe.</summary>
    public static bool IsClickThrough(IntPtr hwnd)
        => hwnd != IntPtr.Zero && (GetStyle(hwnd).ToInt64() & WS_EX_TRANSPARENT) != 0;

    /// <summary>Sin botón en la barra de tareas ni entrada en Alt+Tab (TOOLWINDOW) o de vuelta a lo normal.
    /// Windows solo lee ese estilo al MOSTRAR la ventana: si está visible se oculta un instante y reaparece sin activarse.</summary>
    public static ClickThroughResult SetHiddenFromTaskbar(IntPtr hwnd, bool hidden)
    {
        if (hwnd == IntPtr.Zero) return new(false, false, "no hay ventana (¿todavía no se mostró?)");
        try
        {
            long style = GetStyle(hwnd).ToInt64();
            long wanted = hidden ? (style | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW : style & ~WS_EX_TOOLWINDOW;
            if (wanted != style)
            {
                bool visible = IsWindowVisible(hwnd);
                if (visible) ShowWindow(hwnd, SW_HIDE);
                SetStyle(hwnd, (IntPtr)wanted);
                if (visible) ShowWindow(hwnd, SW_SHOWNA);
            }
            bool now = IsHiddenFromTaskbar(hwnd);
            Emit(new { app = "BI.Windows.Shell", op = "WindowStyles.HiddenFromTaskbar", requested = hidden, applied = now == hidden, styleBefore = style, styleAfter = wanted });
            return new(now == hidden, now, null);
        }
        catch (Exception ex) { return new(false, false, ex.GetType().Name + ": " + ex.Message); }
    }

    /// <summary>Si la ventana está marcada como de herramientas (sin botón en la barra de tareas).</summary>
    public static bool IsHiddenFromTaskbar(IntPtr hwnd)
        => hwnd != IntPtr.Zero && (GetStyle(hwnd).ToInt64() & WS_EX_TOOLWINDOW) != 0;

    static IntPtr GetStyle(IntPtr h) => IntPtr.Size == 8 ? GetWindowLongPtrW(h, GWL_EXSTYLE) : (IntPtr)GetWindowLongW(h, GWL_EXSTYLE);
    static IntPtr SetStyle(IntPtr h, IntPtr v) => IntPtr.Size == 8 ? SetWindowLongPtrW(h, GWL_EXSTYLE, v) : (IntPtr)SetWindowLongW(h, GWL_EXSTYLE, v.ToInt32());
    static void Emit(object o) { try { Telemetry?.Invoke(System.Text.Json.JsonSerializer.Serialize(o)); } catch { } }

    [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtrW(IntPtr h, int i);
    [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtrW(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] static extern int GetWindowLongW(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLongW(IntPtr h, int i, int v);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
}
