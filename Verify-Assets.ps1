$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Turtle.ps1') -Check
$empty=Join-Path ([IO.Path]::GetTempPath()) ('turtle-assets-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($empty)
[TurtlePet]::AssetDirectory=$empty
$pet=New-Object TurtlePet
try {
    $pet.LoadModel()
    if($pet.ModelSource -ne 'procedural') {throw 'The image-free fallback did not load.'}
    if($pet.TriangleCount -lt 200) {throw 'The procedural model failed without reference files.'}
    $output=Join-Path $PSScriptRoot 'previews\procedural-check.png'
    $pet.RenderPreview($output,'working',1.2)
    Write-Output 'PASS: model and workstation render without external images or STL.'
} finally {$pet.Dispose();[IO.Directory]::Delete($empty)}
