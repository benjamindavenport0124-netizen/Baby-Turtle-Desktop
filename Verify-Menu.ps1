param([switch]$Headless)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Turtle.ps1') -Check
$flags=[Reflection.BindingFlags]'Instance,NonPublic,DeclaredOnly'
function Assert($condition,$message) {if(-not $condition) {throw $message}}
$pet=New-Object TurtlePet
try {
    $pet.LoadModel()
    Assert ($pet.ShowInTaskbar) 'The turtle should appear on the taskbar.'
    Assert ($null -ne $pet.Icon) 'The taskbar should have its own turtle icon.'
    $windowStyle=$pet.GetType().GetProperty('CreateParams',$flags).GetValue($pet,$null).ExStyle
    Assert (($windowStyle -band 0x40000) -ne 0) 'The overlay should explicitly appear as an app window.'
    Assert (($windowStyle -band 0x80) -eq 0) 'Tool-window styling must not hide the taskbar button.'
    Assert (($windowStyle -band 0x08000000) -ne 0) 'The turtle must still preserve message-box focus.'
    $iconBitmap=$pet.Icon.ToBitmap()
    try {$iconBitmap.Save((Join-Path $PSScriptRoot 'previews\app-icon.png'))} finally {$iconBitmap.Dispose()}
    $menu=$pet.GetType().GetField('menu',$flags).GetValue($pet)
    $labels=@($menu.Items | ForEach-Object {$_.Text})
    foreach($label in @('Pet actions','Behavior && appearance','Connections','Restart turtle','Quit','Attach screenshot')) {
        Assert ($labels -contains $label) "Missing menu action: $label"
    }
    Assert ($menu.Items.Count -le 14) 'The top-level menu should remain compact.'
    $connections=@($menu.Items | Where-Object Text -eq 'Connections')[0]
    Assert ($connections.DropDownItems.Count -eq 7) 'All connection actions and statuses must remain available.'
    $voice=@($menu.Items | Where-Object Text -eq 'Open voice typing')[0]
    Assert ($voice.ShortcutKeyDisplayString -eq 'Ctrl+Alt+Space') 'Voice typing should have a separate shortcut column.'
    if(-not $Headless) {
    $menu.Show(200,200)
    [Windows.Forms.Application]::DoEvents()
    $image=New-Object Drawing.Bitmap($menu.Width,$menu.Height)
    try {$menu.DrawToBitmap($image,(New-Object Drawing.Rectangle(0,0,$menu.Width,$menu.Height)));$image.Save((Join-Path $PSScriptRoot 'previews\menu.png'))} finally {$image.Dispose()}
    $menu.Close()
    }
    $restart=@($menu.Items | Where-Object Text -eq 'Restart turtle')[0]
    Assert (-not [TurtlePet]::RestartRequested) 'Restart must not run until requested.'
    $restart.PerformClick()
    Assert ([TurtlePet]::RestartRequested) 'Restart should signal the launcher to start a fresh process after shutdown.'
    $parseErrors=$null;$tokens=$null
    [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Turtle.ps1'),[ref]$tokens,[ref]$parseErrors) | Out-Null
    Assert ($parseErrors.Count -eq 0) 'The restart launcher must parse successfully.'
    Write-Output 'PASS: compact menu, grouped connections, shortcut labels, menu render, explicit restart handoff, launcher syntax.'
} finally {$pet.Dispose()}
