// BI.Shell — el MENÚ CONTEXTUAL NATIVO de Windows (IContextMenu del shell),
// el mismo que abre Explorer: verbos del sistema + TODAS las extensiones de terceros (7-Zip y
// cia), con extras del anfitrión anexados al final después de una línea separada.
// Reutilizable desde WPF, WinForms o consola STA — la lib es puros DllImport + COM, sin WPF.
// Regla: best-effort declarado — ningún fallo del interop tumba al anfitrión (enum resultado).
// Pitfalls YA PROBADOS en el disco (no re-inventar desde narración):
//  · delegados NOMBRADOS para Marshal.GetDelegateForFunctionPointer (Func<int,int> genérico lo rechaza);
//  · UN solo RCW por IUnknown (dos GetObjectForIUnknown sobre menuPtr + Marshal.Release a la vez
//    descuadran refcounts — ReleaseComObject en finally y basta);
//  · el pidl hijo de SHBindToParent vive DENTRO del pidl padre: NUNCA ILFree separado;
//  · Nuestros extras van en un rango de ids lejos del shell (first+0x7000): con muchas
//    extensiones el rango continuo puede colisionar.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace BI.Shell;

/// <summary>Ítem EXTRA que el anfitrión anexa al final del menú del shell (tras una línea separada).</summary>
public sealed record ShellMenuExtra(string Label, Action Run);

/// <summary>Cómo terminó la presentación del menú — todo declarado, nada silencioso.</summary>
public enum ShellMenuResult { Shown, NoFile, NotSta, InteropFailed }

[SupportedOSPlatform("windows10.0.17763")]
public static class ShellContextMenu
{
    /// <summary>Si el anfitrión quiere evidencia (una línea JSON por corrida), lo engancha aquí; null = nada escribe.</summary>
    public static Action<string>? Telemetry;

    const uint CMF_NORMAL = 0x0, CMF_EXPLORE = 0x4;
    const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2;
    const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800;
    const uint WM_NULL = 0;

    /// <summary>Los mensajes owner-draw del menú de la shell que el anfitrión DEBE reenviar (sync con WinHook).</summary>
    public static bool IsMenuMessage(int msg) => msg is 0x117 or 0x2B or 0x2C or 0x121 or 0x120;

    /// <summary>Reenvía un mensaje owner-draw a IContextMenu2 (con esto 7-Zip y cia se PINTAN, no salen en blanco).</summary>
    public static bool TryHandleMenuMessage(int msg, IntPtr wp, IntPtr lp, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (_live is null) return false;
        try { return _live.HandleMenuMsg((uint)msg, wp, lp) >= 0; } catch { return false; }
    }

    static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct CMINVOKECOMMANDINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;        // MAKEINTRESOURCE del offset del verbo del shell
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E6-0000-0000-C000-000000000046")]
    interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszName, out uint pchEaten, out IntPtr ppidl, ref uint pdwAttrs);
        [PreserveSig] int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenum);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, IntPtr apidl, ref uint rgfInOut);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, IntPtr apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E4-0000-0000-C000-000000000046")]
    interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO pici);
        [PreserveSig] int GetCommandString(IntPtr idCmd, uint uFlags, IntPtr pwReserved, IntPtr pszName, int cchMax);
    }

    // El reenvío owner-draw OBLIGATORIO para que las extensiones pinten sus ítems:
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F8-0000-0000-C000-000000000046")]
    interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(IntPtr pici);
        [PreserveSig] int GetCommandString(IntPtr idCmd, uint uFlags, IntPtr pwReserved, IntPtr pszName, int cchMax);
        [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wp, IntPtr lp);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);
    [DllImport("shell32.dll")]
    static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr ppidlLast);
    [DllImport("shell32.dll")]
    static extern void ILFree(IntPtr pidl);

    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);
    [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr hMenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);

    static IContextMenu2? _live;   // vive SOLO mientras el popup está en pantalla (un popup a la vez — declarado)

    /// <summary>Abre el menú del shell en las coordenadas de pantalla dadas (device pixels).</summary>
    public static ShellMenuResult Show(string path, IntPtr hwndOwner, int screenX, int screenY,
                                       IReadOnlyList<ShellMenuExtra>? extras = null)
        => ShowCore(path, hwndOwner, screenX, screenY, extras);

    /// <summary>Abre el menú en la posición actual del CURSOR: GetCursorPos DENTRO de la lib (DPI de monitores con escala ≠100%).</summary>
    public static ShellMenuResult ShowAtCursor(string path, IntPtr hwndOwner,
                                               IReadOnlyList<ShellMenuExtra>? extras = null)
    {
        if (!GetCursorPos(out var p))
        { Emit(new { app = "BI.Shell", op = "ShowAtCursor", err = "GetCursorPos falló", at = DateTime.Now.ToString("s") }); return ShellMenuResult.InteropFailed; }
        return ShowCore(path, hwndOwner, p.X, p.Y, extras);
    }

    static ShellMenuResult ShowCore(string path, IntPtr hwndOwner, int screenX, int screenY,
                                    IReadOnlyList<ShellMenuExtra>? extras)
    {
        IntPtr pidl = IntPtr.Zero, folderPtr = IntPtr.Zero, hMenu = IntPtr.Zero, apidl = IntPtr.Zero;
        object? icmObj = null, folderObj = null;
        try
        {
            if (hwndOwner == IntPtr.Zero) return ShellMenuResult.InteropFailed;
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) return ShellMenuResult.NotSta;
            if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero) return ShellMenuResult.NoFile;
            var iidSh = IID_IShellFolder;
            if (SHBindToParent(pidl, ref iidSh, out folderPtr, out var childPidl) != 0 || folderPtr == IntPtr.Zero) return ShellMenuResult.InteropFailed;
            folderObj = Marshal.GetObjectForIUnknown(folderPtr);      // UN RCW por IUnknown; su ReleaseComObject en finally
            var folder = (IShellFolder)folderObj;

            apidl = Marshal.AllocCoTaskMem(IntPtr.Size);              // array de UN pidl (el componente hijo)
            Marshal.WriteIntPtr(apidl, childPidl);
            var iidCm = IID_IContextMenu;
            if (folder.GetUIObjectOf(hwndOwner, 1, apidl, ref iidCm, IntPtr.Zero, out var menuPtr) != 0 || menuPtr == IntPtr.Zero) return ShellMenuResult.InteropFailed;
            icmObj = Marshal.GetObjectForIUnknown(menuPtr);           // UN solo RCW (el Marshal.Release bruto queda fuera)
            var icm = (IContextMenu)icmObj;
            _live = icmObj as IContextMenu2;                          // QI: si no hay owner-draw → null e igual se abre

            if (extras is { Count: > 0x7FF }) return ShellMenuResult.InteropFailed;   // el rango de extras no puede crecer a lo loco

            hMenu = CreatePopupMenu();
            uint first = 1;
            int hr = icm.QueryContextMenu(hMenu, 0, first, first + 0x6FFF, CMF_NORMAL | CMF_EXPLORE);
            if (hr < 0) return ShellMenuResult.InteropFailed;
            int shellCount = hr & 0xFFFF;                             // verbos que puso el shell

            int extraBase = (int)first + 0x7000;                      // lejos del rango del shell (no colisión con extensiones)
            if (extras is { Count: > 0 })
            {
                AppendMenuW(hMenu, MF_SEPARATOR, UIntPtr.Zero, null);
                for (int i = 0; i < extras.Count; i++)
                    AppendMenuW(hMenu, MF_STRING, (UIntPtr)(uint)(extraBase + i), extras[i].Label);
            }

            WindowsDarkMode.FlushMenus();                             // la shell invalida la caché del tema a veces
            SetForegroundWindow(hwndOwner);
            int chosen = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, screenX, screenY, hwndOwner, IntPtr.Zero);
            PostMessage(hwndOwner, WM_NULL, IntPtr.Zero, IntPtr.Zero);   // desbloquea el popup del shell

            if (chosen == 0) return ShellMenuResult.Shown;            // cerrado sin elegir: nada por invocar
            if (extras is { Count: > 0 } && chosen >= extraBase && chosen < extraBase + extras.Count)
            {
                extras[chosen - extraBase].Run();
                return ShellMenuResult.Shown;
            }
            var ci = new CMINVOKECOMMANDINFO
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                lpVerb = (IntPtr)(chosen - (int)first),               // offset del verbo del shell
                nShow = 1,                                            // SW_SHOWNORMAL
                hwnd = hwndOwner,
            };
            icm.InvokeCommand(ref ci);
            return ShellMenuResult.Shown;
        }
        catch (Exception ex)
        { Emit(new { app = "BI.Shell", op = "Show", err = ex.GetType().Name + ": " + ex.Message, at = DateTime.Now.ToString("s") }); return ShellMenuResult.InteropFailed; }
        finally
        {
            if (hMenu != IntPtr.Zero) DestroyMenu(hMenu);
            if (icmObj != null) try { Marshal.ReleaseComObject(icmObj); } catch { }
            if (folderObj != null) try { Marshal.ReleaseComObject(folderObj); } catch { }
            if (apidl != IntPtr.Zero) Marshal.FreeCoTaskMem(apidl);
            if (pidl != IntPtr.Zero) ILFree(pidl);
            _live = null;
        }
    }

    internal static void Emit(object o)
    { try { Telemetry?.Invoke(System.Text.Json.JsonSerializer.Serialize(o)); } catch { } }
}
