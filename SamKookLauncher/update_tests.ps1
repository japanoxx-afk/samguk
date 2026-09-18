param([Parameter(Mandatory=$true)][string]$BuildDirectory)
$ErrorActionPreference='Stop'
$src=$PSScriptRoot
$root=Join-Path $src ('runtime\update tests 한글 '+[guid]::NewGuid())
New-Item -ItemType Directory $root | Out-Null
$csc='C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $csc /nologo /target:winexe /out:"$root\new.exe" "$src\UpdateProbe.cs"
& $csc /nologo /target:winexe /define:OLD /out:"$root\old.exe" "$src\UpdateProbe.cs"
$asm=[Reflection.Assembly]::LoadFile((Join-Path $BuildDirectory 'SamKookLauncher.exe'))
$quote=$asm.GetType('SamKookFreeNet.LauncherForm').GetMethod('Quote',[Reflection.BindingFlags]'Static,NonPublic')
function Q([string]$value){$quote.Invoke($null,@($value))}
function Run([string]$exe,[string]$arguments){Start-Process -FilePath $exe -ArgumentList $arguments -PassThru -WindowStyle Hidden}
$p=Run "$root\new.exe" ('args '+(Q ($root+'\')))
if(!$p.WaitForExit(5000)){throw 'Argument probe timeout'}
if([IO.File]::ReadAllText("$root\args.txt") -ne ($root+'\')){throw 'Trailing slash quoting failed'}
foreach($scenario in @('success','invalid','locked','rollback')) {
 $target=Join-Path $root $scenario; $payload=Join-Path $root ($scenario+'-payload')
 New-Item -ItemType Directory $target,$payload | Out-Null
 Copy-Item "$root\old.exe" "$target\SamKookLauncher.exe"
 Copy-Item "$root\new.exe" "$payload\SamKookLauncher.exe"
 Copy-Item (Join-Path $BuildDirectory 'LauncherUpdater.exe') "$payload\LauncherUpdater.exe"
 if($scenario -ne 'invalid'){Copy-Item (Join-Path $BuildDirectory 'GameMonitor.exe') "$payload\GameMonitor.exe"}
 Copy-Item "$src\README.md" "$payload\README.md"
 Copy-Item "$src\README.md" "$target\README.md"
 if($scenario -eq 'locked'){Copy-Item (Join-Path $BuildDirectory 'GameMonitor.exe') "$target\GameMonitor.exe"}
 $zip=Join-Path $root ($scenario+'.zip'); Compress-Archive -Path "$payload\*" -DestinationPath $zip
 $ready=Join-Path $target 'ready'; $lock=$null
 $parent=Run "$root\old.exe" ('hold '+(Q $ready))
 try {
  if($scenario -eq 'rollback'){$lock=[IO.File]::Open("$target\README.md",'Open','Read','Read')}
  if($scenario -eq 'locked'){$lock=[IO.File]::Open("$target\GameMonitor.exe",'Open','Read','Read')}
  $helper=Run (Join-Path $BuildDirectory 'LauncherUpdater.exe') ((Q $zip)+' '+$parent.Id+' '+(Q ($target+'\'))+' '+(Q $ready)+' --silent')
  if(!$helper.WaitForExit(12000)){throw 'Updater timeout'}
  if($scenario -eq 'invalid' -or $scenario -eq 'locked') {
   if($parent.HasExited -or (Test-Path $ready)){throw 'Invalid ZIP let parent exit'}
   if($helper.ExitCode -ne 1){throw 'Invalid ZIP accepted'}
  } else {
   for($i=0;$i -lt 50 -and !(Test-Path "$target\restarted.txt");$i++){Start-Sleep -Milliseconds 100}
   $expected=if($scenario -eq 'success'){'new'}else{'old'}
   if([IO.File]::ReadAllText("$target\restarted.txt") -ne $expected){throw 'Wrong restarted executable'}
   if($scenario -eq 'rollback' -and (Get-FileHash "$target\SamKookLauncher.exe").Hash -ne (Get-FileHash "$root\old.exe").Hash){throw 'Rollback hash mismatch'}
  }
  "PASS updater $scenario"
 } finally { if($lock){$lock.Dispose()}; if(!$parent.HasExited){$parent.Kill();$parent.WaitForExit()} }
}
'PASS quoted path with spaces, Korean and trailing slash'
