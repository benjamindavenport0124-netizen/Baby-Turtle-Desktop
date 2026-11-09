using System;
using System.IO;
using System.Drawing;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public sealed partial class TurtlePet : Form {
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput input);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [StructLayout(LayoutKind.Sequential)] struct LastInput { public uint size, time; }
    struct V {
        public float x,y,z;
        public V(float a,float b,float c) { x=a;y=b;z=c; }
        public static V operator -(V a,V b) { return new V(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static V Cross(V a,V b) { return new V(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x); }
    }
    sealed class Triangle { public V a,b,c; public Color color; public int part; public bool eye; }
    readonly List<Triangle> mesh=new List<Triangle>();
    readonly Timer clock=new Timer();
    readonly Stopwatch watch=Stopwatch.StartNew();
    readonly Random random=new Random();
    readonly ContextMenuStrip menu=new ContextMenuStrip();
    NotifyIcon tray;
    Icon appIcon;
    ToolStripMenuItem sleepItem;
    ToolStripMenuItem codexItem;
    CodexActivityMonitor codex;
    CodexActivitySnapshot codexStatus=new CodexActivitySnapshot();
    readonly ChatGptActivityMonitor chatgpt=new ChatGptActivityMonitor();
    CodexActivitySnapshot chatgptStatus=new CodexActivitySnapshot();
    ToolStripMenuItem chatgptItem;
    bool chatgptHotkey,batchingActivity;
    long seenChatGptCompletion;
    string pendingCelebration;
    bool watchCodex=true;
    double nextCodexPoll;
    long seenCompletion;
    bool hotkeyDictationQueued;
    double hotkeyDeadline;
    double nextTopmostCheck;
    readonly DictationTarget inputLink=new DictationTarget();
    System.Threading.Tasks.Task<InputFocusResult> inputFocus;
    ToolStripMenuItem linkItem;
    bool preferLinkedInput;
    long seenLinkVersion;
    bool linkHotkey;
    bool screenshotQueued,focusForScreenshot;
    double screenshotDue,screenshotPasteDeadline;
    Bitmap pendingScreenshot;
    IntPtr screenshotPasteWindow;
    long screenshotPasteToken;
    string screenshotDevice;
    readonly string settingsPath;
    bool wander=true, reducedMotion, dragging, pressed, hover, hotkey, messageMicrophone, greetedThisVisit;
    string state="idle", message="";
    double time,last,stateTime,nextAction=4,messageUntil,phase,yaw=-.55,headYaw,headPitch,walkAmount;
    double px,py,velocity,vy,dragVX,dragVY,targetX,lastPointerTime;
    double strokeEffort;
    static double StrokeProgress(double cycles) {
        double whole=Math.Floor(cycles),fraction=cycles-whole;
        return whole+Ease(Math.Min(1,fraction/.60));
    }
    double FlipperStep(double speed,double dt) {
        // The clock keeps the recovery moving even though the body is stationary.
        // Integrating the planted stroke avoids frame-rate dependent slipping.
        double stride=petSize*.16,cycles=phase/(Math.PI*2);
        double next=cycles+Math.Abs(speed)*dt/stride;
        phase=next*Math.PI*2;
        strokeEffort=Math.Max(strokeEffort,Math.Min(1,Math.Abs(speed)/16));
        return Math.Sign(speed)*stride*(StrokeProgress(next)-StrokeProgress(cycles));
    }
    double hoverDuration, lastGreeting=-20, interest, restYaw=-.55;
    double idleLookX, idleLookY, nextLook=3, nextBlink=2, blinkUntil;
    double workAmount;
    double sleepInhibitUntil;
    bool sleepHoverArmed;
    int petSize=190,idleMinutes=5;
    Point pressPoint,origin,lastPointer;
    RectangleF modelBounds=new RectangleF(20,80,230,130);
    public string CurrentState { get { return state; } }
    public int TriangleCount { get { return mesh.Count; } }
    public TurtlePet() : this(null) {}
    public TurtlePet(string settingsFile) {
        settingsPath=settingsFile;LoadSettings();
        Text="Baby Turtle";AccessibleName="Baby turtle desktop companion";
        AccessibleDescription="Click to open Windows voice typing. Drag to carry. Right-click for actions.";
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=true;TopMost=true;DoubleBuffered=true;
        appIcon=CreateTurtleIcon();Icon=appIcon;MinimizeBox=false;MaximizeBox=false;
        Disposed+=(s,e)=>{if(appIcon!=null) {appIcon.Dispose();appIcon=null;}};
        BackColor=Color.Magenta;TransparencyKey=Color.Magenta;ClientSize=new Size(420,400);StartPosition=FormStartPosition.Manual;
        var area=Screen.PrimaryScreen.WorkingArea;px=area.Right-Width-60;py=area.Bottom-Height;targetX=px;
        Location=new Point((int)px,(int)py);Cursor=Cursors.Hand;
        BuildMenu();MouseDown+=OnPointerDown;MouseMove+=OnPointerMove;MouseUp+=OnPointerUp;
        MouseCaptureChanged+=(s,e)=> {if(pressed&&!Capture) {pressed=false;if(dragging) {dragging=false;SetState("drop");}}};
        clock.Interval=20;clock.Tick+=(s,e)=>UpdateFrame();
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get {var p=base.CreateParams;p.ExStyle=(p.ExStyle&~0x00000080)|0x08000000|0x00040000|0x00000008;return p;} }
    static Icon CreateTurtleIcon() {
        using(var bitmap=new Bitmap(64,64)) {
            using(var g=Graphics.FromImage(bitmap)) {
                g.Clear(Color.Transparent);
                using(var flipper=new SolidBrush(Color.FromArgb(127,188,68))) {
                    g.FillRectangle(flipper,5,19,13,9);g.FillRectangle(flipper,5,44,13,9);
                    g.FillRectangle(flipper,46,19,13,9);g.FillRectangle(flipper,46,44,13,9);
                }
                using(var belly=new SolidBrush(Color.FromArgb(187,173,96))) g.FillRectangle(belly,15,18,34,38);
                Color[] greens={Color.FromArgb(54,115,60),Color.FromArgb(83,155,65),Color.FromArgb(124,190,78)};
                for(int y=0;y<4;y++) for(int x=0;x<4;x++) using(var b=new SolidBrush(greens[(x+y*2)%3])) g.FillRectangle(b,16+x*8,17+y*8,8,8);
                using(var head=new SolidBrush(Color.FromArgb(133,197,80))) g.FillRectangle(head,21,3,23,19);
                using(var eye=new SolidBrush(Color.FromArgb(29,40,24))) {g.FillRectangle(eye,21,7,4,5);g.FillRectangle(eye,40,7,4,5);}
            }
            IntPtr h=bitmap.GetHicon();
            try {using(var source=Icon.FromHandle(h)) return (Icon)source.Clone();} finally {DestroyIcon(h);}
        }
    }
    protected override void OnShown(EventArgs e) {base.OnShown(e);KeepOnTop();}
    protected override void OnDeactivate(EventArgs e) {base.OnDeactivate(e);nextTopmostCheck=0;}
    void KeepOnTop() {
        if(!IsHandleCreated||IsDisposed||!Visible||menu.Visible) return;
        TopMost=true;
        // HWND_TOPMOST, NOMOVE | NOSIZE | NOACTIVATE | NOOWNERZORDER.
        // Restore the overlay without stealing the linked message box's focus.
        SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x0213);
    }
    protected override void OnHandleCreated(EventArgs e) {base.OnHandleCreated(e);hotkey=RegisterHotKey(Handle,1,0x4003,0x20);linkHotkey=RegisterHotKey(Handle,2,0x4003,0x4C);chatgptHotkey=RegisterHotKey(Handle,3,0x4003,0x47);}
    protected override void WndProc(ref Message m) {
        if(m.Msg==0x21) {m.Result=(IntPtr)3;return;} // Preserve focus in the editor.
        if(m.Msg==0x312&&m.WParam==(IntPtr)1) {hotkeyDictationQueued=true;hotkeyDeadline=watch.Elapsed.TotalSeconds+3;return;}
        if(m.Msg==0x312&&m.WParam==(IntPtr)2) {preferLinkedInput=true;inputLink.CaptureFocused();return;}
        if(m.Msg==0x312&&m.WParam==(IntPtr)3) {chatgpt.LinkForeground();Notify("Checking ChatGPT activity...");return;}
        base.WndProc(ref m);
    }
    public void LoadModel() { BuildReferenceModel(); }
    void AddQuad(V a,V b,V c,V d,Color color,int part,bool eye=false) {mesh.Add(new Triangle{a=a,b=b,c=c,color=color,part=part,eye=eye});mesh.Add(new Triangle{a=a,b=c,c=d,color=color,part=part,eye=eye});}
    void BuildMenu() {
        menu.Items.Add("Baby Turtle").Enabled=false;
        menu.Items.Add("Open voice typing    Ctrl+Alt+Space",null,(s,e)=>Dictate());
        var screenshots=new ToolStripMenuItem("Attach screenshot");
        screenshots.DropDownOpening+=(s,e)=> {
            screenshots.DropDownItems.Clear();
            foreach(var screen in Screen.AllScreens) {
                string device=screen.DeviceName;
                string number=device.Substring(device.LastIndexOf("DISPLAY",StringComparison.OrdinalIgnoreCase)+7);
                screenshots.DropDownItems.Add("Monitor "+number+(screen.Primary?" (primary)":"")+" - "+screen.Bounds.Width+" x "+screen.Bounds.Height,null,(sender,args)=>AttachScreenshotForDisplay(device));
            }
            screenshots.DropDownItems.Add(new ToolStripSeparator());
            screenshots.DropDownItems.Add("All monitors",null,(sender,args)=>AttachScreenshotForDisplay(null));
        };
        screenshots.DropDownItems.Add("Choose a monitor");menu.Items.Add(screenshots);menu.Items.Add(new ToolStripSeparator());
        linkItem=new ToolStripMenuItem("Link message box    Ctrl+Alt+L",null,(s,e)=>{if(!linkHotkey) {Notify("Ctrl+Alt+L is used by another app");return;}preferLinkedInput=true;inputLink.BeginLink();Notify("Focus chat input, then Ctrl+Alt+L");messageUntil=time+30;});menu.Items.Add(linkItem);
        menu.Items.Add("Forget linked message box",null,(s,e)=>{inputLink.Forget();inputFocus=null;CancelScreenshot();preferLinkedInput=false;Notify("Message box link cleared");});menu.Items.Add(new ToolStripSeparator());
        var watch=new ToolStripMenuItem("React to Codex activity"){Checked=watchCodex,CheckOnClick=true};
        watch.CheckedChanged+=(s,e)=>{watchCodex=watch.Checked;if(watchCodex) StartCodexLink();else {if(codex!=null) {codex.Dispose();codex=null;}codexStatus=new CodexActivitySnapshot();RefreshAssistantPose();}SaveSettings();};menu.Items.Add(watch);
        codexItem=new ToolStripMenuItem("Codex: not connected");
        codexItem.Click+=(s,e)=>MessageBox.Show(codexStatus.Detail+"\n\n"+codexStatus.Title+"\n\nFollows the most recently used local Codex chat.\nReads local start/end events without sending prompts.\nRemote and cloud chats are not supported by this connection.\n\nTo dictate without focusing each time, use Link Codex message box.\nThe Windows microphone button controls recording.","Codex connection");menu.Items.Add(codexItem);
        menu.Items.Add("Link ChatGPT activity    Ctrl+Alt+G",null,(s,e)=>Notify(chatgptHotkey?"Focus ChatGPT, then Ctrl+Alt+G":"Ctrl+Alt+G is used by another app"));
        chatgptItem=new ToolStripMenuItem("ChatGPT: not linked",null,(s,e)=>MessageBox.Show(chatgptStatus.Detail+"\n\nFocus the ChatGPT Windows app and press Ctrl+Alt+G.\nThe turtle watches response buttons in that window without sending anything.\nKeep the conversation open. Some app versions may not expose these controls.\nRelink after restarting ChatGPT or the turtle.\n\nThis activity link is separate from the Ctrl+Alt+L message-box link.","ChatGPT activity"));menu.Items.Add(chatgptItem);
        menu.Items.Add("Forget ChatGPT activity link",null,(s,e)=>{chatgpt.Unlink();ApplyChatGpt(chatgpt.Read());Notify("ChatGPT activity link cleared");});
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Give a little pat",null,(s,e)=>Act("happy"));menu.Items.Add("Wave",null,(s,e)=>Act("wave"));menu.Items.Add("Dance",null,(s,e)=>Act("dance"));
        sleepItem=new ToolStripMenuItem("Go to sleep",null,(s,e)=>{if(AdventureActive()) {Notify("Help me back down first");return;}if(state=="sleep") WakeSurprised();else SetState("sleep");});menu.Items.Add(sleepItem);menu.Items.Add(new ToolStripSeparator());
        var walk=new ToolStripMenuItem("Wander around"){Checked=wander,CheckOnClick=true};walk.CheckedChanged+=(s,e)=>{wander=walk.Checked;if(!wander&&state=="walk") SetState("idle");SaveSettings();};menu.Items.Add(walk);
        var climbing=new ToolStripMenuItem("Climb and tumble"){Checked=climbAndTumble,CheckOnClick=true};climbing.CheckedChanged+=(s,e)=>SetClimbing(climbing.Checked);menu.Items.Add(climbing);
        tryClimbItem=new ToolStripMenuItem("Try a climb now",null,(s,e)=>BeginClimb());menu.Items.Add(tryClimbItem);
        helpUpItem=new ToolStripMenuItem("Help turtle up",null,(s,e)=>HelpUp());menu.Items.Add(helpUpItem);
        menu.Opening+=(s,e)=>{tryClimbItem.Enabled=climbAndTumble&&!reducedMotion&&!AdventureActive()&&state!="sleep"&&!AnyAssistantWorking();helpUpItem.Enabled=state=="onBack";};
        var motion=new ToolStripMenuItem("Reduced motion"){Checked=reducedMotion,CheckOnClick=true};motion.CheckedChanged+=(s,e)=>{reducedMotion=motion.Checked;if(reducedMotion&&state=="walk") SetState("idle");if(reducedMotion&&AdventureActive()) {if(state=="onBack") HelpUp();else if(state!="helpUp"&&state!="recover") SafeDescent();}SaveSettings();};menu.Items.Add(motion);
        var size=new ToolStripMenuItem("Size");foreach(int n in new int[]{145,190,235}) {
            int value=n;var item=new ToolStripMenuItem(value==145?"Small":value==190?"Medium":"Large"){Checked=petSize==value};
            item.Click+=(s,e)=>{petSize=value;foreach(ToolStripMenuItem i in size.DropDownItems) i.Checked=i==item;SaveSettings();};size.DropDownItems.Add(item);
        } menu.Items.Add(size);
        var idle=new ToolStripMenuItem("Sleep when idle");foreach(int n in new int[]{0,2,5,15}) {
            int value=n;var item=new ToolStripMenuItem(value==0?"Never":"After "+value+" minutes"){Checked=idleMinutes==value};
            item.Click+=(s,e)=>{idleMinutes=value;foreach(ToolStripMenuItem i in idle.DropDownItems) i.Checked=i==item;SaveSettings();};idle.DropDownItems.Add(item);
        } menu.Items.Add(idle);
        menu.Items.Add("Move to bottom of this screen",null,(s,e)=>ResetAdventure(Screen.FromControl(this).WorkingArea));
        menu.Items.Add("How to use",null,(s,e)=>MessageBox.Show("One-time link: click your Codex message box and press Ctrl+Alt+L.\nAfter linking, clicking the turtle restores that box and opens dictation.\nRelink if the chat input changes or after restarting the turtle.\n\nWithout linking, focus a message box before clicking the turtle.\nWindows voice typing handles the microphone.\nStop with its microphone button. Review and send yourself.\n\nDrag to carry; right-click for pet actions.\nCtrl+Alt+Space also opens dictation if available.","Baby Turtle"));
        menu.Items.Add(new ToolStripSeparator());menu.Items.Add("Quit",null,(s,e)=>Close());
        RefineMenu();
    }
    public static bool RestartRequested {get;private set;}
    bool resourcesDisposed;
    void RestartTurtle() {
        if(!File.Exists(Path.Combine(Path.GetDirectoryName(AssetDirectory),"Turtle.ps1"))) {Notify("Could not find the turtle launcher");return;}
        SaveSettings();RestartRequested=true;Close();
    }
    ToolStripMenuItem MenuGroup(string label,params string[] labels) {
        var group=new ToolStripMenuItem(label);
        foreach(string text in labels) {
            ToolStripItem found=null;
            foreach(ToolStripItem item in menu.Items) if(item.Text==text) {found=item;break;}
            if(found!=null) {menu.Items.Remove(found);group.DropDownItems.Add(found);}
        }
        return group;
    }
    void RefineMenu() {
        var actions=MenuGroup("Pet actions","Give a little pat","Wave","Dance","Go to sleep");
        var behavior=MenuGroup("Behavior && appearance","Wander around","Climb and tumble","Reduced motion","Size","Sleep when idle","Move to bottom of this screen");
        var connections=MenuGroup("Connections","Link message box    Ctrl+Alt+L","Forget linked message box","React to Codex activity","Codex: not connected","Link ChatGPT activity    Ctrl+Alt+G","ChatGPT: not linked","Forget ChatGPT activity link");
        ToolStripItem title=menu.Items[0],voice=menu.Items[1],shot=menu.Items[2],help=null,quit=null;
        foreach(ToolStripItem item in menu.Items) {if(item.Text=="How to use") help=item;if(item.Text=="Quit") quit=item;}
        menu.Items.Clear();
        title.Tag="title";
        menu.Items.AddRange(new ToolStripItem[]{title,voice,shot,new ToolStripSeparator(),tryClimbItem,helpUpItem,actions,behavior,connections,new ToolStripSeparator(),help,new ToolStripMenuItem("Restart turtle",null,(s,e)=>RestartTurtle()),quit});
        menu.Font=new Font("Segoe UI",10);
        StyleMenu(menu);
    }
    static void StyleMenu(ToolStripDropDown dropdown) {
        dropdown.Renderer=new TurtleMenuRenderer();
        dropdown.BackColor=Color.FromArgb(27,39,33);dropdown.ForeColor=Color.FromArgb(237,243,231);
        dropdown.Padding=new Padding(5);dropdown.ShowItemToolTips=true;
        foreach(ToolStripItem item in dropdown.Items) {
            item.Padding=new Padding(3,5,3,5);
            var entry=item as ToolStripMenuItem;
            if(entry==null) continue;
            entry.DropDown.Font=dropdown.Font;
            if(entry.Text.Contains("    ")) {string[] parts=entry.Text.Split(new string[]{"    "},StringSplitOptions.None);entry.Text=parts[0];entry.ShortcutKeyDisplayString=parts[1];}
            StyleMenu(entry.DropDown);
            if(!object.Equals(entry.DropDown.Tag,"styled")) {
                entry.DropDown.Tag="styled";
                entry.DropDownOpening+=(s,e)=>StyleMenu(entry.DropDown);
            }
        }
    }
    sealed class TurtleMenuColors : ProfessionalColorTable {
        public override Color ToolStripDropDownBackground {get{return Color.FromArgb(27,39,33);}}
        public override Color ImageMarginGradientBegin {get{return ToolStripDropDownBackground;}}
        public override Color ImageMarginGradientMiddle {get{return ToolStripDropDownBackground;}}
        public override Color ImageMarginGradientEnd {get{return ToolStripDropDownBackground;}}
        public override Color MenuItemSelected {get{return Color.FromArgb(53,77,53);}}
        public override Color MenuItemBorder {get{return Color.FromArgb(111,151,89);}}
        public override Color MenuBorder {get{return Color.FromArgb(86,111,85);}}
        public override Color SeparatorDark {get{return Color.FromArgb(65,83,68);}}
        public override Color SeparatorLight {get{return ToolStripDropDownBackground;}}
        public override Color CheckBackground {get{return MenuItemSelected;}}
        public override Color CheckSelectedBackground {get{return MenuItemSelected;}}
    }
    sealed class TurtleMenuRenderer : ToolStripProfessionalRenderer {
        public TurtleMenuRenderer():base(new TurtleMenuColors()) {RoundedEdges=false;}
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
            if(object.Equals(e.Item.Tag,"title")) {
                using(var font=new Font(e.TextFont,FontStyle.Bold)) TextRenderer.DrawText(e.Graphics,e.Text,font,e.TextRectangle,Color.FromArgb(177,211,139),e.TextFormat);
                return;
            }
            e.TextColor=(string)e.Item.Tag=="title"?Color.FromArgb(177,211,139):e.Item.Enabled?Color.FromArgb(237,243,231):Color.FromArgb(144,159,147);
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) {e.ArrowColor=Color.FromArgb(177,211,139);base.OnRenderArrow(e);}
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
            var r=e.ImageRectangle;
            using(var p=new Pen(Color.FromArgb(177,211,139),2)) e.Graphics.DrawLines(p,new Point[]{new Point(r.Left+2,r.Top+r.Height/2),new Point(r.Left+6,r.Bottom-3),new Point(r.Right-1,r.Top+2)});
        }
    }
    void LoadSettings() {
        if(settingsPath==null||!File.Exists(settingsPath)) return;
        try {foreach(var line in File.ReadAllLines(settingsPath)) {var pair=line.Split('=');if(pair.Length!=2) continue;int n;
            switch(pair[0]) {
                case "wander":wander=pair[1]=="true";break;
                case "reducedMotion":reducedMotion=pair[1]=="true";break;
                case "size":if(int.TryParse(pair[1],out n)&&(n==145||n==190||n==235)) petSize=n;break;
                case "idleMinutes":if(int.TryParse(pair[1],out n)&&(n==0||n==2||n==5||n==15)) idleMinutes=n;break;
                case "watchCodex":watchCodex=pair[1]=="true";break;
                case "climbAndTumble":climbAndTumble=pair[1]=="true";break;
            }
        }} catch {}
    }
    void SaveSettings() {if(settingsPath==null) return;try {File.WriteAllLines(settingsPath,new string[]{"wander="+wander.ToString().ToLowerInvariant(),"reducedMotion="+reducedMotion.ToString().ToLowerInvariant(),"size="+petSize,"idleMinutes="+idleMinutes,"watchCodex="+watchCodex.ToString().ToLowerInvariant(),"climbAndTumble="+climbAndTumble.ToString().ToLowerInvariant()});} catch {Notify("Could not save preferences");}}
    void SetState(string next) {if(state=="walk"&&next!="walk") restYaw=yaw;if(next=="sleep") sleepHoverArmed=!hover;state=next;stateTime=0;if(next!="walk") targetX=px;if(sleepItem!=null) sleepItem.Text=next=="sleep"?"Wake up":"Go to sleep";}
    void Act(string next) {if(AdventureActive()) {Notify("Help me back down first");return;}SetState(next);}
    void WakeSurprised() {
        sleepInhibitUntil=time+15;
        blinkUntil=0;lastGreeting=time;greetedThisVisit=true;
        SetState("surprised");
    }
    void Notify(string text) {message=text;messageMicrophone=false;messageUntil=time+3;Invalidate();}
    void Dictate() {
        if(inputFocus!=null||inputLink.Read().Linking||screenshotQueued||pendingScreenshot!=null) return;
        if(preferLinkedInput||inputLink.Read().Linked) {
            focusForScreenshot=false;inputFocus=inputLink.FocusAsync();Notify("Focusing your message box...");return;
        }
        IntPtr foreground=GetForegroundWindow();if(foreground==IntPtr.Zero||foreground==Handle) {Notify("Click your message box first");return;}
        SendVoiceTyping();
    }
    void SendVoiceTyping() {
        keybd_event(0x5B,0,0,UIntPtr.Zero);try {keybd_event(0x48,0,0,UIntPtr.Zero);} finally {keybd_event(0x48,0,2,UIntPtr.Zero);keybd_event(0x5B,0,2,UIntPtr.Zero);}
        if(!AnyAssistantWorking()&&!AdventureActive()) SetState("attention");
        Notify("Voice typing requested");messageMicrophone=true;
    }
    void UpdateInputLink() {
        var status=inputLink.Read();
        if(status.Version!=seenLinkVersion) {
            seenLinkVersion=status.Version;
            linkItem.Text=status.Linked?"Relink message box    Ctrl+Alt+L":status.Linking?"Focus chat input, then Ctrl+Alt+L":"Link message box    Ctrl+Alt+L";
            Notify(status.Text);if(status.Linking) messageUntil=time+30;
        }
        if(inputFocus!=null&&inputFocus.IsCompleted) {
            var request=inputFocus;inputFocus=null;
            if(request.IsFaulted||request.IsCanceled) {CancelScreenshot();Notify("Could not focus Codex; please relink");return;}
            var result=request.Result;
            if(result.Success&&inputLink.IsFocusedTarget(result.Window,result.Token)) {
                if(focusForScreenshot) {screenshotPasteWindow=result.Window;screenshotPasteToken=result.Token;screenshotPasteDeadline=time+3;}
                else SendVoiceTyping();
            } else {CancelScreenshot();Notify(result.Success?"Focus changed; click the turtle again":result.Detail);}
        }
    }
    void AttachScreenshotForDisplay(string deviceName) {
        if(inputFocus!=null||screenshotQueued||pendingScreenshot!=null) {Notify("Please wait for the current action");return;}
        var link=inputLink.Read();
        if(!link.Linked||link.Linking) {Notify("Link your message box with Ctrl+Alt+L");return;}
        // Keep the turtle visible and preserve window activation until after capture.
        screenshotDevice=deviceName;hotkeyDictationQueued=false;menu.Close();screenshotQueued=true;screenshotDue=watch.Elapsed.TotalSeconds+.22;
    }
    void CancelScreenshot() {
        screenshotQueued=false;screenshotPasteWindow=IntPtr.Zero;screenshotPasteToken=0;focusForScreenshot=false;
        if(pendingScreenshot!=null) {pendingScreenshot.Dispose();pendingScreenshot=null;}
    }
    void UpdateScreenshot() {
        if(screenshotQueued&&time>=screenshotDue) {
            screenshotQueued=false;
            try {pendingScreenshot=DesktopScreenshot.Capture(screenshotDevice);}
            catch {Notify("Could not capture the desktop");}
            if(pendingScreenshot!=null) {focusForScreenshot=true;inputFocus=inputLink.FocusAsync();Notify("Attaching screenshot...");}
        }
        if(screenshotPasteWindow==IntPtr.Zero||pendingScreenshot==null) return;
        if(GetForegroundWindow()!=screenshotPasteWindow) {CancelScreenshot();Notify("Focus changed; take the screenshot again");return;}
        bool released=(GetAsyncKeyState(0x11)&0x8000)==0&&(GetAsyncKeyState(0x12)&0x8000)==0&&(GetAsyncKeyState(0x10)&0x8000)==0&&(GetAsyncKeyState(0x5B)&0x8000)==0&&(GetAsyncKeyState(0x5C)&0x8000)==0;
        if(!released) {if(time>screenshotPasteDeadline) {CancelScreenshot();Notify("Release modifier keys and retry");}return;}
        if(!inputLink.IsFocusedTarget(screenshotPasteWindow,screenshotPasteToken)) {CancelScreenshot();Notify("Focus changed; take the screenshot again");return;}
        try {
            using(var payload=new ScreenshotClipboardPayload(pendingScreenshot)) Clipboard.SetDataObject(payload.Data,true,5,80);
            if(!inputLink.IsFocusedTarget(screenshotPasteWindow,screenshotPasteToken)) {Notify("Screenshot copied; paste it into your message box");return;}
            keybd_event(0x11,0,0,UIntPtr.Zero);
            try {keybd_event(0x56,0,0,UIntPtr.Zero);} finally {keybd_event(0x56,0,2,UIntPtr.Zero);keybd_event(0x11,0,2,UIntPtr.Zero);}
            Notify("Screenshot pasted - review in Codex");messageUntil=time+5;
        } catch {Notify("Could not paste the screenshot; retry");}
        finally {CancelScreenshot();}
    }
    void OnPointerDown(object sender,MouseEventArgs e) {
        if(e.Button!=MouseButtons.Left) return;pressed=true;dragging=false;pressPoint=Cursor.Position;origin=Location;lastPointer=pressPoint;lastPointerTime=watch.Elapsed.TotalSeconds;dragVX=dragVY=0;Capture=true;
    }
    void OnPointerMove(object sender,MouseEventArgs e) {
        if(!pressed) return;Point p=Cursor.Position;double now=watch.Elapsed.TotalSeconds,dt=Math.Max(.01,now-lastPointerTime);
        if(!dragging&&Math.Abs(p.X-pressPoint.X)+Math.Abs(p.Y-pressPoint.Y)>6) {dragging=true;ClearAdventureOrientation();SetState("carried");}
        if(dragging) {px=origin.X+p.X-pressPoint.X;py=origin.Y+p.Y-pressPoint.Y;dragVX=(p.X-lastPointer.X)/dt;dragVY=(p.Y-lastPointer.Y)/dt;Location=new Point((int)px,(int)py);}
        lastPointer=p;lastPointerTime=now;
    }
    void OnPointerUp(object sender,MouseEventArgs e) {
        bool carried=dragging,hadPress=pressed;pressed=false;dragging=false;Capture=false;if(e.Button==MouseButtons.Right) {menu.Show(this,e.Location);return;}if(e.Button!=MouseButtons.Left||!hadPress) return;
        if(carried) {bool recent=watch.Elapsed.TotalSeconds-lastPointerTime<.12;velocity=recent?Math.Max(-450,Math.Min(450,dragVX)):0;vy=recent?Math.Max(-500,Math.Min(350,dragVY)):0;SetState("drop");} else if(state=="onBack") HelpUp();else Dictate();
    }
    static double Damp(double from,double to,double rate,double dt) {return from+(to-from)*(1-Math.Exp(-rate*dt));}
    static double AngleDifference(double from,double to) {return Math.Atan2(Math.Sin(to-from),Math.Cos(to-from));}
    static double BlendAngle(double from,double to,double amount) {return from+AngleDifference(from,to)*amount;}
    void StartCodexLink() {
        if(!watchCodex||codex!=null) return;
        string home=Environment.GetEnvironmentVariable("CODEX_HOME");
        if(String.IsNullOrWhiteSpace(home)) home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
        codex=new CodexActivityMonitor(home);codex.Start();
    }
    bool AnyAssistantWorking() {
        return codexStatus.Connected&&codexStatus.Phase=="working"||chatgptStatus.Connected&&chatgptStatus.Phase=="working";
    }
    string WorkingLabel() {
        bool c=codexStatus.Connected&&codexStatus.Phase=="working",g=chatgptStatus.Connected&&chatgptStatus.Phase=="working";
        return c&&g?"Codex + ChatGPT are working":g?"ChatGPT is working":"Codex is working";
    }
    void UpdateCodex() {
        if(time<nextCodexPoll) return;
        nextCodexPoll=time+.4;batchingActivity=true;
        try {if(codex!=null) ApplyCodex(codex.Read());ApplyChatGpt(chatgpt.Read());}
        finally {batchingActivity=false;}
        RefreshAssistantPose();
    }
    void ApplyCodex(CodexActivitySnapshot incoming) {
        bool changed=incoming.ThreadId!=codexStatus.ThreadId;codexStatus=incoming;
        if(changed) seenCompletion=incoming.CompletionSequence;
        codexItem.Text=incoming.Connected?"Codex: "+(incoming.Phase=="working"?"working":incoming.Phase=="idle"?"ready":"connected"):"Codex: not connected";
        if(incoming.CompletionSequence>seenCompletion) {
            seenCompletion=incoming.CompletionSequence;
            if(incoming.CompletionKind=="ready") pendingCelebration="Codex reply ready";
            else if(!AnyAssistantWorking()) Notify("Codex stopped");
        }
        if(!batchingActivity) RefreshAssistantPose();
    }
    void ApplyChatGpt(CodexActivitySnapshot incoming) {
        bool changed=incoming.ThreadId!=chatgptStatus.ThreadId;chatgptStatus=incoming;
        if(changed) seenChatGptCompletion=incoming.CompletionSequence;
        chatgptItem.Text=incoming.ThreadId.Length==0?"ChatGPT: not linked":!incoming.Connected?"ChatGPT: unavailable":incoming.Phase=="working"?"ChatGPT: working":incoming.Phase=="idle"?"ChatGPT: ready":"ChatGPT: checking controls";
        if(incoming.CompletionSequence>seenChatGptCompletion) {
            seenChatGptCompletion=incoming.CompletionSequence;pendingCelebration="ChatGPT response ended";
        }
        if(!batchingActivity) RefreshAssistantPose();
    }
    void RefreshAssistantPose() {
        bool free=!dragging&&!pressed&&state!="drop"&&state!="carried"&&!AdventureActive();
        bool manuallyAsleep=state=="sleep";
        if(AnyAssistantWorking()) {
            if(free&&!manuallyAsleep&&state!="attention"&&state!="working") {SetState("working");velocity=0;}
            return;
        }
        if(state=="working") SetState("idle");
        if(pendingCelebration!=null&&free) {
            if(!manuallyAsleep) {SetState("dance");}
            Notify(pendingCelebration);messageUntil=time+7;pendingCelebration=null;
        }
    }
    void UpdateFrame() {
        double now=watch.Elapsed.TotalSeconds,dt=Math.Min(.05,Math.Max(0,now-last));last=now;time=now;
        if(now>=nextTopmostCheck) {nextTopmostCheck=now+.75;KeepOnTop();}
        if(hotkeyDictationQueued) {
            // Send Win+H only after Ctrl+Alt+Space has been released.
            bool released=(GetAsyncKeyState(0x11)&0x8000)==0&&(GetAsyncKeyState(0x12)&0x8000)==0&&(GetAsyncKeyState(0x20)&0x8000)==0;
            if(released) {hotkeyDictationQueued=false;Dictate();}
            else if(now>hotkeyDeadline) {hotkeyDictationQueued=false;Notify("Release the shortcut keys and retry");}
        }
        UpdateInputLink();UpdateScreenshot();UpdateCodex();Step(dt);Invalidate();
    }
    void Step(double dt) {
        var input=new LastInput{size=(uint)Marshal.SizeOf(typeof(LastInput))};
        uint idle=GetLastInputInfo(ref input)?unchecked((uint)Environment.TickCount-input.time):0;
        StepAt(dt,PointToClient(Cursor.Position),idle);
    }
    void StepAt(double dt,Point pointer,uint idle) {
        stateTime+=dt;Rectangle area=Screen.FromControl(this).WorkingArea;
        if(AdventureActive()) {
            bool found=false;
            foreach(var screen in Screen.AllScreens) if(screen.DeviceName==climbDisplay) {found=true;area=screen.WorkingArea;if(area!=climbArea) ResetAdventure(area);break;}
            if(!found) ResetAdventure(area);
        }
        workAmount=Damp(workAmount,state=="working"?1:0,5,dt);
        UpdateInterest(pointer,dt);
        if(time>=sleepInhibitUntil&&idleMinutes>0&&idle>idleMinutes*60000&&state!="sleep"&&state!="working"&&!dragging&&!AdventureActive()) {SetState("sleep");}
        strokeEffort=0;
        bool adventure=UpdateAdventure(dt,area);
        if(!adventure&&state=="drop") {vy+=1200*dt;py+=vy*dt;px+=velocity*dt;velocity*=Math.Exp(-2*dt);if(py>=area.Bottom-Height) {py=area.Bottom-Height;vy=0;velocity=0;SetState("settle");}}
        else if(!adventure&&!dragging) {if(py>area.Bottom-Height) py=area.Bottom-Height;
            if(state=="walk") {
                double dx=targetX-px, facing=Math.Sign(dx)*1.15;
                // Turn first; then push forward with each planted flipper stroke.
                double alignment=Math.Max(0,1-Math.Abs(AngleDifference(yaw,facing))/1.10);
                double desired=Math.Sign(dx)*Math.Min(25,Math.Abs(dx)*2.5)*alignment*(1-interest*.65);
                double step=FlipperStep(desired,dt);velocity=dt>0?step/dt:0;px+=step;
                if(Math.Abs(dx)<2&&Math.Abs(velocity)<4) {SetState("idle");nextAction=4+random.NextDouble()*5;}
            }
            else velocity=Damp(velocity,0,7,dt);
        }
        if(AdventureActive()) {
            px=Math.Max(area.Left-modelBounds.Left+8,Math.Min(area.Right-modelBounds.Right-8,px));
            py=Math.Max(area.Top-modelBounds.Top+(state=="climbTop"?-8:8),Math.Min(area.Bottom-Height,py));
        } else {px=Math.Max(area.Left,Math.Min(area.Right-Width,px));py=Math.Max(area.Top,py);}
        Location=new Point((int)Math.Round(px),(int)Math.Round(py));
        walkAmount=Damp(walkAmount,strokeEffort,7,dt);
        double targetYaw=state=="walk"||state=="climbApproach"?Math.Sign(targetX-px)*1.15:restYaw;
        if(interest>.15&&state!="walk"&&state!="sleep"&&!AdventureActive()) targetYaw=BlendAngle(restYaw,-.30,interest);
        if(state=="climbApproach") targetYaw=climbSide*(Math.Abs(targetX-px)<20?2.65:1.15);
        if(state=="carried") targetYaw=-.4;
        if(state=="climb") targetYaw=climbSide*2.65;
        if(state=="climbDown") targetYaw=-climbSide*1.15;
        if(state=="climbTop") targetYaw=-topDestinationSide*1.15;
        if(state=="tipBack"||state=="tumble"||state=="onBack"||state=="helpUp") targetYaw=fallFacing;
        if(!AdventureActive()) targetYaw=BlendAngle(targetYaw,-.95,workAmount);
        yaw=BlendAngle(yaw,targetYaw,1-Math.Exp(-3.5*dt));
        if(time>nextLook) {idleLookX=(random.NextDouble()-.5)*.32;idleLookY=(random.NextDouble()-.5)*.12;nextLook=time+3+random.NextDouble()*5;}
        if(time>nextBlink) {blinkUntil=time+.13;nextBlink=time+2.5+random.NextDouble()*4;}
        double lookX=Math.Max(-1,Math.Min(1,(pointer.X-Width/2)/180.0));
        double lookY=Math.Max(-1,Math.Min(1,(pointer.Y-modelBounds.Top-modelBounds.Height*.35)/160.0));
        headYaw=Damp(headYaw,state=="sleep"||AdventureActive()?0:lookX*.32*interest+idleLookX*(1-interest),4,dt);
        headPitch=Damp(headPitch,state=="sleep"?-.12:AdventureActive()?0:-lookY*.16*interest+idleLookY*(1-interest),4,dt);
        if(state=="idle"&&stateTime>nextAction&&!hover&&interest<.25&&!menu.Visible&&!pressed) {
            if(climbAndTumble&&!reducedMotion&&time>=nextClimb&&!AnyAssistantWorking()) BeginClimb();
            else if(wander&&!reducedMotion&&random.NextDouble()<.65) {
                // Short, intentional trips with pauses, rather than a continuous screen-wide patrol.
                double distance=70+random.NextDouble()*150;
                targetX=Math.Max(area.Left,Math.Min(area.Right-Width,px+(random.NextDouble()<.5?-distance:distance)));
                if(Math.Abs(targetX-px)>25) SetState("walk");else {nextAction=stateTime+3;}
            } else SetState("curious");
        }
        if(state=="surprised"&&stateTime>.75) SetState("wake");
        if((state=="wave"||state=="happy"||state=="dance"||state=="curious"||state=="wake"||state=="attention"||state=="settle"||state=="greet")&&stateTime>(state=="greet"?1.9:state=="dance"||state=="attention"?4:2.8)) {SetState("idle");nextAction=5+random.NextDouble()*7;}
    }
    void UpdateInterest(Point pointer,double dt) {
        bool inside=pointer.X>=0&&pointer.Y>=0&&pointer.X<Width&&pointer.Y<Height;
        // The transparent corners of the window don't count as touching the turtle.
        hover=inside&&modelBounds.Contains(pointer)&&(rasterPixels==null||rasterPixels[pointer.Y*Width+pointer.X]!=0);
        var near=modelBounds;near.Inflate(65,55);
        bool nearby=near.Contains(pointer)&&state!="carried"&&state!="drop";
        interest=Damp(interest,nearby?1:0,nearby?5:1.8,dt);
        hoverDuration=hover?hoverDuration+dt:0;
        if(state=="sleep"&&!hover) sleepHoverArmed=true;
        if(!hover) greetedThisVisit=false;
        if(hover&&!pressed&&!menu.Visible) {
            if(state=="walk") {SetState("idle");velocity=0;nextAction=6;}
            if(!greetedThisVisit&&hoverDuration>.18&&time-lastGreeting>9&&(state=="idle"||state=="curious"||state=="wake")) {
                lastGreeting=time;greetedThisVisit=true;
                if(!reducedMotion) SetState("greet");
            }
            if(state=="sleep"&&sleepHoverArmed&&hoverDuration>=.10) WakeSurprised();
        }
    }
    V Camera(V v) {
        double depth=v.x*Math.Cos(yaw)-v.y*Math.Sin(yaw),x=v.y*Math.Cos(yaw)+v.x*Math.Sin(yaw);
        const double pitch=.43;
        double y=v.z*Math.Cos(pitch)-depth*Math.Sin(pitch),angle=HangingAngle();
        // Hang from the top in the viewer's plane: the shell and face remain
        // visible, while the flippers reach upward instead of showing the belly.
        return new V((float)(x*Math.Cos(angle)-y*Math.Sin(angle)),(float)(x*Math.Sin(angle)+y*Math.Cos(angle)),(float)(depth*Math.Cos(pitch)+v.z*Math.Sin(pitch)));
    }
    void DrawModel(Graphics g) {
        PrepareRaster();
        float cx=Width/2,scale=petSize,lowest=0;
        // Ground the rest pose for the current angle/size. Animated hops retain their lift.
        foreach(var triangle in SceneTriangles()) foreach(var vertex in new V[]{triangle.a,triangle.b,triangle.c}) lowest=Math.Min(lowest,Camera(AdventurePose(WorkPose(vertex,triangle.part,false),triangle.part)).y);
        float floor=Height-29+lowest*scale;
        float minX=10000,minY=10000,maxX=-10000,maxY=-10000;
        foreach(var tri in SceneTriangles()) {
            bool closed=state=="sleep"||(state!="surprised"&&!reducedMotion&&time<blinkUntil);
            V va=tri.a,vb=tri.b,vc=tri.c;
            if(closed&&tri.eye) {va.z=eyeCenterZ+(va.z-eyeCenterZ)*.10f;vb.z=eyeCenterZ+(vb.z-eyeCenterZ)*.10f;vc.z=eyeCenterZ+(vc.z-eyeCenterZ)*.10f;}
            V a=Camera(Pose(va,tri.part)),b=Camera(Pose(vb,tri.part)),c=Camera(Pose(vc,tri.part));
            V n=V.Cross(b-a,c-a);double len=Math.Sqrt(n.x*n.x+n.y*n.y+n.z*n.z);if(len<.00000001||n.z<=0) continue;
            double light=.80+.20*Math.Max(-.7,(n.x*(-.35)+n.y*.75+n.z*.55)/len);Color col=tri.color;
            int argb=Color.FromArgb(Math.Min(255,(int)(col.R*light)),Math.Min(255,(int)(col.G*light)),Math.Min(255,(int)(col.B*light))).ToArgb();
            a=new V(cx+a.x*scale,floor-a.y*scale,a.z);b=new V(cx+b.x*scale,floor-b.y*scale,b.z);c=new V(cx+c.x*scale,floor-c.y*scale,c.z);
            foreach(var p in new V[]{a,b,c}) {minX=Math.Min(minX,p.x);maxX=Math.Max(maxX,p.x);minY=Math.Min(minY,p.y);maxY=Math.Max(maxY,p.y);}
            RasterTriangle(a,b,c,argb);
        }
        if(state=="onBack") DrawShellGround(g,cx,floor,scale);
        PresentRaster(g);
        modelBounds=RectangleF.FromLTRB(minX-8,minY-8,maxX+8,maxY+8);
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.None;
        if(state!="onBack"&&state!="carried"&&state!="drop"&&state!="climb"&&state!="climbTop"&&state!="climbDown"&&state!="tipBack"&&state!="tumble") for(int i=4;i>=0;i--) using(var b=new SolidBrush(Color.FromArgb(69+i*4,81+i*4,70+i*4))) g.FillEllipse(b,Width/2-68-i*3,Height-30-i,136+i*6,7+i*2);
        DrawModel(g);if(state=="onBack") DrawBadge(g,"Click to help me up",false);
        else if(hover&&hoverDuration>.8&&state!="sleep"&&!dragging&&time>messageUntil) DrawBadge(g,"Click to dictate",false);
        if(time<messageUntil&&state!="onBack") DrawBadge(g,message,messageMicrophone);
        else if(state!="onBack"&&AnyAssistantWorking()) DrawBadge(g,WorkingLabel()+(reducedMotion?"...":new string('.',1+(int)(time*1.8)%3)),false);
        if(state=="greet"&&!reducedMotion) DrawGreeting(g);
        if(state=="sleep") DrawFloatingText(g,"z Z",Color.FromArgb(158,190,174));if(state=="happy") DrawFloatingText(g,"+",Color.FromArgb(241,161,139));if(state=="curious") DrawFloatingText(g,"?",Color.FromArgb(201,220,163));
        if(state=="surprised") DrawFloatingText(g,"!",Color.FromArgb(242,219,145));
        if(state=="tipBack"||state=="tumble"||state=="onBack") DrawAdventureEmotion(g);
    }
    void DrawFloatingText(Graphics g,string text,Color color) {using(var f=new Font("Segoe UI",18,FontStyle.Bold)) using(var b=new SolidBrush(color)) g.DrawString(text,f,b,Width/2+40,Math.Max(48,modelBounds.Top-26)-(reducedMotion?0:(float)(Math.Sin(time*2)*3)));}
    void DrawBadge(Graphics g,string text,bool microphone) {
        int width=Math.Min(282,Math.Max(146,text.Length*7+32));Rectangle r=new Rectangle((Width-width)/2,Math.Max(8,(int)modelBounds.Top-48),width,34);
        using(var b=new SolidBrush(Color.FromArgb(27,44,36))) g.FillRectangle(b,r);using(var p=new Pen(Color.FromArgb(102,140,107))) g.DrawRectangle(p,r);
        if(microphone) {int x=r.Left+14,y=r.Top+10;using(var pen=new Pen(Color.FromArgb(187,222,161),2)) {g.DrawRectangle(pen,x,y,5,9);g.DrawArc(pen,x-3,y+3,11,10,0,180);g.DrawLine(pen,x+2,y+13,x+2,y+17);}}
        using(var f=new Font("Segoe UI",9)) using(var b=new SolidBrush(Color.FromArgb(237,246,226))) using(var fmt=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center}) g.DrawString(text,f,b,new RectangleF(r.Left+(microphone?22:0),r.Top,r.Width-(microphone?22:0),r.Height),fmt);
    }
    public void RenderPreview(string path,string pose,double seconds) {
        clock.Stop();SetState(pose);time=seconds;stateTime=seconds;phase=seconds*3;walkAmount=pose=="walk"||pose=="climb"?1:0;workAmount=pose=="working"?1:0;climbAmount=pose=="climb"?1:0;backPitch=pose=="onBack"||pose=="tumble"||pose=="helpUp"?Math.PI:pose=="tipBack"?Math.PI*.45+Math.PI*.55*Ease(seconds/.55):0;yaw=pose=="climb"||pose=="tipBack"||pose=="tumble"||pose=="onBack"||pose=="helpUp"?-2.65:pose=="working"?-.95:pose=="walk"?1.15:-.55;headYaw=pose=="curious"?.2:0;headPitch=0;blinkUntil=0;hover=false;hoverDuration=0;
        descentHang=0;
        if(pose=="climbDown") {climbAmount=0;walkAmount=1;yaw=1.15;descentHang=1;descentAngle=-Math.PI/2;}
        topHang=pose=="climbTop"?1:0;
        if(pose=="climbTop") {climbAmount=0;walkAmount=1;yaw=-1.15;}
        using(var b=new Bitmap(Width,Height)) {DrawToBitmap(b,new Rectangle(Point.Empty,ClientSize));b.MakeTransparent(Color.Magenta);b.Save(path,System.Drawing.Imaging.ImageFormat.Png);}
    }
    public static void Run(string settings) {
        AssetDirectory=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settings)),"assets");
        Application.EnableVisualStyles();using(var pet=new TurtlePet(settings)) {
            pet.LoadModel();pet.tray=new NotifyIcon{Icon=(Icon)pet.appIcon.Clone(),Text="Baby Turtle - right-click for actions",ContextMenuStrip=pet.menu,Visible=true};
            pet.StartCodexLink();pet.clock.Start();Application.Run(pet);
        }
    }
    protected override void Dispose(bool disposing) {if(disposing&&!resourcesDisposed) {resourcesDisposed=true;if(hotkey) UnregisterHotKey(Handle,1);if(linkHotkey) UnregisterHotKey(Handle,2);if(chatgptHotkey) UnregisterHotKey(Handle,3);clock.Dispose();chatgpt.Dispose();if(pendingScreenshot!=null) {pendingScreenshot.Dispose();pendingScreenshot=null;}inputLink.Dispose();if(codex!=null) codex.Dispose();if(raster!=null) raster.Dispose();if(tray!=null) {tray.Visible=false;if(tray.Icon!=null) tray.Icon.Dispose();tray.Dispose();tray=null;}menu.Dispose();}base.Dispose(disposing);}
}
