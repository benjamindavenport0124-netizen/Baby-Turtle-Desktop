param([switch]$Live)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName WindowsBase
$uiaReferences = @([System.Windows.Automation.AutomationElement].Assembly.Location, [System.Windows.Automation.ControlType].Assembly.Location, [System.Windows.Rect].Assembly.Location)
Add-Type -Path @((Join-Path $PSScriptRoot 'src\TurtlePet.cs'), (Join-Path $PSScriptRoot 'src\TurtleGeometry.cs'), (Join-Path $PSScriptRoot 'src\CodexActivity.cs'),(Join-Path $PSScriptRoot 'src\DictationTarget.cs'),(Join-Path $PSScriptRoot 'src\DesktopScreenshot.cs'),(Join-Path $PSScriptRoot 'src\ChatGptActivity.cs'),(Join-Path $PSScriptRoot 'src\TurtleAdventure.cs')) -ReferencedAssemblies (@("System.Windows.Forms","System.Drawing","System.Web.Extensions","System.Core") + $uiaReferences)
[TurtlePet]::AssetDirectory = Join-Path $PSScriptRoot 'assets'
function Assert($condition,$message) {if(-not $condition) {throw $message}}
$chatTracker=New-Object ChatGptActivityTracker
$chatTracker.Observe($false,$true,$true)
Assert ($chatTracker.Phase -eq 'idle' -and $chatTracker.CompletionSequence -eq 0) 'An idle ChatGPT window should not celebrate on startup.'
$chatTracker.Observe($true,$false,$true)
Assert ($chatTracker.Phase -eq 'working') 'ChatGPT stop-response controls should indicate real activity.'
$chatTracker.Observe($false,$false,$true)
Assert ($chatTracker.Phase -eq 'unknown' -and $chatTracker.CompletionSequence -eq 0) 'Missing accessibility controls must not fake completion.'
$chatTracker.Observe($false,$true,$true)
Assert ($chatTracker.CompletionSequence -eq 0) 'One ready observation should not complete a response during repaint.'
$chatTracker.Observe($true,$false,$true)
$chatTracker.Observe($false,$true,$true)
$chatTracker.Observe($false,$true,$true)
Assert ($chatTracker.Phase -eq 'idle' -and $chatTracker.CompletionSequence -eq 1) 'Two stable ready observations should end the response once.'
$chatTracker.Observe($false,$true,$true)
Assert ($chatTracker.CompletionSequence -eq 1) 'An idle ChatGPT window must not replay response-ended events.'
$chatTracker.Observe($true,$false,$true)
$chatTracker.Observe($false,$false,$false)
Assert ($chatTracker.Phase -eq 'disconnected' -and $chatTracker.CompletionSequence -eq 1) 'Losing the ChatGPT window must not invent a successful completion.'
$inputLink=New-Object DictationTarget
try {
    $result=$inputLink.FocusAsync().GetAwaiter().GetResult()
    Assert (-not $result.Success -and $result.Window -eq [IntPtr]::Zero) 'An unlinked target must not report focus success.'
    $inputLink.BeginLink()
    Assert ($inputLink.Read().Linking -and -not $inputLink.Read().Linked) 'Link mode must await an explicit target selection.'
    $inputLink.Forget()
    Assert (-not $inputLink.Read().Linking -and -not $inputLink.Read().Linked) 'Forgetting should cancel link mode.'
} finally {$inputLink.Dispose()}
# Validate the lossless image payload without capturing the screen or changing the clipboard.
$sample=New-Object System.Drawing.Bitmap(64,32)
$sample.SetPixel(0,0,[Drawing.Color]::Red)
$sample.SetPixel(63,31,[Drawing.Color]::Blue)
$payload=New-Object ScreenshotClipboardPayload($sample)
try {
    Assert ($payload.Data.GetDataPresent([Windows.Forms.DataFormats]::Bitmap)) 'Screenshot needs a native bitmap clipboard format.'
    Assert ($payload.Data.GetDataPresent('PNG')) 'Screenshot needs PNG clipboard data for image attachments.'
    $png=$payload.Data.GetData('PNG');$png.Position=0
    $decoded=[Drawing.Bitmap]::FromStream($png)
    try {
        Assert ($decoded.Width -eq 64 -and $decoded.Height -eq 32) 'Screenshot PNG dimensions changed.'
        Assert ($decoded.GetPixel(0,0).ToArgb() -eq [Drawing.Color]::Red.ToArgb() -and $decoded.GetPixel(63,31).ToArgb() -eq [Drawing.Color]::Blue.ToArgb()) 'Screenshot PNG should preserve edge pixels.'
    } finally {$decoded.Dispose()}
} finally {$payload.Dispose();$sample.Dispose()}
$displays=[Windows.Forms.Screen]::AllScreens
foreach($display in $displays) {
    $bounds=[DesktopScreenshot]::SelectBounds($displays,$display.DeviceName,[Windows.Forms.SystemInformation]::VirtualScreen)
    Assert ($bounds -eq $display.Bounds) 'A single-monitor selection must use only that display bounds.'
}
$allBounds=[DesktopScreenshot]::SelectBounds($displays,$null,[Windows.Forms.SystemInformation]::VirtualScreen)
Assert ($allBounds -eq [Windows.Forms.SystemInformation]::VirtualScreen) 'All-monitor capture must be an explicit separate selection.'
$unlinkedPet=New-Object TurtlePet
try {
    $private=[Reflection.BindingFlags]'Instance,NonPublic,DeclaredOnly'
    $unlinkedPet.GetType().GetMethod('AttachScreenshotForDisplay',$private).Invoke($unlinkedPet,@([Windows.Forms.Screen]::PrimaryScreen.DeviceName)) | Out-Null
    Assert (-not $unlinkedPet.GetType().GetField('screenshotQueued',$private).GetValue($unlinkedPet)) 'An unlinked screenshot action must not capture or paste.'
    # A menu-selection release must not start dictation/focus. Keep the fallback linked
    # and empty so even a regression cannot inject a shortcut during this check.
    $unlinkedPet.GetType().GetField('preferLinkedInput',$private).SetValue($unlinkedPet,$true)
    $release=New-Object Windows.Forms.MouseEventArgs([Windows.Forms.MouseButtons]::Left,1,0,0,0)
    $unlinkedPet.GetType().GetMethod('OnPointerUp',$private).Invoke($unlinkedPet,@($null,$release.PSObject.BaseObject)) | Out-Null
    Assert ($null -eq $unlinkedPet.GetType().GetField('inputFocus',$private).GetValue($unlinkedPet)) 'A stray menu mouse release must not focus Codex before screenshot capture.'
} finally {$unlinkedPet.Dispose()}
$encoding=New-Object System.Text.UTF8Encoding($false)
$root=Join-Path $PSScriptRoot ('previews\codex-test-'+[Guid]::NewGuid().ToString('N'))
$sessions=Join-Path $root 'sessions'
New-Item -ItemType Directory -Path $sessions -Force | Out-Null
$a='11111111-1111-4111-8111-111111111111';$b='22222222-2222-4222-8222-222222222222'
$fileA=Join-Path $sessions ('rollout-'+$a+'.jsonl');$fileB=Join-Path $sessions ('rollout-'+$b+'.jsonl')
$globalFile=Join-Path $root '.codex-global-state.json'
function Select-Chat($id) {
    $times=@{};$key=ConvertTo-Json -InputObject @('local',$id) -Compress
    $times[$key]=[long]123456789
    $document=@{'electron-persisted-atom-state'=@{'thread-user-activity-times-v1'=$times}}
    [IO.File]::WriteAllText($globalFile,($document|ConvertTo-Json -Depth 8 -Compress),$encoding)
}
function Event-Line($kind,$turn,$parent=$turn) {
    @{type='event_msg';payload=@{type=$kind;turn_id=$turn;root_turn_id=$parent}} | ConvertTo-Json -Depth 8 -Compress
}
function Append-Event($path,$kind,$turn,$parent=$turn) {[IO.File]::AppendAllText($path,(Event-Line $kind $turn $parent)+"`n",$encoding)}
$monitor=New-Object CodexActivityMonitor($root)
try {
    [IO.File]::WriteAllText((Join-Path $root 'session_index.jsonl'),(@{id=$a;thread_name='Fixture A'}|ConvertTo-Json -Compress)+"`n"+(@{id=$b;thread_name='Fixture B'}|ConvertTo-Json -Compress)+"`n",$encoding)
    Select-Chat $a
    Append-Event $fileA 'task_started' 'old'
    Append-Event $fileA 'task_complete' 'old'
    $monitor.Poll();$status=$monitor.Read()
    Assert ($status.Connected -and $status.Phase -eq 'idle' -and $status.CompletionSequence -eq 0) 'Startup should connect without celebrating an old response.'
    Append-Event $fileA 'task_started' 'live'
    $monitor.Poll();Assert ($monitor.Read().Phase -eq 'working') 'A real start event should show working.'
    $completion=Event-Line 'task_complete' 'live';$cut=[int]($completion.Length/2)
    [IO.File]::AppendAllText($fileA,$completion.Substring(0,$cut),$encoding)
    $monitor.Poll();Assert ($monitor.Read().Phase -eq 'working') 'A partial event should remain buffered.'
    [IO.File]::AppendAllText($fileA,$completion.Substring($cut)+"`n",$encoding)
    $monitor.Poll();$status=$monitor.Read()
    Assert ($status.Phase -eq 'idle' -and $status.CompletionSequence -eq 1 -and $status.CompletionKind -eq 'ready') 'Completion should signal one ready reaction.'
    Append-Event $fileA 'task_complete' 'live';$monitor.Poll()
    Assert ($monitor.Read().CompletionSequence -eq 1) 'Duplicate completion should not celebrate again.'
    Append-Event $fileA 'task_started' 'new'
    Append-Event $fileA 'task_complete' 'live'
    Append-Event $fileA 'task_complete' 'child' 'new'
    $monitor.Poll();Assert ($monitor.Read().Phase -eq 'working') 'An old or child completion must not stop a newer root turn.'
    Append-Event $fileA 'turn_aborted' 'new';$monitor.Poll();$status=$monitor.Read()
    Assert ($status.Phase -eq 'stopped' -and $status.CompletionKind -eq 'stopped' -and $status.CompletionSequence -eq 2) 'Interruption must not appear as a successful reply.'
    Append-Event $fileB 'task_started' 'earlier'
    Append-Event $fileB 'task_complete' 'earlier'
    Select-Chat $b;$monitor.Poll();$status=$monitor.Read()
    Assert ($status.ThreadId -eq $b -and $status.Title -eq 'Fixture B' -and $status.Phase -eq 'idle' -and $status.CompletionSequence -eq 2) 'Chat switches should not replay old completion reactions.'
    Append-Event $fileB 'task_started' 'live-b';$monitor.Poll()
    Append-Event $fileB 'task_complete' 'live-b';$monitor.Poll()
    Assert ($monitor.Read().CompletionSequence -eq 3) 'New chat events should be followed after switching.'
    [IO.File]::WriteAllText($fileB,(Event-Line 'task_started' 'truncated')+"`n",$encoding)
    $monitor.Poll();$monitor.Poll()
    Assert ($monitor.Read().Phase -eq 'working') 'Truncated session files should reconnect.'
    [IO.File]::AppendAllText($fileB,((' '*100)+"`n"),$encoding)
    $monitor.Poll()
    foreach($larger in @($false,$true)) {
        $oldLength=(Get-Item -LiteralPath $fileB).Length
        $replacement=(Event-Line 'task_complete' 'swap')+"`n"
        $targetLength=[Math]::Max($oldLength,$encoding.GetByteCount($replacement))+$(if($larger) {100} else {0})
        $replacement=$replacement.PadRight([int]$targetLength,' ')
        $swap=$fileB+'.swap'
        [IO.File]::WriteAllText($swap,$replacement,$encoding)
        [IO.File]::Replace($swap,$fileB,($fileB+'.backup'))
        $monitor.Poll();$monitor.Poll()
        Assert ($monitor.Read().Connected -and $monitor.Read().Phase -eq 'idle') 'Atomic replacement must reopen the actual file, including equal/larger sizes.'
        Append-Event $fileB 'task_started' 'after-swap';$monitor.Poll()
        Assert ($monitor.Read().Phase -eq 'working') 'Events appended after replacement must remain observable.'
    }
    [IO.File]::WriteAllText($globalFile,'{}',$encoding)
    $monitor.Poll();Assert (-not $monitor.Read().Connected) 'Missing activity metadata should report disconnected.'

    # UI reactions use monitor events, not the passage of time or a simulated response.
    $pet=New-Object TurtlePet
    try {
        $flags=[Reflection.BindingFlags]'Instance,NonPublic,DeclaredOnly'
        $apply=$pet.GetType().GetMethod('ApplyCodex',$flags)
        $incoming=New-Object CodexActivitySnapshot
        $incoming.ThreadId=$a;$incoming.Title='Fixture A';$incoming.Connected=$true;$incoming.Phase='working'
        $apply.Invoke($pet,@($incoming.PSObject.BaseObject)) | Out-Null
        Assert ($pet.CurrentState -eq 'working') 'The turtle should react to a real working status.'
        $incoming=$incoming.Copy();$incoming.Phase='idle';$incoming.CompletionSequence=1;$incoming.CompletionKind='ready'
        $apply.Invoke($pet,@($incoming.PSObject.BaseObject)) | Out-Null
        Assert ($pet.CurrentState -eq 'dance') 'A newly completed turn should start a dance.'
        $applyChat=$pet.GetType().GetMethod('ApplyChatGpt',$flags)
        $chat=New-Object CodexActivitySnapshot
        $chat.ThreadId='chatgpt-fixture';$chat.Title='ChatGPT';$chat.Connected=$true;$chat.Phase='working'
        $applyChat.Invoke($pet,@($chat.PSObject.BaseObject)) | Out-Null
        Assert ($pet.CurrentState -eq 'working') 'ChatGPT should use the computer pose independently of Codex.'
        $apply.Invoke($pet,@($incoming.PSObject.BaseObject)) | Out-Null
        Assert ($pet.CurrentState -eq 'working') 'An idle Codex poll must not stop ChatGPT typing.'
        $incoming=$incoming.Copy();$incoming.Phase='working'
        $apply.Invoke($pet,@($incoming.PSObject.BaseObject)) | Out-Null
        $chat=$chat.Copy();$chat.Phase='idle';$chat.CompletionSequence=1;$chat.CompletionKind='response_ended'
        $applyChat.Invoke($pet,@($chat.PSObject.BaseObject)) | Out-Null
        Assert ($pet.CurrentState -eq 'working') 'Finishing ChatGPT must not stop a still-running Codex turn.'
        $incoming=$incoming.Copy();$incoming.Phase='idle';$incoming.CompletionSequence=2
        $apply.Invoke($pet,@($incoming.PSObject.BaseObject)) | Out-Null
        Assert ($pet.CurrentState -eq 'dance') 'The turtle should celebrate after both assistants finish.'
    } finally {$pet.Dispose()}
    Write-Output 'PASS: screenshot payload/guards, Codex lifecycle, ChatGPT observed controls/debounce, mixed-assistant activity, completion reactions.'
} finally {
    $monitor.Dispose()
    # Only remove individual synthetic fixture files, then their empty directories.
    foreach($path in @($fileA,$fileB,($fileB+'.backup'),($fileB+'.swap'),$globalFile,(Join-Path $root 'session_index.jsonl'))) {if(Test-Path -LiteralPath $path) {Remove-Item -LiteralPath $path}}
    Remove-Item -LiteralPath $sessions
    Remove-Item -LiteralPath $root
}
if($Live) {
    $codexHome=$env:CODEX_HOME
    if(-not $codexHome) {$codexHome=Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex'}
    $probe=New-Object CodexActivityMonitor($codexHome)
    try {
        1..8 | ForEach-Object {$probe.Poll()}
        $status=$probe.Read()
        Write-Output "Live connection: connected=$($status.Connected), phase=$($status.Phase), chat=$($status.Title)"
        Assert ($status.Connected) 'The live local Codex activity connection did not attach.'
    } finally {$probe.Dispose()}
}

# Hold the worker lock on another thread to model a blocked disk/provider.
Add-Type -TypeDefinition @"
using System;
using System.Reflection;
using System.Threading;
public static class MonitorResponsivenessCheck {
    public static void Run(object monitor) {
        object gate=monitor.GetType().GetField("gate",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(monitor);
        using(var entered=new ManualResetEvent(false)) using(var release=new ManualResetEvent(false)) {
            var worker=new Thread(()=>{lock(gate) {entered.Set();release.WaitOne();}});
            worker.IsBackground=true;worker.Start();entered.WaitOne();
            Exception failure=null;
            var reader=new Thread(()=>{try {monitor.GetType().GetMethod("Read").Invoke(monitor,null);((IDisposable)monitor).Dispose();} catch(Exception e) {failure=e;}});
            reader.IsBackground=true;reader.Start();
            bool responsive=reader.Join(1000);
            release.Set();worker.Join();reader.Join();
            if(!responsive) throw new Exception("Read/Dispose waited for worker I/O");
            if(failure!=null) throw failure;
        }
    }
}
"@
[MonitorResponsivenessCheck]::Run((New-Object CodexActivityMonitor($root)))
Write-Output 'PASS: monitor read/dispose remain responsive with a blocked worker.'
