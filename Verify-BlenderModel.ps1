$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Turtle.ps1') -Check
$flags=[Reflection.BindingFlags]'Instance,NonPublic,DeclaredOnly'
function Field($object,$name) {$object.GetType().GetField($name,$flags)}
function Assert($condition,$message) {if(-not $condition) {throw $message}}
$data=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'assets\baby-turtle.mesh.json') -Raw | ConvertFrom-Json
Assert ($data.preparedSourceSha256 -eq (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'assets\baby-turtle.blend') -Algorithm SHA256).Hash.ToLowerInvariant()) 'Prepared Blender project must match its declared hash.'
$pet=New-Object TurtlePet
try {
    $pet.LoadModel()
    Assert ($pet.ModelSource -eq 'Blender') 'The app must load the supplied Blender model.'
    Assert ($data.parts.Count -eq 6) 'Expected six exported Blender parts.'
    $expected=0
    foreach($part in $data.parts) {$expected+=$part.triangles.Count; $expected+=@($part.triangles | Where-Object eye).Count}
    Assert ($pet.TriangleCount -eq $expected) 'All exported triangles and eye backing faces must load.'
    $pivots=(Field $pet 'modelPivots').GetValue($pet)
    foreach($part in $data.parts) {
        $pivot=$pivots[$part.id]
        Assert ([Math]::Abs($pivot.x-$part.pivot[0]) -lt 0.00001) 'An animation pivot differs from the exported Blender attachment.'
        Assert ([Math]::Abs($pivot.y-$part.pivot[1]) -lt 0.00001) 'A left/right animation pivot differs from the Blender model.'
    }
    Assert ($pivots[2].y -gt 0 -and $pivots[3].y -lt 0 -and $pivots[4].y -gt 0 -and $pivots[5].y -lt 0) 'Flippers must use the turtle-own left/right convention.'
    Assert ($pivots[2].x -gt $pivots[4].x -and $pivots[3].x -gt $pivots[5].x) 'Front and rear flippers must not be swapped.'
    $mesh=(Field $pet 'mesh').GetValue($pet)
    foreach($part in $data.parts) {
        foreach($triangle in $part.triangles) {
            $color=[Drawing.Color]::FromArgb($triangle.color[0],$triangle.color[1],$triangle.color[2]).ToArgb()
            Assert (@($mesh | Where-Object {$_.part -eq $part.id -and $_.color.ToArgb() -eq $color}).Count -gt 0) 'An authored Blender material color was lost.'
        }
    }
    Assert (@($mesh | Where-Object eye).Count -gt 0) 'Head eyes must remain available to blinking.'
    # A malformed index must fail closed rather than load arbitrary or broken geometry.
    $scratch=Join-Path ([IO.Path]::GetTempPath()) ('turtle-mesh-'+[guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($scratch)
    $fixture=Join-Path $scratch 'baby-turtle.mesh.json'
    $originalAssets=[TurtlePet]::AssetDirectory
    try {
        $data.parts[0].triangles[0].indices[0]=999999
        $data | ConvertTo-Json -Depth 15 -Compress | Set-Content -LiteralPath $fixture
        [TurtlePet]::AssetDirectory=$scratch
        $bad=New-Object TurtlePet
        try {
            $rejected=$false
            try {$bad.LoadModel()} catch {$rejected=$true}
            Assert $rejected 'Out-of-range mesh indices must be rejected.'
        } finally {$bad.Dispose()}
    } finally {
        [TurtlePet]::AssetDirectory=$originalAssets
        if(Test-Path -LiteralPath $fixture) {Remove-Item -LiteralPath $fixture}
        [IO.Directory]::Delete($scratch)
    }
    Write-Output 'PASS: Blender geometry, authored material colors, six attachment pivots, left/right mapping, eye semantics, invalid mesh rejection.'
} finally {$pet.Dispose()}
