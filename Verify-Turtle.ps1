$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName WindowsBase
$uiaReferences = @([System.Windows.Automation.AutomationElement].Assembly.Location, [System.Windows.Automation.ControlType].Assembly.Location, [System.Windows.Rect].Assembly.Location)
Add-Type -Path @((Join-Path $PSScriptRoot 'src\TurtlePet.cs'), (Join-Path $PSScriptRoot 'src\TurtleGeometry.cs'), (Join-Path $PSScriptRoot 'src\CodexActivity.cs'),(Join-Path $PSScriptRoot 'src\DictationTarget.cs'),(Join-Path $PSScriptRoot 'src\DesktopScreenshot.cs'),(Join-Path $PSScriptRoot 'src\ChatGptActivity.cs'),(Join-Path $PSScriptRoot 'src\TurtleAdventure.cs')) -ReferencedAssemblies (@("System.Windows.Forms","System.Drawing","System.Web.Extensions","System.Core") + $uiaReferences)
[TurtlePet]::AssetDirectory = Join-Path $PSScriptRoot 'assets'
$flags = [System.Reflection.BindingFlags]'Instance,NonPublic,DeclaredOnly'
function Field($object, $name) { $object.GetType().GetField($name, $flags) }
function Call($object, $name, $arguments) {
    $values = [object[]]@($arguments | ForEach-Object { $_.PSObject.BaseObject })
    $object.GetType().GetMethod($name, $flags).Invoke($object, $values)
}
function Assert($condition, $message) { if (-not $condition) { throw $message } }
$pet = New-Object TurtlePet
try {
    $pet.LoadModel()
    Assert ($pet.TriangleCount -gt 200) 'Reference model geometry did not load.'
    # Regression: every part must move as one rigid object, including its eyes.
    $mesh = (Field $pet 'mesh').GetValue($pet)
    $parts = @($mesh | ForEach-Object { $_.part } | Sort-Object -Unique)
    Assert ($parts.Count -eq 6) 'Expected a shell, head, and four independent flippers.'
    # Regression: a nearer surface wins even when the farther triangle is submitted last.
    Call $pet 'PrepareRaster' @() | Out-Null
    $vertexType = $pet.GetType().GetNestedType('V', [System.Reflection.BindingFlags]::NonPublic)
    function Vertex([single]$x, [single]$y, [single]$z) { [Activator]::CreateInstance($vertexType, @($x,$y,$z)) }
    $nearColor = [System.Drawing.Color]::Lime.ToArgb()
    $farColor = [System.Drawing.Color]::Red.ToArgb()
    Call $pet 'RasterTriangle' @((Vertex 20 20 0.9),(Vertex 70 20 0.9),(Vertex 20 70 0.9),$nearColor) | Out-Null
    Call $pet 'RasterTriangle' @((Vertex 20 20 0.1),(Vertex 70 20 0.1),(Vertex 20 70 0.1),$farColor) | Out-Null
    $pixels = (Field $pet 'rasterPixels').GetValue($pet)
    Assert ($pixels[30*$pet.Width+30] -eq $nearColor) 'Farther surface incorrectly covered a nearer surface.'
    foreach ($pose in @('walk','wave','dance','curious','carried','sleep','settle','greet')) {
        Call $pet 'SetState' @($pose) | Out-Null
        (Field $pet 'stateTime').SetValue($pet, [double]1.2)
        (Field $pet 'headYaw').SetValue($pet, [double]0.20)
        (Field $pet 'headPitch').SetValue($pet, [double]0.12)
        (Field $pet 'walkAmount').SetValue($pet, [double]1)
        foreach ($part in $parts) {
            $vertices = @($mesh | Where-Object { $_.part -eq $part } | ForEach-Object { $_.a; $_.b; $_.c })
            $a = $vertices[0]; $b = $vertices[$vertices.Count - 1]
            $pa = Call $pet 'Pose' @($a, [int]$part)
            $pb = Call $pet 'Pose' @($b, [int]$part)
            $before = [Math]::Sqrt([Math]::Pow($a.x-$b.x,2)+[Math]::Pow($a.y-$b.y,2)+[Math]::Pow($a.z-$b.z,2))
            $after = [Math]::Sqrt([Math]::Pow($pa.x-$pb.x,2)+[Math]::Pow($pa.y-$pb.y,2)+[Math]::Pow($pa.z-$pb.z,2))
            Assert ([Math]::Abs($before-$after) -lt 0.00001) "Part $part distorted in $pose"
        }
    }
    $area = [System.Windows.Forms.Screen]::FromControl($pet).WorkingArea
    $startX = [double]($area.Left + ($area.Width - $pet.Width) / 2)
    (Field $pet 'px').SetValue($pet, $startX)
    (Field $pet 'py').SetValue($pet, [double]($area.Bottom - $pet.Height))
    Call $pet 'SetState' @('walk') | Out-Null
    (Field $pet 'targetX').SetValue($pet, [double]($startX - 80))
    1..120 | ForEach-Object { Call $pet 'StepAt' @([double](1/60), (New-Object System.Drawing.Point(-10000,-10000)), [uint32]0) | Out-Null }
    $afterX = (Field $pet 'px').GetValue($pet)
    Assert ($afterX -lt $startX - 20) 'Walking failed to move toward its target.'
    Assert ($afterX -ge $area.Left) 'Walking escaped screen bounds.'
    (Field $pet 'py').SetValue($pet, [double]($area.Bottom - $pet.Height - 150))
    (Field $pet 'vy').SetValue($pet, [double]0)
    Call $pet 'SetState' @('drop') | Out-Null
    1..90 | ForEach-Object { Call $pet 'StepAt' @([double](1/60), (New-Object System.Drawing.Point(-10000,-10000)), [uint32]0) | Out-Null }
    Assert ([Math]::Abs((Field $pet 'py').GetValue($pet) - ($area.Bottom - $pet.Height)) -lt 1) 'Drop did not land at the work area floor.'
    Assert ($pet.CurrentState -eq 'settle') 'Drop failed to transition into settling.'
    # Hover pauses walking, greets after a short dwell, and respects the cooldown.
    $pet.RenderPreview((Join-Path $PSScriptRoot 'previews\hover-check.png'), 'idle', 1.2)
    $pixels = (Field $pet 'rasterPixels').GetValue($pet)
    $occupied = @(for ($i=0; $i -lt $pixels.Length; $i++) { if ($pixels[$i] -ne 0) { $i } })
    $pixel = $occupied[[int]($occupied.Count / 2)]
    $point = New-Object System.Drawing.Point(($pixel % $pet.Width), [int][Math]::Floor($pixel / $pet.Width))
    # Rescue rolls may leave the same heading represented by several full turns.
    # Hover must not turn an equivalent heading into a multi-revolution spin.
    $turnPet=New-Object TurtlePet
    try {
        $turnPet.LoadModel()
        $turnPet.RenderPreview((Join-Path $PSScriptRoot 'previews\turn-check.png'),'idle',1.2)
        $wrappedHeading=6*[Math]::PI-0.30
        (Field $turnPet 'yaw').SetValue($turnPet,[double]$wrappedHeading)
        (Field $turnPet 'restYaw').SetValue($turnPet,[double]$wrappedHeading)
        (Field $turnPet 'interest').SetValue($turnPet,[double]1)
        (Field $turnPet 'greetedThisVisit').SetValue($turnPet,$true)
        1..20 | ForEach-Object {Call $turnPet 'StepAt' @([double]0.05,$point,[uint32]0) | Out-Null}
        Assert ([Math]::Abs((Field $turnPet 'yaw').GetValue($turnPet)-$wrappedHeading) -lt 0.02) 'Hover must not spin through accumulated full turns.'
        (Field $turnPet 'yaw').SetValue($turnPet,[double]3.10)
        (Field $turnPet 'restYaw').SetValue($turnPet,[double]3.10)
        (Field $turnPet 'interest').SetValue($turnPet,[double]1)
        Call $turnPet 'StepAt' @([double]0.05,$point,[uint32]0) | Out-Null
        Assert ((Field $turnPet 'yaw').GetValue($turnPet) -gt 3.10) 'Hover must take the shortest turn across the angle boundary.'
        $turnStart=(Field $turnPet 'px').GetValue($turnPet)
        (Field $turnPet 'yaw').SetValue($turnPet,[double](6*[Math]::PI+1.15))
        Call $turnPet 'SetState' @('walk') | Out-Null
        (Field $turnPet 'targetX').SetValue($turnPet,[double]($turnStart-60))
        $away=New-Object Drawing.Point(-10000,-10000)
        1..120 | ForEach-Object {Call $turnPet 'StepAt' @([double]0.05,$away,[uint32]0) | Out-Null}
        Assert ((Field $turnPet 'px').GetValue($turnPet) -lt $turnStart-5) 'Wrapped headings must still allow walking after turning.'
    } finally {$turnPet.Dispose()}
    Call $pet 'SetState' @('walk') | Out-Null
    (Field $pet 'time').SetValue($pet, [double]20)
    Call $pet 'UpdateInterest' @($point, [double]0.10) | Out-Null
    Assert ($pet.CurrentState -eq 'idle') 'Hover should stop the turtle moving under the pointer.'
    Call $pet 'UpdateInterest' @($point, [double]0.11) | Out-Null
    Assert ($pet.CurrentState -eq 'greet') "Hover dwell did not trigger a greeting: state=$($pet.CurrentState), hover=$((Field $pet 'hover').GetValue($pet)), duration=$((Field $pet 'hoverDuration').GetValue($pet)), last=$((Field $pet 'lastGreeting').GetValue($pet)), point=$point"
    Call $pet 'SetState' @('idle') | Out-Null
    (Field $pet 'time').SetValue($pet, [double]21)
    Call $pet 'UpdateInterest' @($point, [double]0.25) | Out-Null
    Assert ($pet.CurrentState -eq 'idle') 'Holding the pointer should not repeat greetings.'
    (Field $pet 'time').SetValue($pet, [double]30)
    Call $pet 'UpdateInterest' @($point, [double]9) | Out-Null
    Assert ($pet.CurrentState -eq 'idle') 'A parked pointer should not repeat a greeting after the cooldown.'
    $corner = New-Object System.Drawing.Point(0,0)
    Call $pet 'UpdateInterest' @($corner, [double]0.10) | Out-Null
    Assert (-not (Field $pet 'hover').GetValue($pet)) 'Transparent window space should not count as pet hover.'
    (Field $pet 'time').SetValue($pet, [double]31)
    Call $pet 'UpdateInterest' @($point, [double]0.25) | Out-Null
    Assert ($pet.CurrentState -eq 'greet') 'A later visit should greet again.'
    (Field $pet 'reducedMotion').SetValue($pet, $true)
    Call $pet 'SetState' @('idle') | Out-Null
    Call $pet 'UpdateInterest' @($corner, [double]0.1) | Out-Null
    (Field $pet 'time').SetValue($pet, [double]45)
    Call $pet 'UpdateInterest' @($point, [double]0.25) | Out-Null
    Assert ($pet.CurrentState -eq 'idle') 'Reduced motion should suppress animated greetings.'
    (Field $pet 'reducedMotion').SetValue($pet, $false)
    # Sleeping does not end because the mouse moves elsewhere or merely comes nearby.
    $pet.RenderPreview((Join-Path $PSScriptRoot 'previews\sleep-wake-check.png'),'sleep',0.2)
    (Field $pet 'time').SetValue($pet,[double]100)
    Call $pet 'StepAt' @([double]0.2,$corner,[uint32]0) | Out-Null
    Assert ($pet.CurrentState -eq 'sleep') 'Unrelated mouse activity must not wake the turtle.'
    $sleepBounds=(Field $pet 'modelBounds').GetValue($pet)
    $nearPoint=New-Object Drawing.Point([int]($sleepBounds.Left-2),[int]($sleepBounds.Top+$sleepBounds.Height/2))
    Call $pet 'StepAt' @([double]0.2,$nearPoint,[uint32]0) | Out-Null
    Assert ($pet.CurrentState -eq 'sleep') 'Hovering near the turtle must not wake it.'
    $sleepPixels=(Field $pet 'rasterPixels').GetValue($pet)
    $bodyPixels=@(for($i=0;$i -lt $sleepPixels.Length;$i++) {if($sleepPixels[$i] -ne 0) {$i}})
    $bodyPixel=$bodyPixels[[int]($bodyPixels.Count/2)]
    $bodyPoint=New-Object Drawing.Point(($bodyPixel % $pet.Width),[int][Math]::Floor($bodyPixel/$pet.Width))
    Call $pet 'StepAt' @([double]0.12,$bodyPoint,[uint32]0) | Out-Null
    Assert ($pet.CurrentState -eq 'surprised') 'Direct body hover should wake it with surprise.'
    Call $pet 'StepAt' @([double]0.8,$corner,[uint32]0) | Out-Null
    Assert ($pet.CurrentState -eq 'wake') 'Surprise should transition into waking.'
    Call $pet 'StepAt' @([double]3,$corner,[uint32]0) | Out-Null
    Assert ($pet.CurrentState -eq 'idle') 'Waking should settle back to idle.'
    # A planted flipper stays low; its recovery stroke lifts it clear of the floor.
    Call $pet 'SetState' @('walk') | Out-Null
    (Field $pet 'walkAmount').SetValue($pet, [double]1)
    $tip = Vertex 0 0.58 0.025
    (Field $pet 'phase').SetValue($pet, [double](0.25 * [Math]::PI * 2))
    $planted = Call $pet 'Pose' @($tip, [int]2)
    (Field $pet 'phase').SetValue($pet, [double](0.8 * [Math]::PI * 2))
    $lifted = Call $pet 'Pose' @($tip, [int]2)
    Assert ($lifted.z -gt $planted.z + 0.03) 'Recovery stroke should lift the flipper instead of sliding it.'
    (Field $pet 'phase').SetValue($pet,[double](.75*[Math]::PI*2))
    $recoveryStep=Call $pet 'FlipperStep' @([double]25,[double]0.04)
    Assert ([Math]::Abs($recoveryStep) -lt .000001) 'The body must stay still while the flippers recover.'
    Assert ((Field $pet 'phase').GetValue($pet) -gt (.75*[Math]::PI*2)) 'Flippers must keep cycling during the stationary recovery.'
    (Field $pet 'phase').SetValue($pet,[double](.25*[Math]::PI*2))
    Assert ((Call $pet 'FlipperStep' @([double]25,[double]0.04)) -gt 0) 'The planted push must advance the body.'
    $stride=(Field $pet 'petSize').GetValue($pet)*.16
    foreach($frames in @(20,100)) {
        (Field $pet 'phase').SetValue($pet,[double]0)
        $travel=0.0
        for($i=0;$i -lt $frames;$i++) {$travel+=Call $pet 'FlipperStep' @([double]$stride,[double](1.0/$frames))}
        Assert ([Math]::Abs($travel-$stride) -lt .0001) 'A full stroke must travel the same distance at different frame rates.'
    }
    foreach ($size in @(145,190,235)) {
        (Field $pet 'petSize').SetValue($pet, [int]$size)
        foreach ($pose in @('idle','walk','wave','dance','sleep','carried','curious','greet','working','surprised','climb','climbTop','climbDown','tipBack','tumble','onBack','helpUp')) {
            $path = Join-Path $PSScriptRoot "previews\$pose-$size.png"
            $pet.RenderPreview($path, $pose, 1.2)
            $bounds = (Field $pet 'modelBounds').GetValue($pet)
            Assert ($bounds.Left -ge 0 -and $bounds.Top -ge 0 -and $bounds.Right -le $pet.Width -and $bounds.Bottom -le $pet.Height) "Clipped model: $pose at $size pixels ($bounds)"
        }
    }
    (Field $pet 'reducedMotion').SetValue($pet, $true)
    $first = Join-Path $PSScriptRoot 'previews\reduced-a.png'
    $second = Join-Path $PSScriptRoot 'previews\reduced-b.png'
    $pet.RenderPreview($first, 'dance', 1.2)
    $pet.RenderPreview($second, 'dance', 1.8)
    Assert ((Get-FileHash -LiteralPath $first).Hash -eq (Get-FileHash -LiteralPath $second).Hash) 'Reduced motion should keep the model still.'
    # The workstation is visible only in the working scene and typing is time-based.
    (Field $pet 'reducedMotion').SetValue($pet, $false)
    $pet.RenderPreview((Join-Path $PSScriptRoot 'previews\work-a.png'),'working',0.2)
    $sceneWorking=@(Call $pet 'SceneTriangles' @())
    (Field $pet 'time').SetValue($pet,[double]0.2)
    $hand = Vertex 0 0.58 0.025
    $tapA=Call $pet 'Pose' @($hand,[int]2)
    (Field $pet 'time').SetValue($pet,[double]0.3)
    $tapB=Call $pet 'Pose' @($hand,[int]2)
    Assert ([Math]::Abs($tapA.z-$tapB.z) -gt 0.001) 'Typing should animate the front flippers.'
    $pet.RenderPreview((Join-Path $PSScriptRoot 'previews\work-exit.png'),'idle',1.2)
    $sceneIdle=@(Call $pet 'SceneTriangles' @())
    Assert ($sceneWorking.Count -gt $sceneIdle.Count) 'Workstation props should appear only while working.'
    (Field $pet 'reducedMotion').SetValue($pet,$true)
    $pet.RenderPreview((Join-Path $PSScriptRoot 'previews\work-still-a.png'),'working',0.2)
    $pet.RenderPreview((Join-Path $PSScriptRoot 'previews\work-still-b.png'),'working',0.8)
    Assert ((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'previews\work-still-a.png')).Hash -eq (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'previews\work-still-b.png')).Hash) 'Reduced-motion working pose should stay still.'
    Write-Output 'PASS: rigid parts, depth, walking/drop, hover, direct-hover surprised waking, reduced motion, 51 pose/size renders, workstation typing.'
} finally { $pet.Dispose() }
