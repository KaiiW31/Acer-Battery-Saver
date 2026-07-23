$ErrorActionPreference = 'Stop'
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows C# compiler was not found.' }

& $compiler /nologo /target:winexe /optimize+ `
    /win32icon:assets\battery-saver.ico `
    /out:Acer-Battery-Saver-Setup-v1.0.1.exe `
    /reference:System.dll `
    /reference:System.Windows.Forms.dll `
    "/resource:AcerBatterySaver.exe,AcerBatterySaver.Payload.exe" `
    "/resource:config.default.json,AcerBatterySaver.DefaultConfig.json" `
    Installer.cs

if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed with exit code $LASTEXITCODE" }
Write-Host 'Built Acer-Battery-Saver-Setup-v1.0.1.exe'
