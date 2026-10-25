using System.Diagnostics;
using System.Windows.Threading;

namespace CodexPetPlugin;

public sealed class PluginWindowLifetime : IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch startup = Stopwatch.StartNew();
    private readonly PetWindow window;
    private bool closingAutomatically;
    private int hostAbsent;
    private int pluginDisabled;
    private bool hostObserved;
    public PluginWindowLifetime(PetWindow window)
    {
        this.window = window;
        timer.Tick += (_, _) => Check();
        window.Loaded += (_, _) =>
        {
            File.WriteAllText(Path.Combine(PluginPaths.Data, "window.pid"), Environment.ProcessId.ToString());
            timer.Start(); Check();
        };
        window.Closed += (_, _) =>
        {
            timer.Stop();
            try { File.Delete(Path.Combine(PluginPaths.Data, "window.pid")); } catch (IOException) { }
            if (!closingAutomatically) PluginPaths.Suppress();
        };
    }
    private void Check()
    {
        bool? host = HostLifetime.DesktopRunning();
        if (host == true) { hostObserved = true; hostAbsent = 0; }
        else if (host == false && hostObserved) hostAbsent++;
        else hostAbsent = 0;
        pluginDisabled = PluginPaths.Enabled() ? 0 : pluginDisabled + 1;

        // SessionStart can run while the desktop process and config are still
        // being replaced during startup.  Do not mistake that short interval
        // for a real exit/disable: it caused the window to flash, close, and
        // wait for the much later MCP discovery fallback.
        if (startup.Elapsed < TimeSpan.FromSeconds(15)) return;
        if (pluginDisabled < 3 && (!hostObserved || hostAbsent < 3)) return;
        closingAutomatically = true; window.Close();
    }
    public void Dispose() => timer.Stop();
}
