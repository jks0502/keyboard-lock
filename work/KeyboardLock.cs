using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class KeyboardGate
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkK = 0x4B;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    private static readonly HookProc Callback = HookCallback;
    private static IntPtr hook;
    private static bool hotkeyHeld;
    private static int locked;
    private static int stateVersion;

    internal static bool Locked
    {
        get { return Interlocked.CompareExchange(ref locked, 0, 0) == 1; }
        set
        {
            Interlocked.Exchange(ref locked, value ? 1 : 0);
            Interlocked.Increment(ref stateVersion);
        }
    }

    internal static int StateVersion
    {
        get { return Interlocked.CompareExchange(ref stateVersion, 0, 0); }
    }

    internal static void Start()
    {
        using (Process process = Process.GetCurrentProcess())
        using (ProcessModule module = process.MainModule)
            hook = SetWindowsHookEx(WhKeyboardLl, Callback, GetModuleHandle(module.ModuleName), 0);

        if (hook == IntPtr.Zero)
            throw new InvalidOperationException("Unable to install the global keyboard hook.");
    }

    internal static void Stop()
    {
        Locked = false;
        if (hook != IntPtr.Zero)
            UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }

    private static IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0)
            return CallNextHookEx(hook, code, wParam, lParam);

        int message = wParam.ToInt32();
        bool isDown = message == WmKeyDown || message == WmSysKeyDown;
        bool isUp = message == WmKeyUp || message == WmSysKeyUp;
        int key = Marshal.ReadInt32(lParam);

        if (key == VkK && isUp)
            hotkeyHeld = false;

        bool ctrl = (GetAsyncKeyState(VkControl) & 0x8000) != 0;
        bool alt = (GetAsyncKeyState(VkMenu) & 0x8000) != 0;
        if (key == VkK && isDown && ctrl && alt)
        {
            if (!hotkeyHeld)
            {
                hotkeyHeld = true;
                Locked = !Locked;
            }
            return (IntPtr)1;
        }

        if (Locked)
        {
            // Let modifier releases through so the foreground app cannot retain a
            // Ctrl/Alt state after the shortcut changes the lock state.
            if (isUp && (key == VkControl || key == VkMenu))
                return CallNextHookEx(hook, code, wParam, lParam);
            return (IntPtr)1;
        }

        return CallNextHookEx(hook, code, wParam, lParam);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string moduleName);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}

internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem toggleItem;
    private readonly System.Windows.Forms.Timer timer;
    private int displayedVersion = -1;

    internal TrayContext()
    {
        toggleItem = new ToolStripMenuItem("Lock keyboard");
        ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit");
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add(toggleItem);
        menu.Items.Add(exitItem);

        tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Keyboard unlocked (Ctrl+Alt+K)",
            ContextMenuStrip = menu,
            Visible = true
        };

        toggleItem.Click += delegate { KeyboardGate.Locked = !KeyboardGate.Locked; };
        tray.DoubleClick += delegate { KeyboardGate.Locked = !KeyboardGate.Locked; };
        exitItem.Click += delegate { ExitThread(); };

        timer = new System.Windows.Forms.Timer { Interval = 50 };
        timer.Tick += delegate { RefreshState(); };
        timer.Start();
        RefreshState();
    }

    private void RefreshState()
    {
        int version = KeyboardGate.StateVersion;
        if (version == displayedVersion)
            return;

        displayedVersion = version;
        bool isLocked = KeyboardGate.Locked;
        toggleItem.Text = isLocked ? "Unlock keyboard" : "Lock keyboard";
        tray.Text = isLocked
            ? "Keyboard locked (Ctrl+Alt+K to unlock)"
            : "Keyboard unlocked (Ctrl+Alt+K)";
        tray.ShowBalloonTip(800,
            isLocked ? "Keyboard locked" : "Keyboard unlocked",
            isLocked ? "Press Ctrl+Alt+K to unlock." : "Keyboard input has been restored.",
            ToolTipIcon.Info);
    }

    protected override void ExitThreadCore()
    {
        KeyboardGate.Stop();
        timer.Dispose();
        tray.Visible = false;
        tray.Dispose();
        base.ExitThreadCore();
    }
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        bool firstInstance;
        using (Mutex mutex = new Mutex(true, "KeyboardLock-Codex-9B9B69E8", out firstInstance))
        {
            if (!firstInstance)
                return;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            KeyboardGate.Start();
            try
            {
                Application.Run(new TrayContext());
            }
            finally
            {
                KeyboardGate.Stop();
            }
        }
    }
}

