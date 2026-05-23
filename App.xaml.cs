using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace BrowserPicker;

public partial class App : Application
{
    // Pre-loaded browser list — populated in background on startup
    internal static List<BrowserItem>? CachedBrowsers { get; private set; }

    private TrayIconManager? _tray;
    private Window?          _anchor;       // janela invísível que mantém o dispatcher vivo
    private MainWindow?      _pickerWindow;
    private Window?          _aboutWindow;

    public App()
    {
        this.UnhandledException += (_, e) =>
        {
            e.Handled = true;
        };
        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var dispatcher = DispatcherQueue.GetForCurrentThread();

            // Pre-load browser list in background so picker opens instantly
            _ = BrowserRegistry.GetInstalledBrowsersAsync()
                    .ContinueWith(t =>
                    {
                        if (t.IsCompletedSuccessfully)
                            CachedBrowsers = t.Result;
                    });

            // Named-pipe server: receives URLs forwarded by subsequent process launches
            PipeServer.StartServer(dispatcher, url => ShowPickerWindow(url));

            // Tray icon
            _tray = new TrayIconManager(dispatcher);
            _tray.OnRestart = RestartApp;
            _tray.OnAbout   = ShowAboutWindow;

            // Janela âncora invisible: evita que o WinUI 3 dispatcher saia quando
            // o picker ou a janela Sobre fecham (comportamento padrão do WinUI 3:
            // encerrar quando todas as janelas são fechadas).
            _anchor = new Window();
            {
                var anchorHwnd  = WindowNative.GetWindowHandle(_anchor);
                var anchorId    = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(anchorHwnd);
                var anchorAppWin = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(anchorId);
                anchorAppWin.IsShownInSwitchers = false;
                anchorAppWin.Move(new PointInt32(-32000, -32000)); // fora do ecrã antes do primeiro Show
                _anchor.Activate();      // tem de ser activada pelo menos uma vez
                anchorAppWin.Hide();     // esconder imediatamente após activação
            }

            // Pré-aquecer a janela picker: criar off-screen e activar para carregar browsers
            // e ícones em background. A janela esconde-se sozinha após o carregamento.
            // Resultado: primeira abertura real é quase instantânea.
            _pickerWindow = new MainWindow("", prewarm: true);
            {
                var pickerHwnd   = WindowNative.GetWindowHandle(_pickerWindow);
                var pickerId     = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(pickerHwnd);
                var pickerAppWin = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(pickerId);
                pickerAppWin.IsShownInSwitchers = false;
                pickerAppWin.Move(new PointInt32(-32000, -32000));  // fora do ecrã durante o pre-warm
                _pickerWindow.Activate();  // despoleta LoadBrowsersAsync; a janela esconde-se sozinha
            }

            // If a URL was passed in this launch, show the picker immediately
            var url = Environment.GetEnvironmentVariable("BROWSERPICKER_URL") ?? string.Empty;
            if (!string.IsNullOrEmpty(url))
                ShowPickerWindow(url);
        }
        catch { }
    }

    // -----------------------------------------------------------------------
    // Window management
    // -----------------------------------------------------------------------

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    internal void ShowPickerWindow(string url)
    {
        if (_pickerWindow != null)
        {
            _pickerWindow.ShowForUrl(url);
        }
        else
        {
            // Fallback: o pre-warm pode ter falhado
            _pickerWindow = new MainWindow(url);
            _pickerWindow.Activate();
        }
        var hwnd = WindowNative.GetWindowHandle(_pickerWindow);
        SetForegroundWindow(hwnd);
    }

    private void ShowAboutWindow()
    {
        if (_aboutWindow != null)
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new AboutWindow();
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Activate();
    }

    private static void RestartApp()
    {
        var exePath = Process.GetCurrentProcess().MainModule!.FileName;
        Process.Start(exePath);
        Application.Current.Exit();
    }
}
