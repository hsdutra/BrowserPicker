using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.UI.Xaml;
using WinRT;

namespace BrowserPicker;

class Program
{
    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(uint dwProcessId);
    private const uint ASFW_ANY = unchecked((uint)-1);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
    private const uint MB_OK               = 0x00;
    private const uint MB_YESNO            = 0x04;
    private const uint MB_ICONQUESTION     = 0x20;
    private const uint MB_ICONINFORMATION  = 0x40;
    private const uint MB_ICONERROR        = 0x10;
    private const uint MB_DEFBUTTON2       = 0x100;
    private const int  IDYES               = 6;

    [STAThread]
    static void Main(string[] args)
    {
        ComWrappersSupport.InitializeComWrappers();

        if (args.Length > 0)
        {
            // --register
            if (args[0].Equals("--register", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    BrowserRegistration.Register();
                }
                catch (UnauthorizedAccessException)
                {
                    RelaunchAsAdmin("--register");
                }
                return;
            }

            // --uninstall  /  --uninstall --confirmed
            if (args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
            {
                bool skipConfirm = args.Length >= 2
                    && args[1].Equals("--confirmed", StringComparison.OrdinalIgnoreCase);
                RunUninstall(skipConfirm);
                return;
            }
        }

        // Single-instance guard: apenas uma instância tray pode existir.
        // Mutex nomeado global — mais robusto que o pipe sozinho (o pipe pode
        // ainda não estar pronto quando o segundo processo arranca).
        using var mutex = new System.Threading.Mutex(
            initiallyOwned: false,
            name: "Global\\BrowserPickerSingleInstance",
            out bool isFirstInstance);

        if (!isFirstInstance)
        {
            // Ceder permissão de foreground ao tray ANTES de enviar o URL.
            // Sem isto, o Windows impede o tray de trazer a janela para frente.
            AllowSetForegroundWindow(ASFW_ANY);

            // Enviar URL pelo named pipe (integrity label MEDIUM, aceita ligações
            // de processos de média integridade como os invocadores de links).
            if (args.Length > 0)
                PipeServer.TrySendUrl(args[0]);
            return;
        }

        // Somos a primeira (e única) instância — arrancar normalmente
        if (args.Length > 0)
            Environment.SetEnvironmentVariable("BROWSERPICKER_URL", args[0]);

        Application.Start(_ => new App());
    }

    internal static void RelaunchAsAdmin(string arguments)
    {
        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName!;
        var psi = new ProcessStartInfo(exePath, arguments)
        {
            UseShellExecute = true,
            Verb = "runas"
        };
        Process.Start(psi);
    }

    private static bool IsAdmin() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);

    private static void RunUninstall(bool skipConfirm)
    {
        if (!IsAdmin())
        {
            // Pedir elevação UAC e passar --confirmed para saltar o diálogo de confirmação
            RelaunchAsAdmin("--uninstall --confirmed");
            return;
        }

        if (!skipConfirm)
        {
            int r = MessageBox(IntPtr.Zero,
                "Tem a certeza que deseja remover o BrowserPicker?",
                "Remover BrowserPicker",
                MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2);
            if (r != IDYES) return;
        }

        // Fechar instância tray em execução
        foreach (var p in Process.GetProcessesByName("BrowserPicker"))
        {
            if (p.Id == Process.GetCurrentProcess().Id) continue;
            try { p.Kill(); p.WaitForExit(3000); } catch { }
        }

        try
        {
            BrowserRegistration.Unregister();
            BrowserRegistration.CleanupAfterExit();
            MessageBox(IntPtr.Zero,
                "O BrowserPicker foi removido com sucesso.\nA pasta de instalação será apagada automaticamente.",
                "Remoção concluída",
                MB_OK | MB_ICONINFORMATION);
        }
        catch (Exception ex)
        {
            MessageBox(IntPtr.Zero, $"Erro ao remover:\n{ex.Message}", "Erro", MB_OK | MB_ICONERROR);
        }
    }
}
