param([switch]$Check, [switch]$Preview)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName WindowsBase
$uiaReferences = @([System.Windows.Automation.AutomationElement].Assembly.Location, [System.Windows.Automation.ControlType].Assembly.Location, [System.Windows.Rect].Assembly.Location)
Add-Type -Path @((Join-Path $PSScriptRoot 'src\TurtlePet.cs'), (Join-Path $PSScriptRoot 'src\TurtleGeometry.cs'), (Join-Path $PSScriptRoot 'src\CodexActivity.cs'),(Join-Path $PSScriptRoot 'src\DictationTarget.cs'),(Join-Path $PSScriptRoot 'src\DesktopScreenshot.cs'),(Join-Path $PSScriptRoot 'src\ChatGptActivity.cs'),(Join-Path $PSScriptRoot 'src\TurtleAdventure.cs')) -ReferencedAssemblies (@("System.Windows.Forms","System.Drawing","System.Web.Extensions","System.Core") + $uiaReferences)
[TurtlePet]::AssetDirectory = Join-Path $PSScriptRoot 'assets'
if ($Check -or $Preview) {
    $pet = New-Object TurtlePet
    try {
        $pet.LoadModel()
        if ($pet.TriangleCount -lt 200) { throw 'Model geometry is missing.' }
        if ($Preview) {
            foreach ($pose in @('idle', 'walk', 'wave', 'dance', 'sleep', 'carried', 'curious', 'greet', 'working', 'surprised', 'climb', 'climbTop', 'climbDown', 'tipBack', 'tumble', 'onBack', 'helpUp')) {
                $pet.RenderPreview((Join-Path $PSScriptRoot "previews\$pose.png"), $pose, 1.2)
            }
        }
        Write-Output "Turtle compiled and $($pet.ModelSource) model loaded: $($pet.TriangleCount) triangles in six rigid parts."
    } finally { $pet.Dispose() }
    return
}
$mutex = New-Object System.Threading.Mutex($false, 'Local\CodexBabyTurtle')
if (-not $mutex.WaitOne(0)) { $mutex.Dispose(); return }
try { [TurtlePet]::Run((Join-Path $PSScriptRoot 'turtle.settings')) }
finally { $mutex.ReleaseMutex(); $mutex.Dispose() }
if ([TurtlePet]::RestartRequested) {
    $appPath=Join-Path $PSScriptRoot 'Baby Turtle.exe'
    if(Test-Path -LiteralPath $appPath) {Start-Process -FilePath $appPath -WindowStyle Hidden}
    else {Start-Process powershell.exe -WindowStyle Hidden -ArgumentList ('-NoProfile -STA -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"')}
}
