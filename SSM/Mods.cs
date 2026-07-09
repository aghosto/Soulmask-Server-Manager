using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using static System.Int32;


namespace SoulmaskServerManager;

class SteamModsList
{
    public ObservableCollection<SteamMod> ModList { get; set; } = new();
}

public class SteamMod : PropertyChangedBase
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string AuthorUrl { get; set; } = "";
    public string PreviewImage { get; set; } = "";
    public string Description { get; set; } = "";
    public string AppId { get; set; } = "";
    public long TimeUpdated { get; set; }
    public int Subscriptions { get; set; }

    private bool _isChecked = false;
    public bool IsChecked
    {
        get => _isChecked;
        set => SetField(ref _isChecked, value);
    }
}

public class InstalledModViewModel : PropertyChangedBase
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    private bool _isPlaceholder;
    public bool IsPlaceholder
    {
        get => _isPlaceholder;
        set { _isPlaceholder = value; OnPropertyChanged(); }
    }

    public bool IsMarkedForDelete { get; set; }
}
