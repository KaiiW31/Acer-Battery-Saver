# Portable installation

1. Download `Acer-Battery-Saver-v1.0.0-win-x64.zip`.
2. Extract the ZIP to a permanent folder such as:
   `Documents\My Apps\Acer Battery Saver`
3. Run `AcerBatterySaver.exe`.
4. Leave **Start with Windows** enabled in the tray menu.

Do not run the executable directly from inside the ZIP or move it after enabling
startup. If you move it, launch it from the new location and toggle **Start with
Windows** off and on again.

Windows may show a SmartScreen warning because this personal build is not
digitally signed. Review the private repository and build the source with
`build.ps1` if preferred.

Bluetooth adapter switching can require Administrator permission on some
Windows installations. The remaining battery-saving features continue to work
if Windows refuses that operation.
