namespace CodexPet;

// Only filesystem metadata; never read message contents or token logs.
// A local activity signal invalidates the quota cache, like DSH session/event.
public sealed class SessionActivityWatcher : IDisposable
{
    private FileSystemWatcher? watcher;
    public event Action? Activity;
    public SessionActivityWatcher()
    {
        string home = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        string path = Path.Combine(home, "sessions");
        if (!Directory.Exists(path)) return;
        watcher = new FileSystemWatcher(path, "*.jsonl") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
        watcher.Changed += (_, _) => Activity?.Invoke();
        watcher.Created += (_, _) => Activity?.Invoke();
        watcher.Error += (_, _) => Activity?.Invoke();
    }
    public bool Enabled { set { if (watcher is not null) watcher.EnableRaisingEvents = value; } }
    public void Dispose() { watcher?.Dispose(); watcher = null; }
}
