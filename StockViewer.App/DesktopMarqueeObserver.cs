using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Threading;

namespace StockViewer.Desktop;

// Observe desktop drags; never consume or synthesize mouse input. All selection
// changes run on Avalonia's UI thread. No keyboard hook or input log is used.
internal abstract class DesktopMarqueeObserver : IDisposable
{
    public abstract bool IsRunning { get; }
    public abstract void Dispose();
    public static DesktopMarqueeObserver? Start(PopupSelection selection) => OperatingSystem.IsWindows()
        ? new WindowsObserver(selection) : OperatingSystem.IsMacOS() ? new MacObserver(selection) : null;

    private sealed class WindowsObserver : DesktopMarqueeObserver
    {
        private readonly PopupSelection selection;
        private readonly HookCallback callback;
        private readonly Thread thread;
        private volatile bool disposed, running;
        private uint threadId;
        private bool tracking;
        public override bool IsRunning => running;
        public WindowsObserver(PopupSelection selection)
        {
            this.selection = selection; callback = MouseHook;
            thread = new Thread(Run) { IsBackground = true, Name = "StockViewer desktop selection" }; thread.Start();
        }
        private void Run()
        {
            threadId = GetCurrentThreadId(); PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            var hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0); running = hook != IntPtr.Zero;
            try
            {
                while (!disposed && running && GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
                { TranslateMessage(ref message); DispatchMessage(ref message); }
            }
            finally { if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook); running = false; }
        }
        private IntPtr MouseHook(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && !disposed && data != IntPtr.Zero)
            {
                var kind = message.ToInt32();
                if (kind is 0x0201 or 0x0202 or 0x0200)
                {
                    var location = Marshal.PtrToStructure<MouseData>(data).Point;
                    var point = new PixelPoint(location.X, location.Y);
                    if (kind == 0x0201)
                    {
                        tracking = IsDesktop(location);
                        if (tracking)
                        {
                            var additive = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                            Post(() => selection.DesktopDown(point, additive));
                        }
                    }
                    else if (tracking)
                    {
                        if (kind == 0x0202) { tracking = false; Post(() => selection.DesktopUp(point)); }
                        else Post(() => selection.DesktopMove(point));
                    }
                }
            }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        }
        private void Post(Action action) => Dispatcher.UIThread.Post(() => { if (!disposed) action(); });
        private static bool IsDesktop(NativePoint point)
        {
            var window = WindowFromPoint(point);
            if (window == IntPtr.Zero) return false;
            var root = GetAncestor(window, 2);
            var name = new StringBuilder(128); GetClassName(root, name, name.Capacity);
            // Explorer's desktop/desktop-icons hierarchy; a normal Explorer
            // file window has class CabinetWClass and is deliberately excluded.
            return name.ToString() is "Progman" or "WorkerW";
        }
        public override void Dispose()
        {
            disposed = true;
            if (threadId != 0) PostThreadMessage(threadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
        }
        private delegate IntPtr HookCallback(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct MouseData { public NativePoint Point; public uint MouseDataValue, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct Message { public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public NativePoint Point; public uint Private; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SetWindowsHookEx(int type, HookCallback hook, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
        [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, IntPtr window, uint min, uint max, uint remove);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
        [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    }

    private sealed class MacObserver : DesktopMarqueeObserver
    {
        private readonly PopupSelection selection;
        private readonly DesktopCallback callback;
        private IntPtr monitor;
        private bool disposed;
        public override bool IsRunning => monitor != IntPtr.Zero;
        public MacObserver(PopupSelection selection)
        { this.selection = selection; callback = Receive; monitor = StartMonitor(callback); }
        private void Receive(int kind, double x, double y, int additive)
        {
            if (disposed) return;
            var point = new PixelPoint((int)Math.Round(x), (int)Math.Round(y));
            if (kind == 1) selection.DesktopDown(point, additive != 0);
            else if (kind == 2) selection.DesktopMove(point);
            else if (kind == 3) selection.DesktopUp(point);
        }
        public override void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (monitor != IntPtr.Zero) { StopMonitor(monitor); monitor = IntPtr.Zero; }
            GC.KeepAlive(callback);
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DesktopCallback(int kind, double x, double y, int additive);
        [DllImport("libStockViewerDesktop.dylib", EntryPoint = "sv_desktop_start")] private static extern IntPtr StartMonitor(DesktopCallback callback);
        [DllImport("libStockViewerDesktop.dylib", EntryPoint = "sv_desktop_stop")] private static extern void StopMonitor(IntPtr monitor);
    }
}
