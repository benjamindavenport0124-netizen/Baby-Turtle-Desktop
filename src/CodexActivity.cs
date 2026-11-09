using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Web.Script.Serialization;

// Read-only compatibility adapter for local Codex desktop metadata and rollout events.
// These files are an internal format: unknown/missing data is reported, never simulated.
public sealed class CodexActivitySnapshot {
    public string ThreadId="", Title="", Phase="disconnected", Detail="Looking for a local Codex chat", CompletionKind="";
    public long CompletionSequence;
    public bool Connected;
    public CodexActivitySnapshot Copy() {return (CodexActivitySnapshot)MemberwiseClone();}
}
public sealed class CodexActivityMonitor : IDisposable {
    readonly string home;
    readonly object gate=new object();
    readonly object snapshotGate=new object(), lifecycleGate=new object();
    CodexActivitySnapshot published=new CodexActivitySnapshot();
    readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=16*1024*1024};
    readonly Dictionary<string,string> paths=new Dictionary<string,string>();
    readonly CodexActivitySnapshot snapshot=new CodexActivitySnapshot();
    Timer timer;
    FileStream stream;
    Decoder decoder=Encoding.UTF8.GetDecoder();
    string pending="", activeTurn="", selected="", selectedPath="";
    string currentPhase="unknown";
    bool initial=true;
    volatile bool disposed;
    DateTime nextLocate=DateTime.MinValue;
    public CodexActivityMonitor(string codexHome) {home=codexHome;}
    public void Start() {lock(lifecycleGate) {if(timer==null&&!disposed) timer=new Timer(s=>Poll(),null,0,750);}}
    public CodexActivitySnapshot Read() {lock(snapshotGate) return published.Copy();}
    static Dictionary<string,object> Obj(object value) {return value as Dictionary<string,object>;}
    static string Str(Dictionary<string,object> value,string key) {object v;return value!=null&&value.TryGetValue(key,out v)&&v!=null?v.ToString():"";}
    static object Get(Dictionary<string,object> value,string key) {object v;return value!=null&&value.TryGetValue(key,out v)?v:null;}
    string ReadShared(string path) {
        using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
        using(var reader=new StreamReader(file,Encoding.UTF8)) return reader.ReadToEnd();
    }
    string RecentThread() {
        var root=Obj(json.DeserializeObject(ReadShared(Path.Combine(home,".codex-global-state.json"))));
        var atoms=Obj(Get(root,"electron-persisted-atom-state"));
        var times=Obj(Get(atoms,"thread-user-activity-times-v1"));
        string choice="";long latest=0;
        if(times!=null) foreach(var entry in times) {
            long stamp;if(!long.TryParse(entry.Value.ToString(),out stamp)||stamp<=latest) continue;
            object[] key;
            try {key=json.DeserializeObject(entry.Key) as object[];} catch {continue;}
            Guid id;
            if(key==null||key.Length!=2||Convert.ToString(key[0])!="local"||!Guid.TryParse(Convert.ToString(key[1]),out id)) continue;
            choice=id.ToString();latest=stamp;
        }
        return choice;
    }
    string Title(string id) {
        try {
            using(var file=new FileStream(Path.Combine(home,"session_index.jsonl"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var reader=new StreamReader(file,Encoding.UTF8)) {
                string line,title="";
                while(!disposed&&(line=reader.ReadLine())!=null) {
                    if(line.IndexOf(id,StringComparison.Ordinal)<0) continue;
                    try {var record=Obj(json.DeserializeObject(line));if(Str(record,"id")==id) title=Str(record,"thread_name");} catch {}
                }
                return title.Length==0?"Local Codex chat":title;
            }
        } catch {return "Local Codex chat";}
    }
    void CloseStream() {if(stream!=null) {stream.Dispose();stream=null;}pending="";decoder=Encoding.UTF8.GetDecoder();activeTurn="";currentPhase="unknown";initial=true;}
    public void Poll() {
        if(!Monitor.TryEnter(gate)) return;
        try {
            if(disposed) return;
            try {
                string id=RecentThread();
                if(id.Length==0) {CloseStream();selected="";snapshot.Connected=false;snapshot.Phase="disconnected";snapshot.Detail="No recently used local Codex chat found";return;}
                if(id!=selected) {
                    CloseStream();selected=id;selectedPath="";nextLocate=DateTime.MinValue;
                    snapshot.ThreadId=id;snapshot.Title=Title(id);snapshot.Connected=false;snapshot.Phase="unknown";snapshot.Detail="Connecting to "+snapshot.Title;
                }
                if(stream==null) {
                    if(DateTime.UtcNow<nextLocate) return;
                    nextLocate=DateTime.UtcNow.AddSeconds(10);
                    if(!paths.TryGetValue(id,out selectedPath)||!File.Exists(selectedPath)) {
                        selectedPath="";
                        string sessions=Path.Combine(home,"sessions");
                        if(Directory.Exists(sessions)) foreach(string path in Directory.EnumerateFiles(sessions,"*"+id+"*.jsonl",SearchOption.AllDirectories)) {selectedPath=path;break;}
                        if(selectedPath.Length>0) paths[id]=selectedPath;
                    }
                    if(selectedPath.Length==0) {snapshot.Connected=false;snapshot.Phase="disconnected";snapshot.Detail="No local session file for this chat";return;}
                    stream=new FileStream(selectedPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                }
                if(!SameFile(stream,selectedPath)||new FileInfo(selectedPath).Length<stream.Position) {CloseStream();snapshot.Connected=false;snapshot.Phase="unknown";snapshot.Detail="Reconnecting after session file changed";nextLocate=DateTime.MinValue;return;}
                byte[] bytes=new byte[65536];char[] chars=new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
                int processed=0,n;
                // Bound each polling pass so very large histories do not monopolize the worker.
                while(!disposed&&processed<2*1024*1024&&(n=stream.Read(bytes,0,bytes.Length))>0) {
                    processed+=n;int count=decoder.GetChars(bytes,0,n,chars,0,false);pending+=new string(chars,0,count);
                    int offset=0,end;
                    while((end=pending.IndexOf('\n',offset))>=0) {ParseLine(pending.Substring(offset,end-offset));offset=end+1;}
                    if(offset>0) pending=pending.Substring(offset);
                    if(pending.Length>16*1024*1024) {snapshot.Connected=false;snapshot.Phase="unknown";snapshot.Detail="Session event too large to read";CloseStream();return;}
                }
                bool caughtUp=stream.Position>=stream.Length;
                if(initial&&caughtUp) initial=false;
                snapshot.Connected=caughtUp&&!initial;
                snapshot.Phase=snapshot.Connected?currentPhase:"unknown";
                snapshot.Detail=snapshot.Connected?(currentPhase=="unknown"?"Connected; waiting for a lifecycle event":"Following "+snapshot.Title):"Reading local chat activity";
            } catch(Exception e) {
                snapshot.Connected=false;snapshot.Phase="disconnected";
                snapshot.Detail=e is UnauthorizedAccessException?"Cannot read local Codex activity":"Local Codex activity is unavailable";
                CloseStream();nextLocate=DateTime.UtcNow.AddSeconds(3);
            }
        } finally {
            if(disposed) CloseStream();
            lock(snapshotGate) {if(!disposed) published=snapshot.Copy();}
            Monitor.Exit(gate);
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    struct FileIdentity {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation,Access,Write;
        public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;
    }
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformationByHandle(SafeFileHandle handle,out FileIdentity info);
    static bool SameFile(FileStream current,string path) {
        using(var probe=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
            FileIdentity a,b;
            if(!GetFileInformationByHandle(current.SafeFileHandle,out a)||!GetFileInformationByHandle(probe.SafeFileHandle,out b)) throw new IOException("Cannot identify session file");
            return a.Volume==b.Volume&&a.IndexHigh==b.IndexHigh&&a.IndexLow==b.IndexLow;
        }
    }
    void ParseLine(string line) {
        if(line.IndexOf("event_msg",StringComparison.Ordinal)<0) return;
        try {
            var record=Obj(json.DeserializeObject(line));if(Str(record,"type")!="event_msg") return;
            var payload=Obj(Get(record,"payload"));string kind=Str(payload,"type"),turn=Str(payload,"turn_id"),root=Str(payload,"root_turn_id");
            if(root.Length>0&&turn.Length>0&&root!=turn) return; // Ignore child turns.
            if(kind=="task_started"&&turn.Length>0) {activeTurn=turn;currentPhase="working";}
            else if(kind=="task_complete"||kind=="turn_aborted"||kind=="task_failed") {
                // Ignore delayed completion from an older turn while a newer one is running.
                if(activeTurn.Length>0&&turn.Length>0&&activeTurn!=turn) return;
                bool wasWorking=currentPhase=="working";
                currentPhase=kind=="task_complete"?"idle":"stopped";activeTurn="";
                if(!initial&&wasWorking) {snapshot.CompletionSequence++;snapshot.CompletionKind=kind=="task_complete"?"ready":"stopped";}
            }
        } catch { /* Ignore malformed records; a partial line is buffered by Poll. */ }
    }
    public void Dispose() {
        lock(lifecycleGate) {disposed=true;if(timer!=null) {timer.Dispose();timer=null;}}
        lock(snapshotGate) {published=published.Copy();published.Connected=false;published.Phase="disconnected";}
        // Busy workers close their own stream; shutdown never waits for disk I/O.
        if(Monitor.TryEnter(gate)) {try {CloseStream();} finally {Monitor.Exit(gate);}}
    }
}
