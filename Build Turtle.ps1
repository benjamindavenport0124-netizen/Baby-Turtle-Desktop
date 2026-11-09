param([string]$OutputDirectory=$PSScriptRoot)
$ErrorActionPreference='Stop'
[void][IO.Directory]::CreateDirectory($OutputDirectory)
& (Join-Path $PSScriptRoot 'Turtle.ps1') -Check
$iconPath=Join-Path $PSScriptRoot 'assets\turtle.ico'
$pet=New-Object TurtlePet
try {
    $stream=[IO.File]::Create($iconPath)
    try {$pet.Icon.Save($stream)} finally {$stream.Dispose()}
} finally {$pet.Dispose()}
$parameters=New-Object CodeDom.Compiler.CompilerParameters
$parameters.GenerateExecutable=$true
$parameters.OutputAssembly=Join-Path $OutputDirectory 'Baby Turtle.exe'
$parameters.CompilerOptions='/target:winexe /win32icon:"'+$iconPath+'"'
[void]$parameters.ReferencedAssemblies.Add('System.dll')
[void]$parameters.ReferencedAssemblies.Add('System.Core.dll')
[void]$parameters.ReferencedAssemblies.Add('System.Windows.Forms.dll')
[void]$parameters.ReferencedAssemblies.Add([Management.Automation.PowerShell].Assembly.Location)
Add-Type -Path (Join-Path $PSScriptRoot 'src\TurtleApp.cs') -CompilerParameters $parameters
Write-Output 'Built Baby Turtle.exe with its own turtle icon.'
