using System;
using System.Diagnostics;

namespace BrowserPicker;

internal static class UrlHandler
{
    internal static void OpenUrlInBrowser(BrowserItem browser, string url)
    {
        var psi = new ProcessStartInfo
        {
            FileName        = browser.ExecutablePath,
            UseShellExecute = false
        };

        if (!string.IsNullOrWhiteSpace(url))
            psi.ArgumentList.Add(url);

        Process.Start(psi);
        // A janela é escondida pelo chamador (MainWindow.HideWindow) antes deste método ser invocado
    }
}
