namespace CodexPetPlugin;

public enum HostAction { None, StartPet, StopPet }

// Two absent samples tolerate a brief desktop restart. Unknown never means closed.
public sealed class HostLifetimeState
{
    private bool running;
    private int absent;
    public HostAction Observe(bool? present)
    {
        if (present is null) { absent = 0; return HostAction.None; }
        if (present.Value)
        {
            absent = 0;
            if (running) return HostAction.None;
            running = true; return HostAction.StartPet;
        }
        if (!running || ++absent < 2) return HostAction.None;
        running = false; absent = 0; return HostAction.StopPet;
    }
}

public static partial class HostLifetime
{
    public static bool IsDesktopPath(string? path)
    {
        string normalized = (path ?? "").Replace('\\', '/');
        return normalized.Contains("/WindowsApps/OpenAI.Codex_", StringComparison.OrdinalIgnoreCase)
            && normalized.EndsWith("/app/ChatGPT.exe", StringComparison.OrdinalIgnoreCase);
    }
}
