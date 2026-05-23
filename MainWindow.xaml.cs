using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using Windows.Graphics;
using WinRT.Interop;

namespace BrowserPicker;

public sealed partial class MainWindow : Window
{
    private string             _url;
    private bool               _browsersLoaded;
    private bool               _isPrewarming;
    private bool               _canHideOnDeactivate; // true apenas depois da janela estar completamente activada
    private List<BrowserItem>? _browsers;

    public MainWindow(string url, bool prewarm = false)
    {
        _url          = url;
        _isPrewarming = prewarm;
        this.InitializeComponent();
        UrlPreview.Text       = url;
        UrlPreview.Visibility = string.IsNullOrEmpty(url) ? Visibility.Collapsed : Visibility.Visible;
        ConfigureWindow();
        this.Activated += MainWindow_Activated;
    }

    // -----------------------------------------------------------------------
    // Window setup
    // -----------------------------------------------------------------------

    private void ConfigureWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);

        var appWindow = GetAppWindow();
        appWindow.IsShownInSwitchers = false;
        appWindow.Resize(new SizeInt32(420, 280));

        // Ícone da janela (Alt+Tab / Task Manager) — Assets\app.ico (build/publish) ou junto ao exe
        var exeDir  = System.IO.Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty) ?? string.Empty;
        var icoFile = System.IO.Path.Combine(exeDir, "Assets", "app.ico");
        if (!System.IO.File.Exists(icoFile))
            icoFile = System.IO.Path.Combine(exeDir, "app.ico");
        if (System.IO.File.Exists(icoFile))
            appWindow.SetIcon(icoFile);
        else if (System.Diagnostics.Process.GetCurrentProcess().MainModule is { } mod)
            appWindow.SetIcon(mod.FileName);

        // Mica backdrop (Windows 11 only; silently skipped on Windows 10)
        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop();

        // Janela não-redimensionável, sem botões minimizar/maximizar/fechar (picker popup)
        if (appWindow.Presenter is OverlappedPresenter overlapped)
        {
            overlapped.IsMaximizable = false;
            overlapped.IsMinimizable = false;
            overlapped.IsResizable   = false;
        }

        // Fechar (X / Alt+F4) esconde a janela; o programa continua na bandeja.
        appWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            HideWindow();
        };

        // Não centrar durante o pre-warm: a janela é posicionada fora do ecrã
        // por App.xaml.cs antes de ser activada, evitando flash na inicialização.
        // ShowForUrl() chama CenterWindow() antes de mostrar a janela ao utilizador.
        if (!_isPrewarming)
            CenterWindow(appWindow);

        // ESC esconde a janela (app fica na bandeja)
        this.Content.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
                HideWindow();
        };

        Activated += OnWindowActivated;
    }

    private AppWindow GetAppWindow()
    {
        var hwnd   = WindowNative.GetWindowHandle(this);
        var wndId  = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(wndId);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetCursorPos(out CursorPoint lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }

    private void ForceToForeground()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        SetForegroundWindow(hwnd);
        BringWindowToTop(hwnd);
    }

    private void CenterWindow(AppWindow appWindow)
    {
        // Usar posição do cursor para centrar no monitor certo (multi-monitor)
        GetCursorPos(out var cursor);
        var display  = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary);
        var workArea = display.WorkArea;
        var size     = appWindow.Size;

        appWindow.Move(new PointInt32(
            workArea.X + (workArea.Width  - size.Width)  / 2,
            workArea.Y + (workArea.Height - size.Height) / 2));
    }

    // -----------------------------------------------------------------------
    // Data loading
    // -----------------------------------------------------------------------

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        // Load only on first activation
        this.Activated -= MainWindow_Activated;
        if (!_isPrewarming)
            ForceToForeground();
        try
        {
            await LoadBrowsersAsync();
        }
        catch
        {
            HideWindow();
        }
    }

    private async Task LoadBrowsersAsync()
    {
        // Usar cache de browsers se já estiver disponível (tray pré-carregou)
        var browsers = App.CachedBrowsers ?? await BrowserRegistry.GetInstalledBrowsersAsync();
        _browsers = browsers;

        ApplyLastBrowserPreference(browsers);

        if (browsers.Count == 0)
        {
            var dialog = new ContentDialog
            {
                Title             = "Nenhum navegador encontrado",
                Content           = "Não foram encontrados navegadores instalados. Deseja abrir as Definições para configurar um navegador predefinido?",
                PrimaryButtonText = "Abrir Definições",
                CloseButtonText   = "Fechar",
                XamlRoot          = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
                Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });

            HideWindow();
            return;
        }

        BrowserList.ItemsSource = browsers;
        _browsersLoaded = true;

        // Altura dinâmica: ajustar ao número de browsers
        const int captionHeight = 32;
        int       headerHeight  = string.IsNullOrEmpty(_url) ? 52 : 76;
        const int itemHeight    = 56;
        const int bottomPad     = 8;
        int windowHeight = captionHeight + headerHeight + (browsers.Count * itemHeight) + bottomPad;
        windowHeight = Math.Clamp(windowHeight, 220, 580);

        if (!IsBrowserPickerDefault())
        {
            NotDefaultBanner.Visibility = Visibility.Visible;
            windowHeight += 62;
        }

        var appWindow = GetAppWindow();
        appWindow.Resize(new SizeInt32(420, windowHeight));
        CenterWindow(appWindow);

        // Pre-warm: esconder agora que a lista está pronta; ícones continuam a carregar
        // nos objectos BrowserItem em background — estarão prontos na próxima abertura.
        if (_isPrewarming)
            HideWindow();

        // Carregar ícones sequencialmente na UI thread (STA); cada ícone aparece assim
        // que estiver pronto via INotifyPropertyChanged (funciona mesmo com janela escondida)
        foreach (var browser in browsers)
        {
            browser.Icon = await IconExtractor.ExtractIconAsync(browser.ExecutablePath);
        }
    }

    // -----------------------------------------------------------------------
    // Item click
    // -----------------------------------------------------------------------

    private void BrowserList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is BrowserItem browser)
        {
            BrowserRegistry.SetLastBrowserPath(browser.ExecutablePath);
            HideWindow();
            UrlHandler.OpenUrlInBrowser(browser, _url);
        }
    }

    // -----------------------------------------------------------------------
    // Window visibility (hide/show instead of close/create)
    // -----------------------------------------------------------------------

    private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated)
        {
            if (_canHideOnDeactivate)
                HideWindow();
        }
        else
        {
            // Só habilitar o hide-on-deactivate após a janela estar completamente activada
            // (evita race condition entre appWindow.Show() e o primeiro CodeActivated)
            _canHideOnDeactivate = _browsersLoaded && !_isPrewarming;
        }
    }

    private void HideWindow()
    {
        _canHideOnDeactivate = false;
        GetAppWindow().Hide();
    }

    /// <summary>
    /// Actualiza o URL e mostra a janela já pré-aquecida (browsers e ícones já carregados).
    /// </summary>
    internal void ShowForUrl(string url)
    {
        _isPrewarming = false;
        _url          = url;

        UrlPreview.Text       = url;
        UrlPreview.Visibility = string.IsNullOrEmpty(url) ? Visibility.Collapsed : Visibility.Visible;

        var appWindow = GetAppWindow();

        if (_browsers is { Count: > 0 })
        {
            // Reordenar: o último browser escolhido sobe ao topo
            ApplyLastBrowserPreference(_browsers);
            BrowserList.ItemsSource = null;
            BrowserList.ItemsSource = _browsers;

            // Recalcular altura (URL pode ter mudado; rever banner "definir como padrão")
            const int captionHeight = 32;
            int       headerHeight  = string.IsNullOrEmpty(url) ? 52 : 76;
            const int itemHeight    = 56;
            const int bottomPad     = 8;
            int windowHeight = captionHeight + headerHeight + (_browsers.Count * itemHeight) + bottomPad;
            windowHeight = Math.Clamp(windowHeight, 220, 580);

            bool isDefault = IsBrowserPickerDefault();
            NotDefaultBanner.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;
            if (!isDefault) windowHeight += 62;

            appWindow.Resize(new SizeInt32(420, windowHeight));
        }

        CenterWindow(appWindow);
        appWindow.Show();   // desesconder explicitamente após AppWindow.Hide() no pre-warm
        this.Activate();
        SetForegroundWindow(WindowNative.GetWindowHandle(this));
    }

    private static void ApplyLastBrowserPreference(List<BrowserItem> browsers)
    {
        var lastPath = BrowserRegistry.GetLastBrowserPath();
        if (string.IsNullOrEmpty(lastPath)) return;
        var idx = browsers.FindIndex(b =>
            string.Equals(b.ExecutablePath, lastPath, StringComparison.OrdinalIgnoreCase));
        if (idx > 0)
        {
            var preferred = browsers[idx];
            browsers.RemoveAt(idx);
            browsers.Insert(0, preferred);
        }
    }

    // -----------------------------------------------------------------------
    // Default browser check
    // -----------------------------------------------------------------------

    [ComImport, Guid("4e530b0a-e611-4c77-a3ac-9031d022281b"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationAssociationRegistration
    {
        void QueryCurrentDefault([MarshalAs(UnmanagedType.LPWStr)] string pszQuery, int atQueryType, int alQueryLevel, [MarshalAs(UnmanagedType.LPWStr)] out string ppszAssociation);
        void QueryAppIsDefault([MarshalAs(UnmanagedType.LPWStr)] string pszQuery, int atQueryType, int alQueryLevel, [MarshalAs(UnmanagedType.LPWStr)] string pszAppRegistryName, out bool pfDefault);
        void QueryAppIsDefaultAll(int alQueryLevel, [MarshalAs(UnmanagedType.LPWStr)] string pszAppRegistryName, out bool pfDefault);
        void SetAppAsDefault([MarshalAs(UnmanagedType.LPWStr)] string pszAppRegistryName, [MarshalAs(UnmanagedType.LPWStr)] string pszSet, int atSetType);
        void SetAppAsDefaultAll([MarshalAs(UnmanagedType.LPWStr)] string pszAppRegistryName);
        void ClearUserAssociations();
    }

    [ComImport, Guid("591209c7-767b-42b2-9fba-44ee4615f2c7")]
    private class ApplicationAssociationRegistrationClass { }

    private static bool IsBrowserPickerDefault()
    {
        try
        {
            var reg = (IApplicationAssociationRegistration)new ApplicationAssociationRegistrationClass();
            reg.QueryAppIsDefaultAll(1, "BrowserPicker", out bool isDefault); // AL_EFFECTIVE = 1
            return isDefault;
        }
        catch { return false; }
    }

    private void SetDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(
            "ms-settings:defaultapps?registeredAppUser=BrowserPicker")
        {
            UseShellExecute = true
        });
    }
}
