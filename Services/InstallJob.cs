using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DragonDen.ModManager.Services;

public sealed class InstallJob : INotifyPropertyChanged
{
    private long _doneBytes;
    private string _eta = "";
    private bool _isCancellable = true;
    private bool _isCompleted;
    private bool _isIndeterminate;
    private string _phase = "queued";
    private int _progress;
    private string _source = "";
    private string _status = "";
    private int _subPercent;
    private string _subTask = "";
    private string _title = "";
    private long _totalBytes;

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
    public CancellationTokenSource? Cts { get; set; }

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

    public string SubTask
    {
        get => _subTask;
        set
        {
            _subTask = value;
            OnPropertyChanged();
        }
    }

    public int SubPercent
    {
        get => _subPercent;
        set
        {
            _subPercent = value;
            OnPropertyChanged();
        }
    }

    public string Eta
    {
        get => _eta;
        set
        {
            _eta = value;
            OnPropertyChanged();
        }
    }

    public long TotalBytes
    {
        get => _totalBytes;
        set
        {
            _totalBytes = value;
            OnPropertyChanged();
        }
    }

    public long DoneBytes
    {
        get => _doneBytes;
        set
        {
            _doneBytes = value;
            OnPropertyChanged();
        }
    }

    public bool IsCancellable
    {
        get => _isCancellable;
        set
        {
            _isCancellable = value;
            OnPropertyChanged();
        }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            _isCompleted = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public void Cancel()
    {
        try
        {
            Cts?.Cancel();
        }
        catch
        {
        }
    }
}