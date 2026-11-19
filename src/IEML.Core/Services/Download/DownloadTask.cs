using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace IEML.Core.Services.Download;

public enum DownloadState { Queued, Running, Paused, Completed, Failed, Canceled }
internal enum DownloadIntent { None, Pause, Cancel }

/// One download. Bind the UI to this (INotifyPropertyChanged; values change on background threads).
public class DownloadTask : INotifyPropertyChanged
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Title { get; }
    public string Source { get; }
    public string Url { get; }
    public string Dest { get; }
    public string? Sha1 { get; }
    /// Optional batch label, e.g. the modpack name.
    public string? Group { get; }

    DownloadState _state = DownloadState.Queued;
    long _done, _total = -1;
    double _speed;
    string? _error;
    int _attempt;

    public DownloadState State { get => _state; internal set => Set(ref _state, value); }
    public long BytesDone { get => _done; internal set { if (Set(ref _done, value)) Raise(nameof(Percent), nameof(Eta)); } }
    public long BytesTotal { get => _total; internal set { if (Set(ref _total, value)) Raise(nameof(Percent), nameof(Eta)); } }
    public double SpeedBps { get => _speed; internal set { if (Set(ref _speed, value)) Raise(nameof(Eta)); } }
    public string? Error { get => _error; internal set => Set(ref _error, value); }
    public int Attempt { get => _attempt; internal set => Set(ref _attempt, value); }

    public double Percent => _total > 0 ? 100.0 * _done / _total : 0;
    public TimeSpan? Eta => _speed > 1 && _total > 0 && _total >= _done ? TimeSpan.FromSeconds((_total - _done) / _speed) : null;

    /// Completes when the download succeeds; faults on failure; is canceled on cancel. Stays pending while paused.
    public Task Completion => Tcs.Task;

    internal DownloadIntent Intent;
    internal CancellationTokenSource? Cts;
    internal readonly TaskCompletionSource Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal DownloadTask(string url, string dest, string title, string source, string? sha1, string? group)
    { Url = url; Dest = dest; Title = title; Source = source; Sha1 = sha1; Group = group; }

    public event PropertyChangedEventHandler? PropertyChanged;
    bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); return true;
    }
    void Raise(params string[] names) { foreach (var n in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); }
}
