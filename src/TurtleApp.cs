using System;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class TurtleApp {
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]
    static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    static void Log(string root,string message,bool append) {
        try {string path=Path.Combine(root,"turtle-startup.log");if(append) File.AppendAllText(path,message);else File.WriteAllText(path,message);} catch { /* Logging is best effort. */ }
    }
    static void ReportStartupError(string root,Exception error,Action<string> showError) {
        Log(root,error+"\r\n",true);
        showError(error.Message);
    }
    [STAThread]
    static void Main() {
        SetCurrentProcessExplicitAppUserModelID("BabyTurtle.DesktopCompanion");
        string root=AppDomain.CurrentDomain.BaseDirectory;
        try {
            Log(root,"Starting Baby Turtle\r\n",false);
            var initial=InitialSessionState.CreateDefault();
            initial.ExecutionPolicy=Microsoft.PowerShell.ExecutionPolicy.Bypass;
            using(var runspace=RunspaceFactory.CreateRunspace(initial)) {
                runspace.ApartmentState=System.Threading.ApartmentState.STA;
                runspace.ThreadOptions=PSThreadOptions.UseCurrentThread;
                runspace.Open();
                using(var shell=PowerShell.Create()) {
                    shell.Runspace=runspace;
                    var output=shell.AddCommand(Path.Combine(root,"Turtle.ps1")).Invoke();
                    foreach(var line in output) Log(root,line+"\r\n",true);
                    foreach(var error in shell.Streams.Error) Log(root,error+"\r\n",true);
                    if(shell.HadErrors) throw new Exception(shell.Streams.Error[0].ToString());
                }
            }
        } catch(Exception error) {
            ReportStartupError(root,error,message=>MessageBox.Show(message,"Baby Turtle could not start",MessageBoxButtons.OK,MessageBoxIcon.Error));
        }
    }
}
