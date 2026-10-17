namespace ChickenCoop.Runtime;

/// <summary>
/// An append-only text log (data/admin.log, data/transfers.log). Lines are written on a background thread in order;
/// the file rolls over to .1 at 5 MB.
/// </summary>
public sealed class AuditLog
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private readonly string _path;
    private readonly object _lock = new();
    private Task _writer = Task.CompletedTask;

    public AuditLog(string path) => _path = path;

    public bool Enabled { get; set; } = true;
    public Action<Exception>? OnError { get; set; }

    public void Write(string text)
    {
        if (!Enabled) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}";
        lock (_lock)
        {
            _writer = _writer.ContinueWith(_ =>
            {
                try
                {
                    var dir = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes) File.Move(_path, _path + ".1", true);
                    File.AppendAllText(_path, line);
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(ex);
                }
            }, TaskScheduler.Default);
        }
    }

    /// <summary>The last <paramref name="count"/> lines (after pending writes finish).</summary>
    public List<string> Tail(int count)
    {
        Flush();
        try
        {
            return File.Exists(_path) ? File.ReadLines(_path).TakeLast(Math.Max(1, count)).ToList() : new List<string>();
        }
        catch (IOException)
        {
            return new List<string>();
        }
    }

    public void Flush(int timeoutMs = 5000)
    {
        Task t;
        lock (_lock) t = _writer;
        t.Wait(timeoutMs);
    }
}
