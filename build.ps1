$ErrorActionPreference = 'Stop'
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows C# compiler was not found.' }
& $compiler /nologo /target:winexe /optimize+ /win32icon:assets\battery-saver.ico /out:AcerBatterySaver.exe /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll AcerBatterySaver.cs
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }
Write-Host 'Built AcerBatterySaver.exe'
