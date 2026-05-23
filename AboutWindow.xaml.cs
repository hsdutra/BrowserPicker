using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace BrowserPicker;

public sealed partial class AboutWindow : Window
{
    public AboutWindow()
    {
        this.InitializeComponent();
        ConfigureWindow();
    }

    private void ConfigureWindow()
    {
        var hwnd   = WindowNative.GetWindowHandle(this);
        var wndId  = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWin = AppWindow.GetFromWindowId(wndId);

        // Extended title bar — Mica preenche a janela toda (look nativo WinUI 3)
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);

        // Caption buttons transparentes para não quebrar o Mica
        appWin.TitleBar.ButtonBackgroundColor         = Color.FromArgb(0, 0, 0, 0);
        appWin.TitleBar.ButtonInactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);

        appWin.Resize(new SizeInt32(380, 260));
        appWin.SetIcon(System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName);

        if (appWin.Presenter is OverlappedPresenter p)
        {
            p.IsMaximizable = false;
            p.IsResizable   = false;
        }

        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop();

        // Centrar no monitor do cursor
        GetCursorPos(out var cursor);
        var display  = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary);
        var workArea = display.WorkArea;
        var size     = appWin.Size;
        appWin.Move(new PointInt32(
            workArea.X + (workArea.Width  - size.Width)  / 2,
            workArea.Y + (workArea.Height - size.Height) / 2));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => this.Close();
}
