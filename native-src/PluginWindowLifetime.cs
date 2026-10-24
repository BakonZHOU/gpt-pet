using System.Diagnostics;
using System.Windows.Threading;

namespace CodexPetPlugin;

public sealed class PluginWindowLifetime : IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly PetWindow window;
    private bool closingAutomatically;
    private int hostAbsent;
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
        hostAbsent = host == false ? hostAbsent + 1 : 0;
        if (PluginPaths.Enabled() && hostAbsent < 2) return;
        closingAutomatically = true; window.Close();
    }
    public void Dispose() => timer.Stop();
}
