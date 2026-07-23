# Acer Battery Saver — PHN16-71

A lightweight Windows tray app tailored to the Acer Predator Helios Neo 16 PHN16-71.

The executable and notification-area icon use the custom transparent battery-and-leaf artwork in `assets/battery-saver-icon.png` and `assets/battery-saver.ico`.

## Install

Download and run `Acer-Battery-Saver-Setup-v1.0.0.exe` from the private GitHub
Release. It installs for the current Windows account, adds a Start Menu shortcut,
registers the app under Windows **Installed apps**, starts the tray app, and
enables startup with Windows. Administrator access is not required for the
installation itself.

An unsigned personal build may trigger Windows SmartScreen. The installer and
application can both be rebuilt from this repository using the included PowerShell
build scripts.

## Current battery profile

- Activates automatically when AC power is unplugged.
- Restores the previous state automatically when AC power returns.
- Starts with Windows by default so automatic switching is always available (toggleable from the tray menu).
- Leaves display brightness untouched.
- Enables native Windows Energy Saver at every battery level, using its aggressive policy.
- Keeps Energy Saver's brightness scaling at 100%, so it does not dim your already-low brightness.
- Changes the internal display from its current rate (normally 165 Hz) to 60 Hz.
- Creates a temporary, reversible Windows power plan with:
  - CPU maximum set to 55% on battery;
  - CPU boost disabled on battery;
  - passive cooling requested;
  - USB selective suspend enabled;
  - display timeout set to 3 minutes;
  - sleep timeout set to 10 minutes.
- Remembers whether a Bluetooth peripheral was connected while plugged in and checks again during activation. If either check finds a connection, Bluetooth stays on. Otherwise, it disables the adapter where Windows permissions allow it and restores it only if the app actually disabled it.
- Suspends only the background processes explicitly listed in `ProcessesToPause`.
- Writes a restore snapshot *before* changing anything, allowing Emergency restore after a crash.

Brightness and the keyboard 30-second timeout are intentionally not changed. Energy Saver is scoped to the temporary power plan, so restoring the original plan also restores its previous behavior. PredatorSense Eco, keyboard lighting and physical GPU MUX control require Acer's private service protocol and are not changed by this build; the app does not write undocumented embedded-controller values.

## Build and run

Right-click `build.ps1`, choose **Run with PowerShell**, then launch `AcerBatterySaver.exe`. Windows includes the compiler used by this build; no SDK is required.

To select background applications, use **Open settings** from the tray menu and add process names, for example:

```json
"ProcessesToPause":["Discord","SteamWebHelper"]
```

Save the file and restart the tray app. Do not add Windows services, antivirus, driver software, or the Acer utility processes.

Bluetooth device enable/disable normally requires Administrator permission. If Windows denies it, the app logs the failure and continues applying the rest of the profile.

## Recovery

Select **Emergency restore** from the tray menu. The saved original power-plan GUID and refresh rate are in `restore-state.json` while battery mode is active.
