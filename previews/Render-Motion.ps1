$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName WindowsBase
$uiaReferences = @([System.Windows.Automation.AutomationElement].Assembly.Location, [System.Windows.Automation.ControlType].Assembly.Location, [System.Windows.Rect].Assembly.Location)
Add-Type -Path @((Join-Path $PSScriptRoot '..\src\TurtlePet.cs'),(Join-Path $PSScriptRoot '..\src\TurtleGeometry.cs'),(Join-Path $PSScriptRoot '..\src\CodexActivity.cs'),(Join-Path $PSScriptRoot '..\src\DictationTarget.cs'),(Join-Path $PSScriptRoot '..\src\DesktopScreenshot.cs'),(Join-Path $PSScriptRoot '..\src\ChatGptActivity.cs'),(Join-Path $PSScriptRoot '..\src\TurtleAdventure.cs')) -ReferencedAssemblies (@("System.Windows.Forms","System.Drawing","System.Web.Extensions","System.Core") + $uiaReferences)
[TurtlePet]::AssetDirectory = Join-Path $PSScriptRoot '..\assets'
$pet=New-Object TurtlePet
try {
    [void][IO.Directory]::CreateDirectory((Join-Path $PSScriptRoot 'motion'))
    $pet.LoadModel()
    foreach($pose in @('walk','greet','working','climb','tipBack','tumble','onBack','helpUp')) {
        for($frame=0;$frame -lt 24;$frame++) {
            $seconds=[double]($frame / 12.0)
            $pet.RenderPreview((Join-Path $PSScriptRoot "motion\$pose-$frame.png"),$pose,$seconds)
        }
    }
} finally { $pet.Dispose() }
