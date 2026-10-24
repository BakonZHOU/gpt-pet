using System.Diagnostics;

namespace CodexPet;

public static partial class HostLifetime
{
    private const string StopName = "Local\\CodexPet.Whale.Watch.Stop.v1";
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
    public static int Watch()
    {
        using var singleton = new Mutex(true, "Local\\CodexPet.Whale.Watch.v1", out bool first);
        if (!first) return 0;
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopName);
        var state = new HostLifetimeState();
        do
        {
            var action = state.Observe(DesktopRunning());
            if (action == HostAction.StartPet)
            {
                try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--show") { UseShellExecute = false, CreateNoWindow = true }); }
                catch { /* Retry on the next desktop launch; never launch a model turn. */ }
            }
            else if (action == HostAction.StopPet) ControlPipe.Send("quit");
        } while (!stop.WaitOne(2000));
        return 0;
    }
    public static void StopWatcher()
    {
        try { using var stop = EventWaitHandle.OpenExisting(StopName); stop.Set(); }
        catch (WaitHandleCannotBeOpenedException) { }
    }
}
