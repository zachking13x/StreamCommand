using System.Collections.Generic;
using System.ComponentModel;

namespace StreamCommand.Models;

public class LinkItem
{
    public string Label { get; set; } = "";
    public string Url { get; set; } = "";
}

public class ChecklistItem : INotifyPropertyChanged
{
    private bool _isChecked;

    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
    public List<LinkItem> Links { get; set; } = new();

    public bool IsChecked
    {
        get => _isChecked;
        set { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class ChecklistSection : INotifyPropertyChanged
{
    private bool _isExpanded = true;

    public string Title { get; set; } = "";
    public string Icon { get; set; } = "";
    public System.Collections.ObjectModel.ObservableCollection<ChecklistItem> Items { get; set; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
