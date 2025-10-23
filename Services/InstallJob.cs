using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DragonDen.ModManager.Services;

public sealed class InstallJob : INotifyPropertyChanged
{
    private bool _isIndeterminate;
    private string _phase = "queued";
    private int _progress;
    private string _source = "";
    private string _status = "";
    private string _title = "";

    public string Id { get; } = Guid.NewGuid().ToString("N");

    public string Title
    {
        get => _title;
        set
        {
            _title = value;
            OnPropertyChanged();
        }
    }

    public string Source
    {
        get => _source;
        set
        {
            _source = value;
            OnPropertyChanged();
        }
    }

    public int Progress
    {
        get => _progress;
        set
        {
            _progress = value;
            OnPropertyChanged();
        }
    }

    public string Phase
    {
        get => _phase;
        set
        {
            _phase = value;
            OnPropertyChanged();
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set
        {
            _isIndeterminate = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}