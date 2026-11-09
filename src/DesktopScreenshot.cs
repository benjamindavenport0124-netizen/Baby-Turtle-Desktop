using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public static class DesktopScreenshot {
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    public static Rectangle SelectBounds(Screen[] screens,string deviceName,Rectangle allBounds) {
        if(String.IsNullOrEmpty(deviceName)) return allBounds;
        foreach(var screen in screens) if(String.Equals(screen.DeviceName,deviceName,StringComparison.OrdinalIgnoreCase)) return screen.Bounds;
        throw new InvalidOperationException("Selected monitor is disconnected. Choose another monitor.");
    }
    public static Bitmap Capture(string deviceName) {
        IntPtr previous=IntPtr.Zero;
        try {
            // Physical desktop pixels, including monitors with different scaling settings.
            try {previous=SetThreadDpiAwarenessContext(new IntPtr(-4));} catch(EntryPointNotFoundException) {}
            Rectangle bounds=SelectBounds(Screen.AllScreens,deviceName,SystemInformation.VirtualScreen);
            if(bounds.Width<=0||bounds.Height<=0) throw new InvalidOperationException("No desktop is available to capture.");
            var image=new Bitmap(bounds.Width,bounds.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try {using(var graphics=Graphics.FromImage(image)) graphics.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size,CopyPixelOperation.SourceCopy);return image;}
            catch {image.Dispose();throw;}
        } finally {if(previous!=IntPtr.Zero) SetThreadDpiAwarenessContext(previous);}
    }
}
public sealed class ScreenshotClipboardPayload : IDisposable {
    readonly MemoryStream png=new MemoryStream();
    public readonly DataObject Data=new DataObject();
    public ScreenshotClipboardPayload(Bitmap image) {
        // Native bitmap plus PNG for Electron/Chromium image paste support.
        image.Save(png,System.Drawing.Imaging.ImageFormat.Png);png.Position=0;
        Data.SetImage(image);Data.SetData("PNG",false,png);
    }
    public void Dispose() {png.Dispose();}
}
