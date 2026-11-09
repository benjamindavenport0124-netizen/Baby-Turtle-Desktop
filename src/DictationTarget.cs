using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

public sealed class InputLinkStatus {
    public bool Linked, Linking;
    public long Version;
    public string Text="No message box linked";
}
public sealed class InputFocusResult {
    public bool Success;
    public IntPtr Window;
    public long Token;
    public string Detail="Link your Codex message box first";
}
public sealed class DictationTarget : IDisposable {
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    readonly object gate=new object();
    readonly int ownPid=Process.GetCurrentProcess().Id;
    readonly Timer timer;
    AutomationElement target;
    IntPtr window;
    int pid;
    long processStart;
    bool linking,disposed;
    DateTime linkUntil;
    long version;
    long epoch;
    string text="No message box linked";
    public DictationTarget() {timer=new Timer(s=>ExpireLink(),null,Timeout.Infinite,Timeout.Infinite);}
    public InputLinkStatus Read() {lock(gate) return new InputLinkStatus{Linked=target!=null,Linking=linking,Version=version,Text=text};}
    public void BeginLink() {
        lock(gate) {epoch++;linking=true;linkUntil=DateTime.UtcNow.AddSeconds(30);text="Focus chat input, then Ctrl+Alt+L";version++;timer.Change(250,250);}
    }
    public void Forget() {lock(gate) {epoch++;target=null;linking=false;text="No message box linked";version++;timer.Change(Timeout.Infinite,Timeout.Infinite);}}
    void ExpireLink() {lock(gate) {if(!disposed&&linking&&DateTime.UtcNow>linkUntil) {linking=false;text="Link timed out; try linking again";version++;timer.Change(Timeout.Infinite,Timeout.Infinite);}}}
    public void CaptureFocused() {
        lock(gate) {if(disposed) return;if(!linking) {epoch++;linking=true;linkUntil=DateTime.UtcNow.AddSeconds(30);text="Focus chat input, then Ctrl+Alt+L";version++;timer.Change(250,250);}}
        ThreadPool.QueueUserWorkItem(s=>Capture());
    }
    static bool Editable(AutomationElement element) {
        var current=element.Current;
        if(!current.IsEnabled||!current.IsKeyboardFocusable||current.IsPassword) return false;
        object pattern;
        if(element.TryGetCurrentPattern(ValuePattern.Pattern,out pattern)) return !((ValuePattern)pattern).Current.IsReadOnly;
        return current.ControlType==ControlType.Edit || current.ControlType==ControlType.Document&&(bool)element.GetCurrentPropertyValue(AutomationElement.IsTextPatternAvailableProperty);
    }
    void Capture() {
        DateTime deadline;
        lock(gate) {
            if(disposed||!linking) return;
            if(DateTime.UtcNow>linkUntil) {linking=false;text="Link timed out; try Link message box again";version++;timer.Change(Timeout.Infinite,Timeout.Infinite);return;}
            deadline=linkUntil;
        }
        // Provider calls stay on a worker thread. No input values or chat text are read.
        try {
            var element=AutomationElement.FocusedElement;
            if(element==null||element.Current.ProcessId==ownPid||!Editable(element)) return;
            AutomationElement parent=element;IntPtr handle=IntPtr.Zero;
            for(int i=0;i<32&&parent!=null;i++) {
                int native=parent.Current.NativeWindowHandle;
                if(native!=0) {handle=GetAncestor(new IntPtr(native),2);break;}
                parent=TreeWalker.ControlViewWalker.GetParent(parent);
            }
            if(handle==IntPtr.Zero||handle!=GetForegroundWindow()) return;
            uint process;GetWindowThreadProcessId(handle,out process);
            long started=Process.GetProcessById((int)process).StartTime.ToUniversalTime().Ticks;
            lock(gate) {
                if(disposed||!linking||linkUntil!=deadline||DateTime.UtcNow>deadline) return;
                epoch++;target=element;window=handle;pid=(int)process;processStart=started;linking=false;
                text="Message box linked";version++;timer.Change(Timeout.Infinite,Timeout.Infinite);
            }
        } catch { /* Some controls do not expose accessibility. Keep waiting for an editable field. */ }
    }
    public Task<InputFocusResult> FocusAsync() {
        AutomationElement element;IntPtr handle;int process;long started,token;
        lock(gate) {element=target;handle=window;process=pid;started=processStart;token=epoch;}
        return Task.Run(()=> {
            var result=new InputFocusResult();
            if(element==null) return result;
            try {
                uint actual;GetWindowThreadProcessId(handle,out actual);
                if(!IsWindow(handle)||actual!=process||Process.GetProcessById(process).StartTime.ToUniversalTime().Ticks!=started) throw new ElementNotAvailableException();
                if(!Editable(element)) {result.Detail="Linked box is unavailable; relink it";return result;}
                if(!Current(token)) {result.Detail="Message box link changed; retry";return result;}
                if(IsIconic(handle)) ShowWindow(handle,9);
                SetForegroundWindow(handle);
                Thread.Sleep(70);
                if(GetForegroundWindow()!=handle) {result.Detail="Windows could not bring Codex forward";return result;}
                if(!Current(token)) {result.Detail="Message box link changed; retry";return result;}
                element.SetFocus();Thread.Sleep(80);
                if(!Current(token)||!element.Current.HasKeyboardFocus||GetForegroundWindow()!=handle) {result.Detail="Could not focus the linked message box";return result;}
                result.Success=true;result.Window=handle;result.Token=token;result.Detail="Message box focused";return result;
            } catch {
                lock(gate) {if(target==element) {target=null;text="Message box changed; please relink";version++;}}
                result.Detail="Message box changed; please relink";return result;
            }
        });
    }
    bool Current(long token) {lock(gate) return !disposed&&epoch==token;}
    public bool IsFocusedTarget(IntPtr expectedWindow,long token) {
        AutomationElement element;int process;long started;
        lock(gate) {
            if(disposed||epoch!=token||window!=expectedWindow||target==null) return false;
            element=target;process=pid;started=processStart;
        }
        try {
            uint actual;GetWindowThreadProcessId(expectedWindow,out actual);
            return Current(token)&&IsWindow(expectedWindow)&&actual==process&&
                Process.GetProcessById(process).StartTime.ToUniversalTime().Ticks==started&&
                GetForegroundWindow()==expectedWindow&&Editable(element)&&element.Current.HasKeyboardFocus&&Current(token);
        } catch {return false;}
    }
    public void Dispose() {lock(gate) {epoch++;disposed=true;linking=false;target=null;timer.Dispose();}}
}
