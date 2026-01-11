# Gamma Manager

A small Windows console utility that temporarily boosts display gamma using the GDI gamma ramp. 
It lets you toggle a higher "boosted" gamma, restore the previous or default gamma, and fine‑tune the boost value at runtime. 
Input is handled by a lightweight poller so mouse buttons and keys that can't be registered with WM_HOTKEY also work.

## Features
- Read and apply system gamma via GDI ramps.
- Toggle between the current/previous gamma and a configurable boosted gamma.
- Increase/decrease the boosted gamma at runtime.
- Saves settings to `gamma_config.json`.
- Simple, colorized console UI and a tray indicator.
- Poller-based input handling (works with mouse buttons and F-keys).

## Requirements
- Windows
- .NET 10
- Visual Studio (optional) for development

## Build & Run

Using dotnet CLI:

dotnet build dotnet run --project .

Using Visual Studio:
1. Open the project/solution in Visual Studio.
2. Build the solution using __Build > Build Solution__.
3. Run with __Debug > Start Without Debugging__ or __Start Debugging__.

The application runs as a console app and keeps running until you close it (Ctrl+C). Background threads keep the tray icon and input poller active.

## First run & configuration
On first run the app will create `gamma_config.json` if it doesn't exist and then:

- Prompt you to press a key (or mouse button) to set the Toggle binding when `ToggleKey` is unset.
- Use defaults for other keys when unset: `ResetKey` = `F7`, `IncreaseKey` = `F8`, `DecreaseKey` = `F9`.
- Prompt for initial `BoostedGamma` (press Enter to accept the default `2.3`).
- `DefaultGamma` is initially set to `1.0` in code but will be overwritten with the detected system gamma if readable on first run. `PreviousGamma` is used to restore the gamma saved before a boost.

{ "ToggleKey": "F1", "ResetKey": "F7", "IncreaseKey": "F8", "DecreaseKey": "F9", "DefaultGamma": 1.0, "BoostedGamma": 2.3, "PreviousGamma": 1.0 }

### Example `gamma_config.json`

json
{
  "ToggleKey": "F7",
  "ResetKey": "F8",
  "IncreaseKey": "F9",
  "DecreaseKey": "F10",
  "BoostedGamma": 2.3,
  "DefaultGamma": 1.0
}

Notes:
- Values are sanitized on save (NaN/Infinity replaced with safe defaults).
- `DefaultGamma` will reflect the system gamma after first successful read.

## Key mapping rules
The app resolves key names in this order:
1. Matches the internal `Keys` enum (covers mouse buttons, numpad, F1–F12, A–Z, 0–9).
2. Tries `ConsoleKey` if the above doesn't match.
3. Single-character strings map to their ASCII/VK values (A–Z, 0–9).
4. Unknown or invalid names are treated as unset.

When prompted for the Toggle key, press the desired key or mouse button; the app debounces and reports the selection.

## Usage
- Toggle boost: press the configured Toggle key.
- Reset to default gamma: press the Reset key (default F7).
- Increase / Decrease boosted gamma: press Increase / Decrease keys (defaults F8 / F9). If the boost is active, changes apply immediately.

The console displays a startup summary and writes periodic status updates.