using System.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BrowserPicker;

public class BrowserItem : INotifyPropertyChanged
{
    public string Name { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;

    private BitmapImage? _icon;
    public BitmapImage? Icon
    {
        get => _icon;
        set { _icon = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon))); }
    }

    public string? ToolTipText { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
}
