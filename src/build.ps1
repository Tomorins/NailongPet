$ErrorActionPreference = 'Stop'
$petRoot = Split-Path -Parent $PSScriptRoot
$petCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $petCompiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 "/out:$petRoot\NailongPet.exe" "/win32icon:$PSScriptRoot\pet.ico" "/win32manifest:$PSScriptRoot\app.manifest" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/resource:$PSScriptRoot\sprites.png,sprites.png" "/resource:$PSScriptRoot\laugh.wav,laugh.wav" "/resource:$PSScriptRoot\pet.ico,pet.ico" "$PSScriptRoot\NailongPet.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built: $petRoot\NailongPet.exe"
