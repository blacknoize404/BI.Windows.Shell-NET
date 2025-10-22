# BI.Windows.Shell

Windows shell integration for .NET — no native code of its own, just P/Invoke and COM:

- **Dark mode** for the title bar and for the classic menus the shell draws.
- **Click-through windows** and windows hidden from the taskbar, for widgets and overlays.
- **The native Explorer context menu** for any file, with every system verb and every
  third-party extension (7-Zip and friends), plus your own items appended at the end.

The Java edition lives at [BI.Windows.Shell-JAVA](https://github.com/blacknoize404/BI.Windows.Shell-JAVA)
(Foreign Function & Memory API, same feature set).

> **Documentación en español**: [README.es.md](README.es.md)

## Install

The library is a single `net8.0-windows` project. Reference it directly or pack it:

```xml
<ProjectReference Include="..\BI.Windows.Shell\BI.Windows.Shell.csproj" />
```

Requires Windows 10 1809 or later. Every public entry point returns an explicit result
record instead of throwing: nothing from the interop reaches the host as an exception.

## Dark mode

```csharp
DarkModeResult r = WindowsDarkMode.Apply(hwnd);          // dark title bar and menus
WindowsDarkMode.Set(hwnd, dark: false, titleBar: true);  // back to light
WindowsDarkMode.FlushMenus();                            // re-read the menu theme cache
```

This is best effort and honest about it: the app-mode switch goes through an undocumented
`uxtheme.dll` ordinal (135, `SetPreferredAppMode`) that has survived every Windows 10/11
build so far but is guaranteed by no one. The result tells you what actually happened
(`Applied`, `PreviousMode`, `Ordinal`, `Error`) — on a Windows that refuses, everything
stays as it was and the record says so.

## Explorer context menu

```csharp
ShellMenuResult r = ShellContextMenu.ShowAtCursor(@"C:\Users\me\picture.png", hwnd,
    new[] { new ShellMenuExtra("Copy path", () => Clipboard.SetText(path)) });
```

Call it from a single-threaded-apartment thread (the WPF dispatcher thread is one).
`ShellMenuResult` is `Shown`, `NoFile`, `NotSta` or `InteropFailed` — each one means
what it says, and `InteropFailed` comes with a telemetry line explaining why.

Shell extensions paint their own menu items, and they only do it if someone forwards
the owner-draw messages to `IContextMenu2.HandleMenuMsg`. By default the library
temporarily subclasses the owner window while the popup is open and takes care of it
itself. If the host already hooks its window procedure (a WPF `HwndSourceHook`, a
custom `WndProc`), pass `subclass: false` and forward the messages with
`ShellContextMenu.IsMenuMessage` / `TryHandleMenuMessage` instead.

`Telemetry` on both classes receives one JSON line per call, if you want a record of
what ran.

## Click-through and taskbar visibility

Neither WPF nor WinForms lets a window ignore the mouse and let clicks fall through to
whatever is underneath. `WindowStyles` asks Windows for it (`WS_EX_TRANSPARENT`, plus
`WS_EX_LAYERED` and `WS_EX_NOACTIVATE` so it doesn't steal focus):

```csharp
IntPtr hwnd = WindowHandles.FindOwnWindow("My widget");    // a top-level window of this process
ClickThroughResult r = WindowStyles.SetClickThrough(hwnd, true);
// r.Applied: the style read back is the one requested; r.Error: why not, or null

WindowStyles.SetClickThrough(hwnd, false);                 // takes the mouse again
bool passing = WindowStyles.IsClickThrough(hwnd);
WindowStyles.SetHiddenFromTaskbar(hwnd, true);             // no taskbar button, no Alt+Tab entry
bool hidden  = WindowStyles.IsHiddenFromTaskbar(hwnd);
IntPtr top   = WindowHandles.Foreground();                 // the system's foreground window
```

In WPF the usual way to an HWND is `new WindowInteropHelper(window).Handle`;
`WindowHandles.FindOwnWindow` is for the cases where that doesn't apply — a window owned
by another toolkit in the same process, or a plain console/host window.

Two honest quirks. A translucent window only receives the mouse where its pixels are not
fully transparent, so empty areas take no clicks even with click-through off. And Windows
only reads the taskbar style when the window is shown, so a visible window blinks once
while `SetHiddenFromTaskbar` flips it. If the window is closed and shown again it is a
different native window: call it again.

## License

This project is distributed under the [Apache License 2.0](LICENSE).
