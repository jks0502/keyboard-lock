Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$isFirstInstance = $false
$instanceMutex = [System.Threading.Mutex]::new($true, 'KeyboardLock-Codex-9B9B69E8', [ref]$isFirstInstance)
if (-not $isFirstInstance) {
    $instanceMutex.Dispose()
    exit
}

Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

public static class KeyboardGate
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_K = 0x4B;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    private static readonly HookProc Callback = HookCallback;
    private static IntPtr hook = IntPtr.Zero;
    private static bool hotkeyHeld;
    private static int togglePending;

    public static bool Locked { get; set; }

    public static bool ConsumeToggleRequest()
    {
        return Interlocked.Exchange(ref togglePending, 0) == 1;
    }

    public static void Start()
    {
        using (Process process = Process.GetCurrentProcess())
        using (ProcessModule module = process.MainModule)
            hook = SetWindowsHookEx(WH_KEYBOARD_LL, Callback,
                GetModuleHandle(module.ModuleName), 0);

        if (hook == IntPtr.Zero)
            throw new InvalidOperationException("Unable to install the keyboard hook.");
    }

    public static void Stop()
    {
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }

    private static IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int message = wParam.ToInt32();
            bool isDown = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
            bool isUp = message == WM_KEYUP || message == WM_SYSKEYUP;
            int key = Marshal.ReadInt32(lParam);
            bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

            if (key == VK_K && isUp)
            {
                hotkeyHeld = false;
                if (Locked) return (IntPtr)1;
            }

            if (key == VK_K && ctrl && alt)
            {
                if (isDown && !hotkeyHeld)
                {
                    hotkeyHeld = true;
                    Interlocked.Exchange(ref togglePending, 1);
                }
                return (IntPtr)1;
            }

            if (Locked) return (IntPtr)1;
        }
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback,
        IntPtr module, uint threadId);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code,
        IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string moduleName);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
'@

$appContext = [System.Windows.Forms.ApplicationContext]::new()
$tray = [System.Windows.Forms.NotifyIcon]::new()
$tray.Icon = [System.Drawing.SystemIcons]::Application
$tray.Text = 'Keyboard unlocked (Ctrl+Alt+K)'
$tray.Visible = $true

$toggleItem = [System.Windows.Forms.ToolStripMenuItem]::new('Lock keyboard')
$exitItem = [System.Windows.Forms.ToolStripMenuItem]::new('Exit')
$menu = [System.Windows.Forms.ContextMenuStrip]::new()
[void]$menu.Items.Add($toggleItem)
[void]$menu.Items.Add($exitItem)
$tray.ContextMenuStrip = $menu

$toggle = {
    [KeyboardGate]::Locked = -not [KeyboardGate]::Locked
    if ([KeyboardGate]::Locked) {
        $toggleItem.Text = 'Unlock keyboard'
        $tray.Text = 'Keyboard locked (Ctrl+Alt+K to unlock)'
        $tray.ShowBalloonTip(1000, 'Keyboard locked', 'Press Ctrl+Alt+K to unlock, or use the tray menu.', 'Info')
    } else {
        $toggleItem.Text = 'Lock keyboard'
        $tray.Text = 'Keyboard unlocked (Ctrl+Alt+K)'
        $tray.ShowBalloonTip(800, 'Keyboard unlocked', 'Keyboard input has been restored.', 'Info')
    }
}

$toggleItem.Add_Click($toggle)
$tray.Add_DoubleClick($toggle)
$exitItem.Add_Click({
    [KeyboardGate]::Locked = $false
    [KeyboardGate]::Stop()
    $tray.Visible = $false
    $appContext.ExitThread()
})

$pollTimer = [System.Windows.Forms.Timer]::new()
$pollTimer.Interval = 50
$pollTimer.Add_Tick({
    if ([KeyboardGate]::ConsumeToggleRequest()) {
        $toggle.Invoke()
    }
})
$pollTimer.Start()

try {
    [KeyboardGate]::Start()
    [System.Windows.Forms.Application]::Run($appContext)
} finally {
    [KeyboardGate]::Locked = $false
    [KeyboardGate]::Stop()
    $pollTimer.Dispose()
    $tray.Dispose()
    $menu.Dispose()
    $appContext.Dispose()
    $instanceMutex.ReleaseMutex()
    $instanceMutex.Dispose()
}

