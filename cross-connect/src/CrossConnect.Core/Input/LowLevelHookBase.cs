using System.Runtime.InteropServices;

namespace CrossConnect.Core.Input;

/// <summary>
/// Shared plumbing for Win32 low-level hooks (WH_KEYBOARD_LL / WH_MOUSE_LL).
///
/// Windows delivers low-level hook callbacks to the thread that installed the
/// hook, and that thread must service a message loop. This base owns a
/// dedicated background thread running a native GetMessage pump, keeping hook
/// latency off the WPF UI thread and keeping subclasses free of thread plumbing.
///
/// Hook callbacks must return quickly — Windows removes hooks that stall too
/// long — so subclasses only translate the raw event and hand it to a handler
/// delegate; any slow work happens elsewhere.
/// </summary>
public abstract class LowLevelHookBase : IDisposable
{
    // ── Win32 interop (shared by all low-level hooks) ────────────────────────

    private const int WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr Hwnd;
        public uint   Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint   Time;
        public int    PointX;
        public int    PointY;
    }

    private delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(ref MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    // ── State ────────────────────────────────────────────────────────────────

    // Rooted so the GC never collects the delegate while the native hook lives.
    private readonly LowLevelHookProc _proc;
    private readonly ManualResetEventSlim _installCompleted = new(false);

    private Thread?    _thread;
    private uint       _threadId;
    private IntPtr     _hookHandle = IntPtr.Zero;
    private Exception? _installError;
    private bool       _disposed;

    /// <summary>The WH_* identifier for this hook type.</summary>
    protected abstract int HookId { get; }

    /// <summary>
    /// Called on the hook thread for every event. Return true to swallow the
    /// event (prevent local processing); false to let it continue.
    /// </summary>
    protected abstract bool OnEvent(int msg, IntPtr lParam);

    public bool IsInstalled => _thread is { IsAlive: true } && _hookHandle != IntPtr.Zero;

    protected LowLevelHookBase()
    {
        _proc = HookProc;
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public void Install()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsInstalled) return;

        _installCompleted.Reset();

        _thread = new Thread(HookThreadProc)
        {
            Name         = $"CrossConnect.{GetType().Name}",
            IsBackground = true
        };
        _thread.Start();

        // Wait until the hook is actually installed on the new thread so a
        // failure (e.g. hook limit reached) surfaces to the caller immediately.
        _installCompleted.Wait();
        if (_installError is not null)
            throw new InvalidOperationException("Failed to install low-level hook.", _installError);
    }

    public void Uninstall()
    {
        var thread = _thread;
        if (thread is null) return;

        // Wake the message loop; the thread unhooks itself before exiting.
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(TimeSpan.FromSeconds(2));

        _thread     = null;
        _threadId   = 0;
        _hookHandle = IntPtr.Zero;
    }

    // ── Hook thread ──────────────────────────────────────────────────────────

    private void HookThreadProc()
    {
        _threadId   = GetCurrentThreadId();
        _hookHandle = SetWindowsHookEx(HookId, _proc, GetModuleHandle(null), 0);

        if (_hookHandle == IntPtr.Zero)
        {
            int errorCode = Marshal.GetLastWin32Error();
            _installError = errorCode != 0
                ? new System.ComponentModel.Win32Exception(errorCode)
                : new InvalidOperationException("SetWindowsHookEx returned null.");
        }

        _installCompleted.Set();
        if (_hookHandle == IntPtr.Zero) return;

        try
        {
            // Message pump — hook callbacks arrive as messages on this thread.
            var msg = new MSG();
            while (GetMessage(ref msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        finally
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && OnEvent(wParam.ToInt32(), lParam))
            return (IntPtr)1; // eat the event: local applications never see it

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    // ── IDisposable ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Uninstall();
        _installCompleted.Dispose();
    }
}
