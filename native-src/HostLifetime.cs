using System.Diagnostics;
namespace CodexPetPlugin;

public static partial class HostLifetime
{
    public static bool? DesktopRunning()
    {
        bool unknown = false;
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        {
            using (process)
            {
                try { if (IsDesktopPath(process.MainModule?.FileName)) return true; }
                catch { unknown = true; }
            }
        }
        return unknown ? null : false;
    }
    public static string? DesktopSession()
    {
        var sessions = new List<(long Started, int Id)>();
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        {
            using (process)
            {
                try { if (IsDesktopPath(process.MainModule?.FileName)) sessions.Add((process.StartTime.ToUniversalTime().Ticks, process.Id)); }
                catch { }
            }
        }
        if (sessions.Count == 0) return null;
        var first = sessions.MinBy(s => s.Started);
        return first.Id + ":" + first.Started;
    }
}
