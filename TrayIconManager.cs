using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Win32;

namespace BrowserPicker;

/// <summary>
/// Manages the system tray icon and context menu via Shell_NotifyIcon P/Invoke.
/// Must be created and used on the UI (STA) thread.
/// </summary>
internal sealed class TrayIconManager : IDisposable
{
    // -----------------------------------------------------------------------
    // Win32 constants
    // -----------------------------------------------------------------------
    private const uint NIM_ADD    = 0x00;
    private const uint NIM_DELETE = 0x02;
    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON   = 0x02;
    private const uint NIF_TIP    = 0x04;

    private const uint WM_APP_TRAY       = 0x8001;
    private const uint WM_RBUTTONUP      = 0x0205;
    private const uint WM_LBUTTONDBLCLK  = 0x0203;
    private const uint WM_DESTROY        = 0x0002;
    private const uint WM_SETICON        = 0x0080;
    private const int  ICON_SMALL        = 0;
    private const int  ICON_BIG          = 1;

    private const uint MF_STRING    = 0x00;
    private const uint MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 0x02;
    private const uint TPM_RETURNCMD   = 0x100;

    private const int CMD_RESTART   = 1;
    private const int CMD_ABOUT     = 2;
    private const int CMD_EXIT      = 3;
    private const int CMD_AUTOSTART = 4;

    private const uint MF_CHECKED   = 0x0008;

    private const string AutoStartKeyPath   = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValueName = "BrowserPicker";

    // -----------------------------------------------------------------------
    // P/Invoke structures
    // -----------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint   cbSize;
        public IntPtr hWnd;
        public uint   uID;
        public uint   uFlags;
        public uint   uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint   dwState;
        public uint   dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint   uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint   dwInfoFlags;
        public Guid   guidItem;      // Windows 2000+ — necessário para cbSize correcto no Vista+
        public IntPtr hBalloonIcon;  // Windows Vista+
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int    cbSize;
        public uint   style;
        public IntPtr lpfnWndProc;
        public int    cbClsExtra;
        public int    cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public IntPtr lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private delegate IntPtr WNDPROC(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    // -----------------------------------------------------------------------
    // P/Invoke declarations
    // -----------------------------------------------------------------------
    [DllImport("Shell32.dll", SetLastError = true)]
    static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int    iIcon;
        public uint   dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON      = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_LARGEICON = 0x000000000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y,
        int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern uint RegisterWindowMessage(string lpString);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate bool WNDENUMPROC(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(WNDENUMPROC lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // PostMessage em vez de SendMessage — não bloqueia o thread de UI
    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    // Carregar ícone de ficheiro .ico com dimensão exacta
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    // Carregar ícone de recurso PE (HMODULE + resource ID)
    // lpIconName = MAKEINTRESOURCE(id) = (IntPtr)id
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr hIcon);

    private const uint IMAGE_ICON      = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;

    // -----------------------------------------------------------------------
    // Fields
    // -----------------------------------------------------------------------
    private WNDPROC?       _wndProc;    // must be kept alive to prevent GC collection
    private WNDENUMPROC?   _enumProc;   // must be kept alive to prevent GC collection
    private IntPtr         _hwnd;
    private IntPtr         _hIcon;       // HICON (32x32) — WM_SETICON ICON_BIG
    private IntPtr         _hIconSmall;  // HICON (16x16) — bandeja do sistema
    private NOTIFYICONDATA _nid;
    private uint           _wmTaskbarCreated;
    private bool           _disposed;

    private readonly DispatcherQueue _dq;

    public Action? OnOpen    { get; set; }
    public Action? OnRestart { get; set; }
    public Action? OnAbout   { get; set; }

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------
    public TrayIconManager(DispatcherQueue dispatcherQueue)
    {
        _dq = dispatcherQueue;
        _wmTaskbarCreated = RegisterWindowMessage("TaskbarCreated");
        CreateMessageWindow();
        AddTrayIcon();
    }

    // -----------------------------------------------------------------------
    // Setup
    // -----------------------------------------------------------------------
    private void CreateMessageWindow()
    {
        _wndProc = WndProcCallback;

        var className = "BPTray_" + Environment.ProcessId;
        var wc = new WNDCLASSEX
        {
            cbSize        = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance     = GetModuleHandle(null),
            lpszClassName = className,
            lpszMenuName  = IntPtr.Zero
        };

        RegisterClassEx(ref wc);

        // HWND_MESSAGE (-3) = invisible message-only window
        _hwnd = CreateWindowEx(0, className, string.Empty, 0,
            0, 0, 0, 0,
            new IntPtr(-3), IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
    }

    private void AddTrayIcon()
    {
        var exePath = Environment.ProcessPath ?? string.Empty;

        // 1.ª tentativa: SHGetFileInfo — a mesma forma que o Explorer usa;
        //   funciona com .NET 8 single-file porque opera por path, não por HMODULE.
        var sfLarge = new SHFILEINFO();
        var sfSmall = new SHFILEINFO();
        SHGetFileInfo(exePath, 0, ref sfLarge, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
        SHGetFileInfo(exePath, 0, ref sfSmall, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_SMALLICON);
        _hIcon      = sfLarge.hIcon;
        _hIconSmall = sfSmall.hIcon != IntPtr.Zero ? sfSmall.hIcon : _hIcon;

        // 2.ª tentativa: LoadImage do ficheiro app.ico
        if (_hIcon == IntPtr.Zero)
        {
            var exeDir  = Path.GetDirectoryName(exePath) ?? string.Empty;
            var icoPath = Path.Combine(exeDir, "app.ico");
            if (!File.Exists(icoPath))
                icoPath = Path.Combine(exeDir, "Assets", "app.ico");
            if (File.Exists(icoPath))
            {
                _hIconSmall = LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                _hIcon      = LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
                if (_hIconSmall == IntPtr.Zero) _hIconSmall = _hIcon;
            }
        }

        var cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>();

        _nid = new NOTIFYICONDATA
        {
            cbSize           = cbSize,
            hWnd             = _hwnd,
            uID              = 1,
            uFlags           = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_APP_TRAY,
            hIcon            = _hIconSmall,
            szTip            = "BrowserPicker"
        };

        Shell_NotifyIcon(NIM_ADD, ref _nid);

        // Diferir a aplicação do ícone para depois da inicialização completa do WinUI 3
        // (prioridade Low = após todos os pending tasks de alta prioridade)
        _dq.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => ApplyIconToProcessWindows(exePath));
    }

    /// <summary>
    /// Enumera todas as janelas top-level do processo actual e aplica o ícone
    /// da aplicação via WM_SETICON. Resolve a ausência de ícone no Task Manager
    /// causada pela janela interna do WinUI 3.
    /// </summary>
    private void ApplyIconToProcessWindows(string exePath)
    {
        // Reutilizar os handles já extraídos em AddTrayIcon (mesmo ICO, mesmas dimensões)
        var hLarge = _hIcon;
        var hSmall = _hIconSmall;

        var pid = (uint)Environment.ProcessId;

        // Manter delegado em campo para evitar GC durante a enumeração
        _enumProc = (hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint windowPid);
            if (windowPid == pid)
            {
                // PostMessage: não-bloqueante, evita deadlock com o rendering thread do WinUI 3
                PostMessage(hwnd, WM_SETICON, new IntPtr(ICON_SMALL), hSmall);
                PostMessage(hwnd, WM_SETICON, new IntPtr(ICON_BIG),   hLarge);
            }
            return true; // continuar enumeração
        };
        EnumWindows(_enumProc, IntPtr.Zero);
    }

    // -----------------------------------------------------------------------
    // WndProc
    // -----------------------------------------------------------------------
    private IntPtr WndProcCallback(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_APP_TRAY)
        {
            var evt = (uint)(lParam.ToInt32() & 0xFFFF);
            if (evt == WM_RBUTTONUP)
                ShowContextMenu();
            else if (evt == WM_LBUTTONDBLCLK)
                _dq.TryEnqueue(() => OnOpen?.Invoke());
            return IntPtr.Zero;
        }

        // Explorer restarted → re-register tray icon
        if (msg == _wmTaskbarCreated)
        {
            Shell_NotifyIcon(NIM_ADD, ref _nid);
            return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    // -----------------------------------------------------------------------
    // Autostart (HKCU\...\Run)
    // -----------------------------------------------------------------------
    private static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AutoStartKeyPath);
        return key?.GetValue(AutoStartValueName) != null;
    }

    private static void ToggleAutoStart()
    {
        if (IsAutoStartEnabled())
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartKeyPath, writable: true);
            key?.DeleteValue(AutoStartValueName, throwOnMissingValue: false);
        }
        else
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartKeyPath, writable: true);
            key?.SetValue(AutoStartValueName, $"\"{exePath}\"");
        }
    }

    // -----------------------------------------------------------------------
    // Context menu
    // -----------------------------------------------------------------------
    private void ShowContextMenu()
    {
        GetCursorPos(out var pt);
        bool autoStart = IsAutoStartEnabled();

        var hMenu = CreatePopupMenu();
        AppendMenu(hMenu, MF_STRING,                               new IntPtr(CMD_RESTART),   "Reiniciar");
        AppendMenu(hMenu, MF_SEPARATOR,                            IntPtr.Zero,               null);
        AppendMenu(hMenu, MF_STRING | (autoStart ? MF_CHECKED : 0u), new IntPtr(CMD_AUTOSTART), "Iniciar com o Windows");
        AppendMenu(hMenu, MF_SEPARATOR,                            IntPtr.Zero,               null);
        AppendMenu(hMenu, MF_STRING,                               new IntPtr(CMD_ABOUT),     "Sobre...");
        AppendMenu(hMenu, MF_SEPARATOR,                            IntPtr.Zero,               null);
        AppendMenu(hMenu, MF_STRING,                               new IntPtr(CMD_EXIT),      "Fechar");

        SetForegroundWindow(_hwnd);
        int cmd = TrackPopupMenu(hMenu, TPM_RIGHTBUTTON | TPM_RETURNCMD,
                                 pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        DestroyMenu(hMenu);

        switch (cmd)
        {
            case CMD_RESTART:   _dq.TryEnqueue(() => OnRestart?.Invoke()); break;
            case CMD_AUTOSTART: ToggleAutoStart();                         break;
            case CMD_ABOUT:     _dq.TryEnqueue(() => OnAbout?.Invoke());   break;
            case CMD_EXIT:
                Dispose();
                _dq.TryEnqueue(() => Application.Current.Exit());
                break;
        }
    }

    // -----------------------------------------------------------------------
    // Dispose
    // -----------------------------------------------------------------------
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd   = _hwnd,
            uID    = 1
        };
        Shell_NotifyIcon(NIM_DELETE, ref nid);

        if (_hwnd != IntPtr.Zero)
            DestroyWindow(_hwnd);

        // Ícones carregados via LoadIcon são shared handles — não chamar DestroyIcon
        // (double-free). Os carregados via LoadImage seriam owned, mas o processo está
        // a terminar e o OS liberta todos os handles.
    }
}
