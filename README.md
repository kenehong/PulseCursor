# PulseCursor

Windows tray app with a real WinUI 3 resource card and settings window.
The arrow is a **solid color**: no waves or partial fill.
Its black outline, size and click hotspot are preserved.

| Default usage | State | Default color |
| --- | --- | --- |
| Below 60% | Normal | White |
| 60% to below 80% | Busy | Amber |
| 80% or higher | High load | Orange |
| 95% or higher for 10 continuous seconds | Critical | Red |

Samples selected resources once per second; the highest valid usage controls the color.
Hold Shift for 0.6 seconds to show the compact card without taking focus.
Release Shift to close, or move into the card to keep it open. Esc dismisses it.
The compact card uses Consolas, segmented meters and one top CPU app with its icon.
GPU and Memory stay system-level indicators rather than determining the top app.
Values are current samples, not a five-second average or a power-consumption score.
Usage and the top app can change at each refresh; there is no averaging or ranking hold.
An **Open Task Manager** action remains available.

## Run

Requires Windows and .NET 10 Desktop Runtime. Build with .NET 10 SDK.
The official WinUI package is restored by NuGet; Windows App SDK native runtime
files are bundled next to the executable. No administrator access, driver,
installer, background service or startup registration.

```powershell
Set-Location .\load-cursor
dotnet build -c Release
.\bin\Release\winui\PulseCursor.exe --settings
```

Exit an older running version from its tray menu before starting this version.
Run the **EXE**, not `dotnet PulseCursor.dll`: the EXE supplies the native activation manifest.
Without `--settings`, the app starts in the tray.

## Settings

- **Appearance:** System, Light or Dark. Both the card and settings follow this choice.
- **Resources:** CPU, GPU and Memory. Unchecked resources are not sampled.
  Uncheck all three to pause monitoring.
- **Sensitivity:** Sensitive (50/70/90), Balanced (60/80/95), Relaxed (70/88/97).
  Custom thresholds must satisfy `1 <= Busy < High load < Critical <= 100`.
- **Colors:** separate Normal, Busy, High load and Critical colors using native color pickers.
  Select a color swatch with its dropdown arrow to open the spectrum and RGB/HEX inputs.
  Each selector has the real cursor shape beside it. Editing a color updates
  that preview without changing the system cursor to a demonstration state.
- **Pulse ring at critical load:** optional slow rings around the tip after the critical delay.
  Respects the Windows animation preference. No wave or liquid animation.
- **Hold Shift for details:** enable or disable the popup.
- **Pause in full-screen apps:** restore the original arrow and stop sampling while the
  foreground app's client area covers a monitor. A normal maximized window is not full screen.
- **Pause in energy saver:** stop sampling and restore the original arrow when Windows reports energy saver.

Changes take effect immediately and persist atomically to
`%LOCALAPPDATA%\LoadCursor\settings.json`.
Rapid edits are coalesced for 300 ms; normal Exit flushes a pending save.
Invalid thresholds stay unsaved with an inline error. If saving fails, the app
reports the error; changes remain in memory until exit.

## Restore

Pause or Exit restores the captured arrow. High Contrast keeps the original arrow.
No Windows cursor theme or registry setting is saved. After a forced termination,
close the app and reload the saved Windows cursor scheme:

```powershell
.\bin\Release\winui\PulseCursor.exe --restore
```

## Scope and measurement

- Only the shared system arrow changes. Text, link, resize, busy and app-drawn cursors do not.
- Use a regular light-filled arrow, not an inverted/animated cursor theme.
- CPU is total processor time usage; Memory is physical memory usage. No temperatures or VRAM.
- GPU adds processes on the same adapter/engine, then uses the busiest engine.
  Missing GPU counters show N/A, never a fake zero.
- App ranking runs only while the card is open. A single process scan finds the top CPU app.
  CPU uses time deltas normalized by elapsed time and logical processor count.
  GPU activity and memory size do not enter this ranking.
  Processes are grouped by executable name, so unrelated instances with the same executable name
  may be combined. Unavailable process reads are reported in tooltips.
- CPU needs an initial sample interval. GPU counter failures remain unavailable, not zero.
- App icons come from the executable. Protected processes or missing icons use a generic icon.
- Friendly app names use executable metadata when available.
- Hovering an app window or taskbar icon does not select that app; the leader is system-wide.
- Light/Dark/High Contrast XAML resources are included. High Contrast uses system brushes.
- On Windows 11, the popup disables the extra DWM frame border so its XAML outline owns both themes.

## Build a distribution package

On Windows x64, install .NET 10 SDK, PowerShell 7 and Windows SDK with MakeAppx.
From the project directory:

```powershell
pwsh -File .\build-package.ps1 -Version 1.0.0.0
```

The script publishes Release with both .NET and Windows App SDK runtime files included,
copies dependency licenses/notices and package metadata, runs the self-test, generates
the package logos, and creates a validated, unsigned
`PulseCursor_1.0.0.0_x64.msix` under a new `artifacts\package-*` directory.
The SHA-256 hash and exact output path are printed at completion.
The installed display name, executable and file description are PulseCursor.
The source project, local settings directory and single-instance mutex retain their
original LoadCursor names to preserve existing settings and prevent concurrent versions.

The package identity is `Kenehong.PulseCursor`, with publisher `CN=kenehong`.
Keep both stable for updates and increment the four-part package version.
No signing key, certificate trust, Developer Mode setting, installation or publishing
action is performed by the script. Distribute only the final MSIX, not the staging
directory, logs or diagnostic screenshots.

Unsigned packages need a distribution/development workflow that supports them.
Direct production MSIX installation requires a signature trusted by the target device;
the signing certificate subject must match the manifest publisher. Never commit signing keys.
Test installation, launch, settings persistence, cursor restoration and upgrades on a
separate Windows device before broad distribution.

For folder-based distribution instead, share the complete self-contained `payload`
directory, not just its EXE. The normal `dotnet build` output still requires the
.NET 10 Desktop Runtime.

## Checks

```powershell
dotnet format --verify-no-changes
dotnet build -c Release
$check = Start-Process .\bin\Release\winui\PulseCursor.exe `
    -ArgumentList --self-test -Wait -PassThru `
    -RedirectStandardOutput .\bin\Release\winui\self-test.log `
    -RedirectStandardError .\bin\Release\winui\self-test-errors.log
$check.ExitCode
Get-Content .\bin\Release\winui\self-test.log
```

Exit code 0 means success. The self-test briefly shows its own UI, but does not
change the system arrow or the user's saved settings. It checks bindings, icons,
Light/Dark rendering and native frame updates, live cursor previews, narrow layout,
settings controls/persistence/validation, all four color pickers opening/editing/closing in
both themes, CPU-only app ranking, pause policy, thresholds, system GPU engine aggregation,
solid-color/alpha/pulse rendering and real metrics.
PNG evidence and temporary settings tests are written beneath
`%TEMP%\PulseCursor\self-test\<process-id>`, not into the application install directory.

For steady-state runtime measurements, exit the running app and launch the EXE with
`--benchmark`, redirecting stdout/stderr as above. It runs the real monitor with all
three resources selected, fullscreen/energy-saver pausing off and default thresholds.
Each stage warms up for 5 seconds and samples for 20 seconds: background, settings,
then the panel after hiding settings. The last stage includes the retained settings window.
JSON output reports CPU normalized across logical processors, average/min/max Working Set,
and private committed bytes in MiB. Private committed bytes are not physical RAM.
This mode briefly shows UI and changes the system arrow, restores it on normal exit,
and never saves its temporary settings. It is not a power/energy measurement.

For a native arrow-change/restore check, exit the running app first:

```powershell
Start-Process .\bin\Release\winui\PulseCursor.exe -ArgumentList --demo -Wait
```
