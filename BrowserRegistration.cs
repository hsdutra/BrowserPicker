using System;
using Microsoft.Win32;

namespace BrowserPicker;

/// <summary>
/// Writes and removes the BrowserPicker registry entries required for
/// Windows to recognise it as a registered browser / protocol handler.
/// Requires elevation (HKLM writes).
/// </summary>
internal static class BrowserRegistration
{
    private const string AppName    = "BrowserPicker";
    private const string ProgId     = "BrowserPickerProtocol";
    private const string HtmlProgId = "BrowserPickerHTML";
    private const string InstallDir = @"C:\Program Files\BrowserPicker";
    private static string ExePath   => System.IO.Path.Combine(InstallDir, "BrowserPicker.exe");

    // -----------------------------------------------------------------------
    // Registration
    // -----------------------------------------------------------------------

    internal static void Register()
    {
        // 1. App registration block
        using (var appKey = Registry.LocalMachine.CreateSubKey(
                   $@"SOFTWARE\{AppName}", writable: true))
        {
            appKey.SetValue("DisplayName",    AppName);
            appKey.SetValue("Description",    "Intercepts HTTP/HTTPS links and lets you choose which browser to open them in.");
            appKey.SetValue("ApplicationIcon",$"{ExePath},0");
        }

        // 2. Capabilities
        using (var capKey = Registry.LocalMachine.CreateSubKey(
                   $@"SOFTWARE\{AppName}\Capabilities", writable: true))
        {
            capKey.SetValue("ApplicationName",        AppName);
            capKey.SetValue("ApplicationDescription", "Browser picker for Windows");
        }

        // 3. URL associations
        using (var urlKey = Registry.LocalMachine.CreateSubKey(
                   $@"SOFTWARE\{AppName}\Capabilities\URLAssociations", writable: true))
        {
            urlKey.SetValue("http",  ProgId);
            urlKey.SetValue("https", ProgId);
            urlKey.SetValue("ftp",   ProgId);
        }

        // 3b. File associations — necessário para o checkmark "Definir padrão" persistir
        using (var fileKey = Registry.LocalMachine.CreateSubKey(
                   $@"SOFTWARE\{AppName}\Capabilities\FileAssociations", writable: true))
        {
            fileKey.SetValue(".html", HtmlProgId);
            fileKey.SetValue(".htm",  HtmlProgId);
        }

        // 4. ProgId / protocol handler — escrever em HKLM e HKCU\SOFTWARE\Classes
        //    O Windows 11 exige o ProgId em HKCU para aceitar o UserChoice
        using (var classesKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes", writable: true))
        {
            RegisterProgId(classesKey!);
            RegisterHtmlProgId(classesKey!);
        }

        using (var classesKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Classes", writable: true))
        {
            RegisterProgId(classesKey);
            RegisterHtmlProgId(classesKey);
        }

        // 5. RegisteredApplications
        using var regApps = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\RegisteredApplications", writable: true);
        regApps?.SetValue(AppName, $@"Software\{AppName}\Capabilities");

        // 6. Add/Remove Programs — aparece em Definições → Aplicações instaladas
        using (var arpKey = Registry.LocalMachine.CreateSubKey(
                   $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{AppName}", writable: true))
        {
            arpKey.SetValue("DisplayName",     AppName);
            arpKey.SetValue("DisplayIcon",     $"{ExePath},0");
            arpKey.SetValue("Publisher",       AppName);
            arpKey.SetValue("InstallLocation", InstallDir);
            arpKey.SetValue("UninstallString", $"\"{ExePath}\" --uninstall");
            arpKey.SetValue("NoModify",        1, RegistryValueKind.DWord);
            arpKey.SetValue("NoRepair",        1, RegistryValueKind.DWord);
        }

        // 7. StartMenuInternet — necessario para aparecer no dialogo "Escolher app"
        using (var smiRoot = Registry.LocalMachine.CreateSubKey(
                   $@"SOFTWARE\Clients\StartMenuInternet\{AppName}", writable: true))
        {
            smiRoot.SetValue(null, AppName);

            using (var icon = smiRoot.CreateSubKey("DefaultIcon", writable: true))
                icon.SetValue(null, $"{ExePath},0");

            using (var installInfo = smiRoot.CreateSubKey("InstallInfo", writable: true))
            {
                installInfo.SetValue("IconsVisible",      1, RegistryValueKind.DWord);
                installInfo.SetValue("ReinstallCommand",  $"\"{ExePath}\" --register");
                installInfo.SetValue("HideIconsCommand",  $"\"{ExePath}\" --register");
                installInfo.SetValue("ShowIconsCommand",  $"\"{ExePath}\" --register");
            }

            using (var smiCap = smiRoot.CreateSubKey("Capabilities", writable: true))
            {
                smiCap.SetValue("ApplicationName",        AppName);
                smiCap.SetValue("ApplicationDescription", "Browser picker for Windows");
                smiCap.SetValue("ApplicationIcon",        $"{ExePath},0");

                using (var smiUrl = smiCap.CreateSubKey("URLAssociations", writable: true))
                {
                    smiUrl.SetValue("http",  ProgId);
                    smiUrl.SetValue("https", ProgId);
                    smiUrl.SetValue("ftp",   ProgId);
                }

                using (var smiFile = smiCap.CreateSubKey("FileAssociations", writable: true))
                {
                    smiFile.SetValue(".html", HtmlProgId);
                    smiFile.SetValue(".htm",  HtmlProgId);
                }
            }

            using (var openCmd = smiRoot.CreateSubKey(@"shell\open\command", writable: true))
                openCmd.SetValue(null, $"\"{ExePath}\"");
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void RegisterProgId(RegistryKey classesKey)
    {
        using var progIdKey = classesKey.CreateSubKey(ProgId, writable: true);
        progIdKey.SetValue(null, "BrowserPicker Protocol");
        progIdKey.SetValue("URL Protocol", string.Empty);
        // EditFlags=2 (FTA_NoEdit) — necessário para Windows aceitar o ProgId como browser handler
        progIdKey.SetValue("EditFlags", 2, RegistryValueKind.DWord);

        using (var iconKey = progIdKey.CreateSubKey("DefaultIcon", writable: true))
            iconKey.SetValue(null, $"{ExePath},0");

        using var cmdKey = progIdKey.CreateSubKey(@"shell\open\command", writable: true);
        cmdKey.SetValue(null, $"\"{ExePath}\" \"%1\"");
    }

    private static void RegisterHtmlProgId(RegistryKey classesKey)
    {
        using var progIdKey = classesKey.CreateSubKey(HtmlProgId, writable: true);
        progIdKey.SetValue(null, "HTML Document");
        progIdKey.SetValue("EditFlags", 2, RegistryValueKind.DWord);

        using (var iconKey = progIdKey.CreateSubKey("DefaultIcon", writable: true))
            iconKey.SetValue(null, $"{ExePath},0");

        using var cmdKey = progIdKey.CreateSubKey(@"shell\open\command", writable: true);
        cmdKey.SetValue(null, $"\"{ExePath}\" \"%1\"");
    }

    // -----------------------------------------------------------------------
    // Unregistration  (registry only — folder removal done by PS script)
    // -----------------------------------------------------------------------

    internal static void Unregister()
    {
        // Remove ProgIds de HKLM\SOFTWARE\Classes
        using (var classesKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes", writable: true))
        {
            classesKey?.DeleteSubKeyTree(ProgId,     throwOnMissingSubKey: false);
            classesKey?.DeleteSubKeyTree(HtmlProgId, throwOnMissingSubKey: false);
        }

        // Remove ProgIds de HKCU\SOFTWARE\Classes
        using (var classesKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Classes", writable: true))
        {
            classesKey?.DeleteSubKeyTree(ProgId,     throwOnMissingSubKey: false);
            classesKey?.DeleteSubKeyTree(HtmlProgId, throwOnMissingSubKey: false);
        }

        // Remove app capability tree from HKLM
        Registry.LocalMachine.DeleteSubKeyTree($@"SOFTWARE\{AppName}",
            throwOnMissingSubKey: false);

        // Remove StartMenuInternet entry
        Registry.LocalMachine.DeleteSubKeyTree(
            $@"SOFTWARE\Clients\StartMenuInternet\{AppName}",
            throwOnMissingSubKey: false);

        // Remove entry from RegisteredApplications
        using var regApps = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\RegisteredApplications", writable: true);
        regApps?.DeleteValue(AppName, throwOnMissingValue: false);

        // Remove Add/Remove Programs entry
        Registry.LocalMachine.DeleteSubKeyTree(
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{AppName}",
            throwOnMissingSubKey: false);

        // Remove autostart do utilizador corrente
        using var runKey = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        runKey?.DeleteValue(AppName, throwOnMissingValue: false);
    }

    /// <summary>
    /// Escreve um script .cmd no %TEMP% que, após o processo terminar,
    /// apaga a pasta de instalação e o próprio script.
    /// Deve ser chamado imediatamente antes de Application.Current.Exit().
    /// </summary>
    internal static void CleanupAfterExit()
    {
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bp_cleanup.cmd");
        var script =
            "@echo off\r\n" +
            ":wait\r\n" +
            $"tasklist /fi \"imagename eq {AppName}.exe\" 2>nul | find /i \"{AppName}.exe\" >nul\r\n" +
            "if not errorlevel 1 (\r\n" +
            "    timeout /t 1 /nobreak >nul\r\n" +
            "    goto wait\r\n" +
            ")\r\n" +
            $"rd /s /q \"{InstallDir}\"\r\n" +
            "del \"%~f0\"\r\n";

        System.IO.File.WriteAllText(tmp, script, System.Text.Encoding.ASCII);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c \"{tmp}\"")
        {
            CreateNoWindow  = true,
            UseShellExecute = false,
            WindowStyle     = System.Diagnostics.ProcessWindowStyle.Hidden
        });
    }
}
