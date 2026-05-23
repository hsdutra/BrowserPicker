using System;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BrowserPicker;

public sealed partial class UninstallWindow : Window
{
    public UninstallWindow()
    {
        this.InitializeComponent();
        // Hide the shell window — we only show dialogs
        var appWindow = GetAppWindow();
        appWindow.Hide();

        this.Activated += UninstallWindow_Activated;
    }

    // -----------------------------------------------------------------------
    // Window helpers
    // -----------------------------------------------------------------------

    private Microsoft.UI.Windowing.AppWindow GetAppWindow()
    {
        var hwnd  = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var wndId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return Microsoft.UI.Windowing.AppWindow.GetFromWindowId(wndId);
    }

    // -----------------------------------------------------------------------
    // Entry point  (first activation)
    // -----------------------------------------------------------------------

    private async void UninstallWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        this.Activated -= UninstallWindow_Activated;

        var mode = Environment.GetEnvironmentVariable("BROWSERPICKER_MODE") ?? string.Empty;

        if (mode.Equals("uninstall-confirmed", StringComparison.OrdinalIgnoreCase))
        {
            // Came back elevated — remove registry directly
            RunUnregister();
            return;
        }

        // "uninstall" — ask the user first
        var confirm = new ContentDialog
        {
            Title               = "Remover BrowserPicker",
            Content             = "Tem a certeza que deseja remover o BrowserPicker?",
            PrimaryButtonText   = "Remover",
            CloseButtonText     = "Cancelar",
            XamlRoot            = Content.XamlRoot
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            Application.Current.Exit();
            return;
        }

        RunUnregister();
    }

    // -----------------------------------------------------------------------
    // Registry removal
    // -----------------------------------------------------------------------

    private async void RunUnregister()
    {
        try
        {
            BrowserRegistration.Unregister();
            BrowserRegistration.CleanupAfterExit();
            await ShowInfoDialog("Remoção concluída",
                "O BrowserPicker foi removido com sucesso.\n\nA pasta de instalação será apagada automaticamente.");
        }
        catch (UnauthorizedAccessException)
        {
            // Need elevation — relaunch with --uninstall --confirmed + runas
            try
            {
                Program.RelaunchAsAdmin("--uninstall --confirmed");
            }
            catch (Exception ex)
            {
                await ShowInfoDialog("Erro ao remover",
                    $"Não foi possível elevar permissões. Detalhes:\n{ex.Message}\n\n" +
                    "Uma política de grupo pode estar a bloquear o acesso ao registry.");
            }
        }
        catch (Exception ex)
        {
            await ShowInfoDialog("Erro ao remover", ex.Message);
        }

        Application.Current.Exit();
    }

    private System.Threading.Tasks.Task<ContentDialogResult> ShowInfoDialog(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title         = title,
            Content       = message,
            CloseButtonText = "Fechar",
            XamlRoot      = Content.XamlRoot
        };
        return dialog.ShowAsync().AsTask();
    }
}
