param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if(!$OutputDirectory) { $OutputDirectory=$here }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
& $csc /nologo /target:winexe /optimize+ /out:"$OutputDirectory\SamKookLauncher.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Net.Http.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "$here\SamKookLauncher.cs" "$here\QueuePatch.cs" "$here\LegacyHash.cs"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $csc /nologo /target:winexe /optimize+ /out:"$OutputDirectory\LauncherUpdater.exe" /reference:System.dll /reference:System.Core.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "$here\LauncherUpdater.cs"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $csc /nologo /target:winexe /platform:x86 /optimize+ /out:"$OutputDirectory\GameMonitor.exe" /reference:System.dll "$here\GameMonitor.cs"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $csc /nologo /target:exe /optimize+ /out:"$OutputDirectory\ProtocolTests.exe" /reference:System.dll /reference:System.Core.dll "$here\AuthTests.cs"
exit $LASTEXITCODE
