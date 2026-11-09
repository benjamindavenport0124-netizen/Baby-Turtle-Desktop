$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Turtle.ps1') -Check
$flags=[Reflection.BindingFlags]'Instance,NonPublic,DeclaredOnly'
function Field($o,$name) {$o.GetType().GetField($name,$flags)}
function Call($o,$name,$values) {
    $argsForMethod=[object[]]@($values|ForEach-Object {$_.PSObject.BaseObject})
    $o.GetType().GetMethod($name,$flags).Invoke($o,$argsForMethod)
    # Keep route tests deterministic; slip coverage below explicitly expires this timer.
    if($name -eq 'BeginClimb') {(Field $o 'slipRemaining').SetValue($o,[double]::PositiveInfinity)}
}
function Assert($value,$message) {if(-not $value) {throw $message}}
$away=New-Object Drawing.Point(-10000,-10000)
$pet=New-Object TurtlePet
try {
    $pet.LoadModel()
    Call $pet 'StartLandingDust' @([double]40) | Out-Null
    $smallCount=(Field $pet 'landingDustCount').GetValue($pet)
    $smallDuration=(Field $pet 'landingDustDuration').GetValue($pet)
    $smallStrength=(Field $pet 'landingDustStrength').GetValue($pet)
    Call $pet 'StartLandingDust' @([double]550) | Out-Null
    Assert ((Field $pet 'landingDustCount').GetValue($pet) -gt $smallCount) 'Higher falls should produce more dust particles.'
    Assert ((Field $pet 'landingDustDuration').GetValue($pet) -gt $smallDuration) 'Higher falls should produce longer dust puffs.'
    Assert ((Field $pet 'landingDustStrength').GetValue($pet) -gt $smallStrength) 'Higher falls should spread larger clouds.'
    Call $pet 'StartLandingDust' @([double]100000) | Out-Null
    Assert ((Field $pet 'landingDustCount').GetValue($pet) -le 18) 'Extreme fall heights should keep dust bounded.'
    (Field $pet 'reducedMotion').SetValue($pet,$true)
    Call $pet 'StartLandingDust' @([double]550) | Out-Null
    Assert ((Field $pet 'landingDust').GetValue($pet) -eq 0) 'Reduced motion should suppress landing puffs at any height.'
    (Field $pet 'reducedMotion').SetValue($pet,$false)
    Assert (-not (Field $pet 'climbAndTumble').GetValue($pet)) 'Climbing must be opt-in.'
    Call $pet 'BeginClimb' @() | Out-Null
    Assert ($pet.CurrentState -eq 'idle') 'Disabled climbing should not start.'
    (Field $pet 'climbAndTumble').SetValue($pet,$true)
    Call $pet 'BeginClimb' @() | Out-Null
    Assert ($pet.CurrentState -eq 'climbApproach') 'Enabled turtle should approach a screen edge.'
    $side=(Field $pet 'climbSide').GetValue($pet)
    (Field $pet 'yaw').SetValue($pet,[double](-$side*1.15))
    (Field $pet 'interest').SetValue($pet,[double]1)
    $approachX=(Field $pet 'px').GetValue($pet)
    $near=New-Object Drawing.Point(210,160)
    Call $pet 'StepAt' @([double]0.05,$near,[uint32]0) | Out-Null
    Assert ([Math]::Abs((Field $pet 'px').GetValue($pet)-$approachX) -lt 0.001) 'Facing away must turn before moving toward the wall.'
    Assert ((Field $pet 'yaw').GetValue($pet)*$side -gt -1.15) 'The approach must turn toward its destination even with mouse interest.'
    for($i=0;$i -lt 500 -and $pet.CurrentState -eq 'climbApproach';$i++) {Call $pet 'StepAt' @([double]0.05,$away,[uint32]0) | Out-Null}
    Assert ($pet.CurrentState -eq 'climb') 'Edge approach should transition into climbing.'
    Assert ([Math]::Abs((Field $pet 'yaw').GetValue($pet)-$side*2.65) -lt .12) 'Climbing must wait until he faces the wall.'
    $startY=(Field $pet 'py').GetValue($pet)
    1..30 | ForEach-Object {Call $pet 'StepAt' @([double]0.05,$away,[uint32]0) | Out-Null}
    Assert ((Field $pet 'py').GetValue($pet) -lt $startY-20) 'The turtle should move upward during a climb.'
    Call $pet 'SlipFromEdge' @() | Out-Null
    $slipY=(Field $pet 'py').GetValue($pet)
    Assert ($pet.CurrentState -eq 'tipBack') 'A slip must tip backward before it starts dropping.'
    $startPitch=(Field $pet 'backPitch').GetValue($pet)
    Call $pet 'StepAt' @([double]0.04,$away,[uint32]0) | Out-Null
    Assert ([Math]::Abs((Field $pet 'py').GetValue($pet)-$slipY) -lt 0.001) 'The turtle should stay at its height while tipping onto its back.'
    Assert ((Field $pet 'backPitch').GetValue($pet) -gt $startPitch) 'The first phase should tip backward.'
    for($i=0;$i -lt 50 -and $pet.CurrentState -eq 'tipBack';$i++) {Call $pet 'StepAt' @([double]0.02,$away,[uint32]0) | Out-Null}
    Assert ($pet.CurrentState -eq 'tumble') 'Only after tipping onto its back should it begin falling.'
    Assert ([Math]::Abs((Field $pet 'backPitch').GetValue($pet)-[Math]::PI) -lt 0.001) 'The turtle must already be on its back at the start of the drop.'
    $facing=(Field $pet 'yaw').GetValue($pet)
    Call $pet 'StepAt' @([double]0.04,$away,[uint32]0) | Out-Null
    Assert ((Field $pet 'py').GetValue($pet) -gt $slipY) 'The second phase should descend.'
    Assert ([Math]::Abs((Field $pet 'backPitch').GetValue($pet)-[Math]::PI) -lt 0.001) 'Falling must hold the back-down pose without rotating.'
    Assert ([Math]::Abs((Field $pet 'yaw').GetValue($pet)-$facing) -lt 0.001) 'The view must not spin during the drop.'
    for($i=0;$i -lt 300 -and $pet.CurrentState -eq 'tumble';$i++) {Call $pet 'StepAt' @([double]0.02,$away,[uint32]0) | Out-Null}
    Assert ($pet.CurrentState -eq 'onBack') 'A tumble should land on the turtle back.'
    Assert ((Field $pet 'landingDust').GetValue($pet) -gt 0) 'Landing should trigger a brief dust puff.'
    Assert ([Math]::Abs((Field $pet 'backPitch').GetValue($pet)-[Math]::PI) -lt 0.001) 'The landed turtle must be upside down.'
    $area=(Field $pet 'climbArea').GetValue($pet)
    Assert ([Math]::Abs((Field $pet 'py').GetValue($pet)-($area.Bottom-$pet.Height)) -lt 1) 'The tumble should land at the desktop floor.'
    1..20 | ForEach-Object {Call $pet 'StepAt' @([double]0.05,$away,[uint32]0) | Out-Null}
    Assert ($pet.CurrentState -eq 'onBack') 'The turtle should wait for help rather than reset itself.'
    Assert ((Field $pet 'landingDust').GetValue($pet) -eq 0) 'Landing dust should clear while the turtle waits for help.'
    $busy=New-Object CodexActivitySnapshot
    $busy.ThreadId='adventure-test';$busy.Connected=$true;$busy.Phase='working'
    Call $pet 'ApplyCodex' @($busy) | Out-Null
    Assert ($pet.CurrentState -eq 'onBack') 'Assistant activity must not magically right the turtle.'
    (Field $pet 'preferLinkedInput').SetValue($pet,$true)
    (Field $pet 'pressed').SetValue($pet,$true)
    $release=New-Object Windows.Forms.MouseEventArgs([Windows.Forms.MouseButtons]::Left,1,0,0,0)
    Call $pet 'OnPointerUp' @($pet,$release) | Out-Null
    Assert ($pet.CurrentState -eq 'helpUp') 'Clicking an upside-down turtle should help it up.'
    Assert ($null -eq (Field $pet 'inputFocus').GetValue($pet)) 'Helping must not trigger dictation.'
    $helpFacing=(Field $pet 'yaw').GetValue($pet)
    1..13 | ForEach-Object {Call $pet 'StepAt' @([double]0.05,$away,[uint32]0) | Out-Null}
    Assert ([Math]::Abs((Field $pet 'backPitch').GetValue($pet)-[Math]::PI) -lt .001) 'Helping should roll sideways rather than reverse the backward pitch.'
    1..14 | ForEach-Object {Call $pet 'StepAt' @([double]0.05,$away,[uint32]0) | Out-Null}
    Assert ([Math]::Abs((Field $pet 'yaw').GetValue($pet)-$helpFacing-[Math]::PI) -lt .001) 'Side recovery should preserve its final heading without a snap.'
    1..8 | ForEach-Object {Call $pet 'StepAt' @([double]0.05,$away,[uint32]0) | Out-Null}
    Assert ([Math]::Abs((Field $pet 'backPitch').GetValue($pet)) -lt 0.001) 'Recovery should finish upright.'
    Call $pet 'RefreshAssistantPose' @() | Out-Null
    Assert ($pet.CurrentState -eq 'working') 'Assistant work should resume after helping.'
    $busy=$busy.Copy();$busy.Connected=$false;$busy.Phase='idle';Call $pet 'ApplyCodex' @($busy) | Out-Null
    Call $pet 'ResetAdventure' @($area) | Out-Null
    Call $pet 'BeginClimb' @() | Out-Null
    Call $pet 'SetClimbing' @($false) | Out-Null
    Assert ($pet.CurrentState -eq 'climbDown') 'Disabling climbing should start a safe descent.'
    1..40 | ForEach-Object {Call $pet 'StepAt' @([double]0.05,$away,[uint32]0) | Out-Null}
    Assert (-not (Call $pet 'AdventureActive' @())) 'Disabling should leave no stuck adventure state.'
    (Field $pet 'climbAndTumble').SetValue($pet,$true)
    foreach($routeSide in @(-1,1)) {
        Call $pet 'ResetAdventure' @($area) | Out-Null
        Call $pet 'BeginClimb' @() | Out-Null
        (Field $pet 'climbSide').SetValue($pet,[int]$routeSide)
        (Field $pet 'yaw').SetValue($pet,[double]($routeSide*2.65))
        (Field $pet 'climbAmount').SetValue($pet,[double]1)
        (Field $pet 'py').SetValue($pet,[double]($area.Top-200))
        Call $pet 'SetState' @('climb') | Out-Null
        Call $pet 'StepAt' @([double]0.05,$away,[uint32]0) | Out-Null
        Assert ($pet.CurrentState -eq 'climbTop') 'Normal climbing should reach the top instead of randomly falling.'
        Assert ((Field $pet 'topDestinationSide').GetValue($pet) -eq -$routeSide) 'Top traversal should target the opposite edge.'
        for($i=0;$i -lt 5000 -and $pet.CurrentState -eq 'climbTop';$i++) {Call $pet 'StepAt' @([double]0.05,$away,[uint32]0) | Out-Null}
        Assert ($pet.CurrentState -eq 'climbDown') 'Crossing the top should lead into descending the opposite side.'
        Assert ((Field $pet 'climbSide').GetValue($pet) -eq -$routeSide) 'The descent should use the opposite wall.'
        1..30 | ForEach-Object {Call $pet 'StepAt' @([double]0.05,$away,[uint32]0) | Out-Null}
        Assert ([Math]::Abs((Field $pet 'climbAmount').GetValue($pet)) -lt .06) 'Descent should keep the shell facing the viewer.'
        Assert ((Field $pet 'descentHang').GetValue($pet) -gt .94) 'Descent should rotate headfirst in the viewer plane.'
        for($i=0;$i -lt 2000 -and (Call $pet 'AdventureActive' @());$i++) {Call $pet 'StepAt' @([double]0.05,$away,[uint32]0) | Out-Null}
        Assert (-not (Call $pet 'AdventureActive' @())) 'The full screen route should finish safely on the floor.'
    }
    foreach($interrupt in @('assistant','disabled')) {
        Call $pet 'BeginClimb' @() | Out-Null
        Call $pet 'SetState' @('climbTop') | Out-Null
        (Field $pet 'topExit').SetValue($pet,$false)
        (Field $pet 'topDestinationSide').SetValue($pet,[int]1)
        (Field $pet 'px').SetValue($pet,[double]($area.Left-$pet.Width/2+100))
        (Field $pet 'py').SetValue($pet,[double]($area.Top-100))
        if($interrupt -eq 'assistant') {$busy.Connected=$true;$busy.Phase='working';Call $pet 'ApplyCodex' @($busy) | Out-Null}
        else {Call $pet 'SetClimbing' @($false) | Out-Null}
        Call $pet 'StepAt' @([double]0.05,$away,[uint32]0) | Out-Null
        Assert ((Field $pet 'topExit').GetValue($pet)) 'Either interruption should exit the top route.'
        Assert ((Field $pet 'topDestinationSide').GetValue($pet) -eq -1) 'An interrupted top route should turn back to the nearest wall.'
        for($i=0;$i -lt 3000 -and (Call $pet 'AdventureActive' @());$i++) {Call $pet 'StepAt' @([double]0.05,$away,[uint32]0) | Out-Null}
        Assert (-not (Call $pet 'AdventureActive' @())) 'Interrupted top traversal should reach the ground.'
        $busy.Connected=$false;$busy.Phase='idle';Call $pet 'ApplyCodex' @($busy) | Out-Null
        (Field $pet 'climbAndTumble').SetValue($pet,$true)
    }
    foreach($slipState in @('climb','climbTop','climbDown')) {
        Call $pet 'ResetAdventure' @($area) | Out-Null
        Call $pet 'BeginClimb' @() | Out-Null
        Call $pet 'SetState' @($slipState) | Out-Null
        (Field $pet 'py').SetValue($pet,[double]($area.Bottom-$pet.Height-150))
        (Field $pet 'climbAmount').SetValue($pet,[double]$(if($slipState -eq 'climbTop') {1/.45} elseif($slipState -eq 'climbDown') {-1} else {1}))
        (Field $pet 'slipRemaining').SetValue($pet,[double]0)
        Call $pet 'StepAt' @([double]0.05,$away,[uint32]0) | Out-Null
        Assert ($pet.CurrentState -eq 'tipBack') "A grip loss should trigger a fall from $slipState."
        for($i=0;$i -lt 150 -and $pet.CurrentState -ne 'onBack';$i++) {Call $pet 'StepAt' @([double]0.05,$away,[uint32]0) | Out-Null}
        Assert ($pet.CurrentState -eq 'onBack') "A fall from $slipState should land on the shell."
    }
    Call $pet 'ResetAdventure' @($area) | Out-Null
    (Field $pet 'reducedMotion').SetValue($pet,$true)
    Call $pet 'SetState' @('idle') | Out-Null;Call $pet 'BeginClimb' @() | Out-Null
    Assert ($pet.CurrentState -eq 'idle') 'Reduced motion must suppress automatic climbing.'
    foreach($dragState in @('climbTop','climbDown')) {
        Call $pet 'SetState' @($dragState) | Out-Null
        foreach($name in @('climbAmount','backPitch','topHang','descentHang','fallHang')) {(Field $pet $name).SetValue($pet,[double]1)}
        (Field $pet 'pressed').SetValue($pet,$true)
        (Field $pet 'dragging').SetValue($pet,$false)
        (Field $pet 'pressPoint').SetValue($pet,$away)
        $move=New-Object Windows.Forms.MouseEventArgs([Windows.Forms.MouseButtons]::Left,0,0,0,0)
        $pet.GetType().GetMethod('OnPointerMove',$flags).Invoke($pet,@($null,$move.PSObject.BaseObject)) | Out-Null
        Assert ($pet.CurrentState -eq 'carried') 'Dragging must interrupt climbing.'
        foreach($name in @('climbAmount','backPitch','topHang','descentHang','fallHang')) {Assert ((Field $pet $name).GetValue($pet) -eq 0) "Dragging must clear $name."}
        Assert ((Call $pet 'HangingAngle' @()) -eq 0) 'Carried turtle must be upright.'
        $pet.GetType().GetMethod('OnPointerUp',$flags).Invoke($pet,@($null,$move.PSObject.BaseObject)) | Out-Null
        Assert ($pet.CurrentState -eq 'drop' -and (Call $pet 'HangingAngle' @()) -eq 0) 'Drop must remain upright.'
        Call $pet 'SetState' @('idle') | Out-Null
        Assert ((Call $pet 'HangingAngle' @()) -eq 0) 'Idle must not retain the climbing rotation.'

    }
    Write-Output 'PASS: opt-in, edge approach, upward climb, tumble landing, waits for help, click recovery without dictation, busy priority, safe disable, reduced motion.'
} finally {$pet.Dispose()}
