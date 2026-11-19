using IEML.Core.Contracts;
using NAudio.Wave;
namespace IEML.Core.Services;

public class MusicService : IMusicService, IDisposable
{
    static readonly HashSet<string> Ext = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".flac", ".m4a", ".wma" };
    readonly List<Track> _list = new();
    int _idx = -1;
    WaveOutEvent? _out; AudioFileReader? _reader; float _vol = 0.7f;

    public IReadOnlyList<Track> Playlist => _list;
    public Track? Current => _idx >= 0 && _idx < _list.Count ? _list[_idx] : null;
    public event Action? Changed;

    public float Volume { get => _vol; set { _vol = Math.Clamp(value, 0f, 1f); if (_reader != null) _reader.Volume = _vol; } }

    public void AddFolder(string path)
    {
        foreach (var f in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories).Where(f => Ext.Contains(Path.GetExtension(f))))
            _list.Add(new Track(f, Path.GetFileNameWithoutExtension(f)));
        Changed?.Invoke();
    }

    public void Play(Track? t = null)
    {
        if (t is null && _out?.PlaybackState == PlaybackState.Paused) { _out.Play(); Changed?.Invoke(); return; }
        if (t is not null) _idx = _list.IndexOf(t);
        else if (_idx < 0 && _list.Count > 0) _idx = 0;
        if (_idx < 0) return;
        StartCurrent();
    }

    public void Pause() { _out?.Pause(); Changed?.Invoke(); }
    public void Next() { if (_list.Count == 0) return; _idx = (_idx + 1) % _list.Count; StartCurrent(); }
    public void Previous() { if (_list.Count == 0) return; _idx = (_idx - 1 + _list.Count) % _list.Count; StartCurrent(); }

    void StartCurrent()
    {
        Cleanup();
        try
        {
            _reader = new AudioFileReader(_list[_idx].Path) { Volume = _vol };
            _out = new WaveOutEvent();
            _out.Init(_reader);
            _out.PlaybackStopped += OnStopped;
            _out.Play();
        }
        catch { Cleanup(); Next(); return; } // skip unreadable file
        Changed?.Invoke();
    }

    void OnStopped(object? s, StoppedEventArgs e)
    {
        if (_reader != null && _reader.Position >= _reader.Length) Next(); // track finished -> next
    }

    void Cleanup()
    {
        if (_out != null) { _out.PlaybackStopped -= OnStopped; _out.Dispose(); _out = null; }
        _reader?.Dispose(); _reader = null;
    }

    public void Dispose() => Cleanup();
}
