param([ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[a-zA-Z0-9.]+)?$')][string]$Version='0.1.0-beta.1')
$ErrorActionPreference='Stop'
$releaseRoot=Join-Path $PSScriptRoot ('dist\'+$Version+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$stage=Join-Path $releaseRoot 'BabyTurtle'
[void][IO.Directory]::CreateDirectory($stage)
& powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build Turtle.ps1') -OutputDirectory $stage
if($LASTEXITCODE -ne 0) {throw 'The app host failed to build.'}
foreach($name in @('src','assets','docs\media','previews','tools')) {[void][IO.Directory]::CreateDirectory((Join-Path $stage $name))}
foreach($name in @('Start Turtle.cmd','Turtle.ps1','Build Turtle.ps1','Package Release.ps1','README.md','LICENSE','CREDITS.md','CHANGELOG.md','RELEASE_NOTES.md','SECURITY.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage
}
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File | Copy-Item -Destination (Join-Path $stage 'src')
Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'Verify-*.ps1' -File | Copy-Item -Destination $stage
foreach($name in @('working.png','idle.png','climbTop.png')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('docs\media\'+$name)) -Destination (Join-Path $stage 'docs\media')
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'previews\Render-Motion.ps1') -Destination (Join-Path $stage 'previews')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\SECURITY_REVIEW.md') -Destination (Join-Path $stage 'docs')
foreach($tool in @('Export-BlenderModel.py','Verify-BlenderExporter.py')) {Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('tools\'+$tool)) -Destination (Join-Path $stage 'tools')}
foreach($name in @('turtle.ico','baby-turtle.mesh.json','baby-turtle.blend')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('assets\'+$name)) -Destination (Join-Path $stage 'assets')
}
# Compile and load the actual release payload.
& powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File (Join-Path $stage 'Turtle.ps1') -Check
if($LASTEXITCODE -ne 0) {throw 'The staged release failed its startup check.'}
$zip=Join-Path $releaseRoot ('BabyTurtle-'+$Version+'-windows.zip')
Compress-Archive -LiteralPath $stage -DestinationPath $zip
$hash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($zip+'.sha256'),$hash+'  '+[IO.Path]::GetFileName($zip)+[Environment]::NewLine)
Write-Output ('Release ZIP: '+$zip)
Write-Output ('SHA256: '+$hash)
