using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

// English accessibility controls, not chat text. A missing control is unknown, not idle.
public sealed class ChatGptActivityTracker {
    public string Phase="unknown";
    public long CompletionSequence;
    bool working;
    int idleVotes;
    public void Observe(bool stopPresent,bool readyPresent,bool available) {
        if(!available) {Phase="disconnected";idleVotes=0;return;}
        if(stopPresent) {working=true;idleVotes=0;Phase="working";return;}
        if(!readyPresent) {Phase="unknown";idleVotes=0;return;}
        if(working) {
            idleVotes++;
            if(idleVotes<2) {Phase="working";return;}
            CompletionSequence++;working=false;idleVotes=0;
        }
        Phase="idle";
    }
}
public sealed class ChatGptActivityMonitor : IDisposable {
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr handle,out uint pid);
    readonly object gate=new object();
    readonly Timer timer;
    readonly int ownPid=Process.GetCurrentProcess().Id;
    CodexActivitySnapshot snapshot=new CodexActivitySnapshot{Detail="Focus ChatGPT and press Ctrl+Alt+G to link activity"};
    ChatGptActivityTracker tracker=new ChatGptActivityTracker();
    IntPtr window;
    int pid,polling;
    long started,generation;
    bool disposed;
    static Condition ButtonNames(params string[] names) {
        var matches=new Condition[names.Length];
        for(int i=0;i<names.Length;i++) matches[i]=new PropertyCondition(AutomationElement.NameProperty,names[i],PropertyConditionFlags.IgnoreCase);
        return new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new OrCondition(matches));
    }
    static readonly Condition StopControl=new OrCondition(
        ButtonNames("Stop generating","Stop streaming","Stop response","Stop responding","Stop answering"),
        new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.AutomationIdProperty,"stop-button")));
    static readonly Condition ReadyControl=ButtonNames("Send prompt","Send message","Send","Start voice mode","Use voice mode","Voice mode");
    public ChatGptActivityMonitor() {timer=new Timer(s=>Poll(),null,Timeout.Infinite,Timeout.Infinite);}
    public CodexActivitySnapshot Read() {lock(gate) return snapshot.Copy();}
    public void LinkForeground() {
        IntPtr handle=GetForegroundWindow();uint process;GetWindowThreadProcessId(handle,out process);
        if(handle==IntPtr.Zero||process==ownPid) {lock(gate) snapshot.Detail="Focus the ChatGPT app, then press Ctrl+Alt+G";return;}
        try {
            long start=Process.GetProcessById((int)process).StartTime.ToUniversalTime().Ticks;
            lock(gate) {
                if(disposed) return;
                window=handle;pid=(int)process;started=start;generation++;tracker=new ChatGptActivityTracker();
                snapshot=new CodexActivitySnapshot{ThreadId="chatgpt-window:"+handle.ToInt64()+":"+generation,Title="ChatGPT Windows app",Phase="unknown",Detail="Checking ChatGPT response controls"};
                timer.Change(0,900);
            }
        } catch {lock(gate) snapshot.Detail="Could not link this window. Focus ChatGPT and retry.";}
    }
    public void Unlink() {lock(gate) {generation++;window=IntPtr.Zero;tracker=new ChatGptActivityTracker();snapshot=new CodexActivitySnapshot{Detail="ChatGPT activity is not linked"};timer.Change(Timeout.Infinite,Timeout.Infinite);}}
    void Poll() {
        if(Interlocked.Exchange(ref polling,1)!=0) return;
        try {
            IntPtr handle;int process;long start,token;
            lock(gate) {if(disposed||window==IntPtr.Zero) return;handle=window;process=pid;start=started;token=generation;}
            bool available=false,stop=false,ready=false;
            try {
                uint actual;GetWindowThreadProcessId(handle,out actual);
                if(IsWindow(handle)&&actual==process&&Process.GetProcessById(process).StartTime.ToUniversalTime().Ticks==start) {
                    // UIA stays on this worker thread and never invokes controls or reads messages.
                    var root=AutomationElement.FromHandle(handle);
                    stop=root.FindFirst(TreeScope.Descendants,StopControl)!=null;
                    ready=!stop&&root.FindFirst(TreeScope.Descendants,ReadyControl)!=null;
                    available=true;
                }
            } catch { }
            lock(gate) {
                if(disposed||token!=generation) return;
                tracker.Observe(stop,ready,available);
                snapshot.Connected=available;snapshot.Phase=tracker.Phase;
                snapshot.CompletionSequence=tracker.CompletionSequence;snapshot.CompletionKind="response_ended";
                snapshot.Detail=!available?"ChatGPT window unavailable; relink with Ctrl+Alt+G":tracker.Phase=="working"?"ChatGPT response in progress":tracker.Phase=="idle"?"ChatGPT composer is ready":"No recognized response controls. Open a chat; this app version may expose different controls.";
            }
        } finally {Interlocked.Exchange(ref polling,0);}
    }
    public void Dispose() {lock(gate) {disposed=true;generation++;window=IntPtr.Zero;timer.Dispose();}}
}
