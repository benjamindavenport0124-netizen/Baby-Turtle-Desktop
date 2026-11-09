$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path (Join-Path $PSScriptRoot 'src\TurtleApp.cs') -ReferencedAssemblies @('System','System.Core','System.Windows.Forms',[Management.Automation.PowerShell].Assembly.Location)
$type=([AppDomain]::CurrentDomain.GetAssemblies() | ForEach-Object {$_.GetType('TurtleApp',$false)} | Where-Object {$_} | Select-Object -First 1)
$root=Join-Path ([IO.Path]::GetTempPath()) ('turtle-startup-test-'+[Guid]::NewGuid().ToString('N'))
$log=Join-Path $root 'turtle-startup.log'
[void][IO.Directory]::CreateDirectory($log)
try {
    $flags=[Reflection.BindingFlags]'Static,NonPublic'
    $type.GetMethod('Log',$flags).Invoke($null,@($root.PSObject.BaseObject,'diagnostic',$false)) | Out-Null
    $script:reported=''
    $show=[Action[string]]{param($message) $script:reported=$message}
    $type.GetMethod('ReportStartupError',$flags).Invoke($null,@($root.PSObject.BaseObject,(New-Object Exception('Useful startup error')).PSObject.BaseObject,$show.PSObject.BaseObject)) | Out-Null
    if($script:reported -ne 'Useful startup error') {throw 'Logging failure hid the startup error.'}
    Write-Output 'PASS: unwritable log destination does not prevent startup error reporting.'
} finally {[IO.Directory]::Delete($log);[IO.Directory]::Delete($root)}

