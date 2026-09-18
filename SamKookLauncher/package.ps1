param([Parameter(Mandatory=$true)][string]$BuildDirectory,[Parameter(Mandatory=$true)][string]$Version)
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid release version'}
$files=@('SamKookLauncher.exe','LauncherUpdater.exe','GameMonitor.exe','cnc-ddraw.dll','cnc-ddraw-LICENSE.txt')
$paths=@($files | ForEach-Object { Join-Path $BuildDirectory $_ })
foreach($path in $paths){if(!(Test-Path -LiteralPath $path)){throw "Missing release file: $path"}}
if((Get-Item -LiteralPath $paths[0]).VersionInfo.FileVersion -ne ($Version+'.0')){throw 'Launcher version mismatch'}
if((Get-FileHash -LiteralPath (Join-Path $BuildDirectory 'cnc-ddraw.dll')).Hash -ne '85E0F7D530DFDA134793A57CB3E76B0287DCC96892EE57162DD68F47283B03A9'){throw 'Graphics module hash mismatch'}
$paths+=Join-Path $PSScriptRoot 'README.md'
$output=Join-Path $PSScriptRoot ('dist\SamKookLauncher-'+$Version+'.zip')
Compress-Archive -LiteralPath $paths -DestinationPath $output
Write-Output $output
