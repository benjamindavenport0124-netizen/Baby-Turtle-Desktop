param([switch]$Relaunch,[switch]$Headless)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Turtle.ps1') -Check
if($Headless) {
    $pet=New-Object TurtlePet
    try {
        $flags=[Reflection.BindingFlags]'Instance,NonPublic'
        $pet.GetType().GetMethod('RestartTurtle',$flags).Invoke($pet,@()) | Out-Null
        if(-not [TurtlePet]::RestartRequested) {throw 'Restart handoff missing.'}
        $pet.Dispose();$pet.Dispose()
        Write-Output 'PASS: restart handoff and repeated disposal without relaunch.'
    } finally {$pet.Dispose()}
    return
}
$timer=New-Object Windows.Forms.Timer
$timer.Interval=400
$timer.Add_Tick({
    $timer.Stop()
    $form=@([Windows.Forms.Application]::OpenForms | Where-Object {$_ -is [TurtlePet]})[0]
    $flags=[Reflection.BindingFlags]'Instance,NonPublic'
    $form.GetType().GetMethod('RestartTurtle',$flags).Invoke($form,@()) | Out-Null
})
$settings=Join-Path $PSScriptRoot 'restart-test.settings'
try {
    $before=@(Get-Process powershell,'Baby Turtle' -ErrorAction SilentlyContinue | ForEach-Object {$_.Id})
    $timer.Start()
    if($Relaunch) {& (Join-Path $PSScriptRoot 'Turtle.ps1')}
    else {[TurtlePet]::Run($settings)}
    if(-not [TurtlePet]::RestartRequested) {throw 'Restart was not requested.'}
    if($Relaunch) {
        Start-Sleep -Seconds 3
        $replacement=@(Get-Process powershell,'Baby Turtle' -ErrorAction SilentlyContinue | Where-Object {$before -notcontains $_.Id})
        if($replacement.Count -ne 1) {throw 'Expected one fresh turtle process after restart.'}
        Write-Output "PASS: real launcher restarted into running process $($replacement[0].Id)."
    }
    Write-Output 'PASS: live restart exits its message loop and disposes cleanly.'
} finally {$timer.Dispose();if(Test-Path -LiteralPath $settings) {Remove-Item -LiteralPath $settings}}
