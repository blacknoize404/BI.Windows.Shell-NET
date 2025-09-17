# BI.Shell

Utilidades de shell nativo de Windows para .NET — **cero dependencias de WPF** (usable desde WPF, WinForms o una app de consola con message loop).

## Qué hay dentro

### `WindowsDarkMode` — menús clásicos en oscuro

La shell de Windows dibuja los menús clásicos (contextuales, barras de menú) con el tema claro aunque tu app sea oscura. `WindowsDarkMode` aplica el modo oscuro de uxtheme llamando a los **ordinales no documentados** (135/136) — la técnica que usa el propio `notepad.exe`:

```csharp
using BI.Shell;

// hwnd de la ventana (WPF: PresentationSource.FromVisual(this) → HwndSource.Handle)
DarkModeResult r = WindowsDarkMode.Apply(hwnd, titleBar: true);
// r: applied · previousMode · ordinal usado — la evidencia se reporta, no se adivina
```

- `WindowsDarkMode.FlushMenus()` — re-tema los menús tras `ClearMenu`/recrear (la shell clona el menú y pierde el tema).
- `Telemetry`: gancho `Action<string>` opcional — cada Apply/Flush emite un JSON de evidencia.

### `ShellContextMenu` — el menú contextual REAL del Explorador

`IContextMenu`/`IContextMenu2` con el puente owner-draw completo (`WM_INITMENU`, `WM_MEASUREITEM`, `WM_DRAWITEM`, `WM_MENUCHAR`, `WM_INITMENUPOPUP`) — los ítems que la shell marca como owner-drawn se reenvían al objeto COM para que se dibujen ellos:

```csharp
using BI.Shell;

// desde WPF/WinForms: reenviar los mensajes del menú en el hook de la ventana
if (ShellContextMenu.IsMenuMessage(m.Msg) &&
    ShellContextMenu.TryHandleMenuMessage(m.Msg, m.WParam, m.LParam, out var res))
{ m.Result = res; handled = true; }

// mostrar el menú de un archivo en el punto del clic derecho
ShellMenuResult r = ShellContextMenu.Show(path, hwnd, x, y,
    extras: new[] { new ShellMenuExtra("Abrir ubicación", () => AbrirUbicacion(path)) });
```

- `ShowAtCursor` — la variante que posiciona en el cursor actual.
- `ShellMenuExtra` — ítems propios mezclados con los del shell (separadores incluidos).
- Resultados explícitos (`InteropFailed`, `NotSta`, `NoFile`…): nada silencioso — la llamada exige STA y el archivo existente, y lo declara.
- `Telemetry`: misma evidencia JSON por operación.

## Requisitos

- .NET 8 — `net8.0-windows`
- Windows 10 1809+ (los ordinales de uxtheme existen desde entonces; en builds antiguos `Apply` devuelve `applied:false` declarado, no explota)
- El hilo que llama a `ShellContextMenu` debe ser STA con message loop

## Filosofía

Nada silencioso: cada resultado (éxito o rechazo) viaja en enums/records explícitos y se puede telemetrizar. Cero P/Invoke genérico: los layouts están verificados contra `winioctl.h`/SDK.

Origen: extraído del motor de búsqueda **Buscador High Level** (BlackNoizE Apps) — el menú contextual y el tema oscuro de su GUI, empaquetados como librería reutilizable.
