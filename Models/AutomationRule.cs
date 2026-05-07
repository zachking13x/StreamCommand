using System.ComponentModel;

namespace StreamCommand.Models;

public class AutomationRule : INotifyPropertyChanged
{
    private bool _isEnabled;

    public string Trigger { get; set; } = "";
    public string Action { get; set; } = "";
    public string Category { get; set; } = "";

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
