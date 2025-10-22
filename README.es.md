# BI.Windows.Shell

Integración con la shell de Windows para .NET — sin código nativo propio, solo P/Invoke y COM:

- **Modo oscuro** para la barra de título y para los menús clásicos que dibuja la shell.
- **Ventanas que dejan pasar el ratón** (click-through) y ventanas ocultas de la barra de tareas.
- **El menú contextual nativo del Explorador** para cualquier archivo, con todos los verbos del
  sistema y todas las extensiones de terceros (7-Zip y compañía), y elementos propios al final.

La edición Java está en [BI.Windows.Shell-JAVA](https://github.com/blacknoize404/BI.Windows.Shell-JAVA)
(API de funciones externas FFM, mismo conjunto de funciones).

> **English documentation**: [README.md](README.md)

## Instalación

La biblioteca es un solo proyecto `net8.0-windows`. Se referencia directamente o se empaqueta:

```xml
<ProjectReference Include="..\BI.Windows.Shell\BI.Windows.Shell.csproj" />
```

Requiere Windows 10 1809 o posterior. Cada punto de entrada público devuelve un registro de
resultado explícito en lugar de lanzar excepciones: nada del interop llega al anfitrión
como una excepción.

## Modo oscuro

```csharp
DarkModeResult r = WindowsDarkMode.Apply(hwnd);          // barra de título y menús oscuros
WindowsDarkMode.Set(hwnd, dark: false, titleBar: true);  // de vuelta al claro
WindowsDarkMode.FlushMenus();                            // relee la caché del tema de los menús
```

Es de mejor esfuerzo y lo declara: el interruptor del modo de la aplicación pasa por un
ordinal no documentado de `uxtheme.dll` (135, `SetPreferredAppMode`) que ha sobrevivido a
todos los builds de Windows 10/11 hasta hoy pero que nadie garantiza. El resultado dice lo
que pasó de verdad (`Applied`, `PreviousMode`, `Ordinal`, `Error`) — en un Windows que no
obedece, todo queda como estaba y el registro lo cuenta.

## Menú contextual del Explorador

```csharp
ShellMenuResult r = ShellContextMenu.ShowAtCursor(@"C:\Users\yo\imagen.png", hwnd,
    new[] { new ShellMenuExtra("Copiar ruta", () => Clipboard.SetText(ruta)) });
```

Llámalo desde un hilo de apartamento COM monohilo (el hilo del dispatcher de WPF lo es).
`ShellMenuResult` es `Shown`, `NoFile`, `NotSta` o `InteropFailed` — cada uno significa lo
que dice, y `InteropFailed` viene acompañado de una línea de telemetría con el motivo.

Las extensiones de la shell pintan sus propios elementos del menú, y solo lo hacen si alguien
reenvía los mensajes de dibujo a `IContextMenu2.HandleMenuMsg`. Por defecto la biblioteca
subclasifica temporalmente la ventana propietaria mientras el menú está abierto y se encarga
sola. Si el anfitrión ya engancha el procedimiento de su ventana (un `HwndSourceHook` de WPF,
un `WndProc` propio), pasa `subclass: false` y reenvía los mensajes con
`ShellContextMenu.IsMenuMessage` / `TryHandleMenuMessage`.

`Telemetry` en las dos clases recibe una línea JSON por llamada, por si quieres constancia
de lo que se ejecutó.

## Click-through y visibilidad en la barra de tareas

Ni WPF ni WinForms permiten que una ventana ignore el ratón y lo deje pasar a lo que hay
debajo. `WindowStyles` se lo pide a Windows (`WS_EX_TRANSPARENT`, junto con `WS_EX_LAYERED`
y `WS_EX_NOACTIVATE` para que no robe el foco):

```csharp
IntPtr hwnd = WindowHandles.FindOwnWindow("Mi widget");    // una ventana de nivel superior de este proceso
ClickThroughResult r = WindowStyles.SetClickThrough(hwnd, true);
// r.Applied: el estilo leído de vuelta es el pedido; r.Error: por qué no, o null

WindowStyles.SetClickThrough(hwnd, false);                 // vuelve a recibir el ratón
bool pasa = WindowStyles.IsClickThrough(hwnd);
WindowStyles.SetHiddenFromTaskbar(hwnd, true);             // sin botón en la barra ni entrada en Alt+Tab
bool oculta = WindowStyles.IsHiddenFromTaskbar(hwnd);
IntPtr arriba = WindowHandles.Foreground();                // la ventana en primer plano del sistema
```

En WPF el HWND se obtiene normalmente con `new WindowInteropHelper(ventana).Handle`;
`WindowHandles.FindOwnWindow` es para los casos en que eso no aplica — una ventana de otro
toolkit dentro del mismo proceso, o una ventana de consola/anfitrión sin toolkit.

Dos peculiaridades honestas. Una ventana translúcida solo recibe el ratón donde sus píxeles
no son del todo transparentes, así que las zonas vacías no reciben clics aunque el modo esté
apagado. Y Windows solo lee el estilo de la barra de tareas al mostrar la ventana, así que
una ventana visible parpadea una vez mientras `SetHiddenFromTaskbar` la cambia. Si la ventana
se cierra y se vuelve a mostrar es otra ventana nativa: hay que repetir la llamada.

## Licencia

Este proyecto se distribuye bajo la [Apache License 2.0](LICENSE).
