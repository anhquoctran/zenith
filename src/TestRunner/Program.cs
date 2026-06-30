using System;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;

class Program {
    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }
    
    static void Main() {
        object obj = new object();
        try {
            var dxgi = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(Marshal.GetIUnknownForObject(obj));
            Console.WriteLine("Casted");
        } catch (Exception ex) {
            Console.WriteLine("ERR: " + ex.Message);
        }
    }
}
