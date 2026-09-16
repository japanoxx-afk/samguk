$ErrorActionPreference = 'Stop'
$csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
& $csc /nologo /target:winexe /optimize+ /out:"$here\SamKookLauncher.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Net.Http.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "$here\SamKookLauncher.cs"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $csc /nologo /target:winexe /optimize+ /out:"$here\LauncherUpdater.exe" /reference:System.dll /reference:System.Core.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "$here\LauncherUpdater.cs"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $csc /nologo /target:exe /optimize+ /out:"$here\ProtocolTests.exe" /reference:System.dll /reference:System.Core.dll "$here\ProtocolTests.cs"
exit $LASTEXITCODE
