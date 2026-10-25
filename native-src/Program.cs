using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexPetPlugin;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        PluginPaths.Configure(args);
        if (args.Contains("--mcp")) return PluginMcp.Run();
        if (args.Contains("--launch")) return PluginMcp.EnsureRunning(true) ? 0 : 1;
        if (args.Contains("--ensure")) return PluginMcp.EnsureRunning(false) ? 0 : 1;
        if (args.Contains("--apply-update")) return PluginUpdater.ApplyAfterDesktopExitAsync().GetAwaiter().GetResult();
        
        if (args.Contains("--probe"))
        {
            try
            {
                using var client = new CodexClient();
                var quota = client.ReadAsync().GetAwaiter().GetResult();
                // Only quota data; no account ID, email, credit IDs or credentials.
                string json = JsonSerializer.Serialize(quota, new JsonSerializerOptions { WriteIndented = true });
                int at = Array.IndexOf(args, "--output");
                if (at >= 0 && at + 1 < args.Length) File.WriteAllText(Path.GetFullPath(args[at + 1]), json);
                return 0;
            }
            catch { return 1; }
        }
        using var singleton = new Mutex(true, PluginPaths.MutexName, out bool first);
        bool diagnostic = args.Contains("--preview") || args.Contains("--ui-test");
        if (!first && !diagnostic)
        {
            string command = args.Contains("--quit") ? "quit" : args.Contains("--hide") ? "hide" : args.Contains("--refresh") ? "refresh" : "show";
            return ControlPipe.Send(command) ? 0 : 1;
        }
        if (args.Contains("--quit") || args.Contains("--hide") || args.Contains("--refresh")) return 0;
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show("桌宠遇到了问题，请重新启动。\n" + e.Exception.GetType().Name, "Codex 鲸鱼娘");
            e.Handled = true;
            app.Shutdown(1);
        };
        PluginPaths.PrepareUserFiles();
        var window = new PetWindow(diagnostic, args.Contains("--idle"));
        using var pluginLifetime = diagnostic ? null : new PluginWindowLifetime(window);
        using var controlStop = new CancellationTokenSource();
        if (!diagnostic)
        {
            _ = ControlPipe.ListenAsync(window, controlStop.Token);
            window.Closed += (_, _) => controlStop.Cancel();
        }
        bool uiTestStarted = false;
        if (args.Contains("--ui-test"))
            window.Loaded += async (_, _) =>
            {
                if (uiTestStarted) return;
                uiTestStarted = true;
                try
                {
                    int at = Array.IndexOf(args, "--ui-test");
                    await window.VerifyUiAsync(Path.GetFullPath(args[at + 1]));
                    app.Shutdown();
                }
                catch (Exception e)
                {
                    int at = Array.IndexOf(args, "--ui-test");
                    File.WriteAllText(Path.GetFullPath(args[at + 1]), "FAIL " + e.Message);
                    app.Shutdown(1);
                }
            };
        if (args.Contains("--preview"))
        {
            window.Loaded += (_, _) =>
            {
                int at = Array.IndexOf(args, "--preview");
                if (at + 1 >= args.Length) { app.Shutdown(1); return; }
                window.Dispatcher.InvokeAsync(() =>
                {
                    int skinAt = Array.IndexOf(args, "--skin");
                    if (skinAt >= 0 && skinAt + 1 < args.Length) window.PrepareSkin(args[skinAt + 1]);
                    if (args.Contains("--reset-preview")) window.PreparePreview("reset");
                    if (args.Contains("--quote-preview")) window.PreparePreview("quote");
                    window.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(window.Content as Visual);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(Path.GetFullPath(args[at + 1]))) encoder.Save(stream);
                    app.Shutdown();
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
        }
        return app.Run(window);
    }
}
