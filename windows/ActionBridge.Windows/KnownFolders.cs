using System.Runtime.InteropServices;
namespace ActionBridge.Windows;
internal static class KnownFolders {
    [DllImport("shell32.dll")]static extern int SHGetKnownFolderPath(ref Guid id,uint flags,IntPtr token,out IntPtr path);
    public static string Downloads() {
        var id=new Guid("374DE290-123F-4565-9164-39C4925E467B");var pointer=IntPtr.Zero;
        try {var result=SHGetKnownFolderPath(ref id,0,IntPtr.Zero,out pointer);if(result!=0)Marshal.ThrowExceptionForHR(result);return Marshal.PtrToStringUni(pointer)??throw new IOException("Downloads folder is unavailable.");}
        finally{if(pointer!=IntPtr.Zero)Marshal.FreeCoTaskMem(pointer);}
    }
}
