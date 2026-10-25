using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace CodexPetPlugin;

public static class PluginMcp
{
    public static bool Running()
    {
        try { using var mutex = Mutex.OpenExisting(PluginPaths.MutexName); return true; }
        catch (WaitHandleCannotBeOpenedException) { return false; }
    }
    public static bool EnsureRunning(bool explicitShow)
    {
        if (!PluginPaths.Enabled()) return false;
        using var gate = new Mutex(false, PluginPaths.MutexName + ".Launch");
        try { if (!gate.WaitOne(TimeSpan.FromSeconds(15))) return false; } catch (AbandonedMutexException) { }
        try
        {
            if (explicitShow) PluginPaths.ClearSuppression();
            if (Running() && RunningVersion() is { } runningVersion && runningVersion < typeof(PluginMcp).Assembly.GetName().Version)
            {
                ControlPipe.Send("quit");
                for (int attempt = 0; attempt < 60 && Running(); attempt++) Thread.Sleep(50);
            }
            if (Running())
            {
                if (!explicitShow || ControlPipe.Send("show")) return true;
                StopUnresponsiveWindow();
                for (int attempt = 0; attempt < 40 && Running(); attempt++) Thread.Sleep(50);
                if (Running()) return false;
            }
            if (!explicitShow && PluginPaths.Suppressed()) return false;
            PluginPaths.PrepareUserFiles();
            string source = Path.Combine(PluginPaths.Root, "native", "CodexPetPlugin.exe");
            string digest;
            using (var stream = File.OpenRead(source)) digest = Convert.ToHexString(SHA256.HashData(stream))[..16];
            string runtime = Path.Combine(PluginPaths.Data, "runtime", digest);
            if (!File.Exists(Path.Combine(runtime, "CodexPetPlugin.exe")))
            {
                // Run outside the plugin install cache so disabling/uninstalling can remove it.
                Directory.CreateDirectory(runtime);
                foreach (string file in Directory.GetFiles(Path.GetDirectoryName(source)!, "*", SearchOption.AllDirectories))
                {
                    if (string.Equals(file, source, StringComparison.OrdinalIgnoreCase)) continue;
                    string target = Path.Combine(runtime, Path.GetRelativePath(Path.GetDirectoryName(source)!, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, true);
                }
                // The executable is copied last as the completion marker.
                File.Copy(source, Path.Combine(runtime, "CodexPetPlugin.exe"), true);
            }
            // ShellExecute launches a GUI without inheriting the MCP pipe handles.
            // Redirecting just the three standard streams still leaks other
            // inheritable pipe handles through CreateProcess on Windows.
            var start = new ProcessStartInfo(Path.Combine(runtime, "CodexPetPlugin.exe")) { UseShellExecute = true, WorkingDirectory = runtime };
            foreach (var value in new[] { "--plugin-root", PluginPaths.Root, "--plugin-id", PluginPaths.Id, "--data-dir", PluginPaths.Data }) start.ArgumentList.Add(value);
            try { File.Delete(Path.Combine(PluginPaths.Data, "window.pid")); } catch (IOException) { }
            using var child = Process.Start(start);
            if (child is null) return false;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                if (Running()) return true;
                try { if (child.HasExited) break; } catch (InvalidOperationException) { break; }
                Thread.Sleep(50);
            }
            return Running();
        }
        finally { gate.ReleaseMutex(); }
    }
    private static Version? RunningVersion()
    {
        try
        {
            int id = int.Parse(File.ReadAllText(Path.Combine(PluginPaths.Data, "window.pid")));
            using var process = Process.GetProcessById(id);
            string? file = process.MainModule?.FileName;
            string prefix = Path.Combine(PluginPaths.Data, "runtime") + Path.DirectorySeparatorChar;
            return file is not null && file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Version.TryParse(FileVersionInfo.GetVersionInfo(file).FileVersion, out var version) ? version : null;
        }
        catch (Exception e) when (e is IOException or ArgumentException or FormatException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }
    private static void StopUnresponsiveWindow()
    {
        try
        {
            int id = int.Parse(File.ReadAllText(Path.Combine(PluginPaths.Data, "window.pid")));
            using var process = Process.GetProcessById(id);
            string? file = process.MainModule?.FileName;
            string prefix = Path.Combine(PluginPaths.Data, "runtime") + Path.DirectorySeparatorChar;
            if (file is not null && file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is IOException or ArgumentException or FormatException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
    public static int Run()
    {
        string? line;
        while ((line = Console.ReadLine()) is not null)
        {
            JsonElement? id = null;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var request = doc.RootElement;
                if (!request.TryGetProperty("id", out var requestId)) continue;
                id = requestId.Clone();
                string method = request.GetProperty("method").GetString() ?? "";
                object result;
                if (method == "initialize")
                {
                    bool launched = EnsureRunning(false);
                    result = new { protocolVersion = request.GetProperty("params").GetProperty("protocolVersion").GetString(), serverInfo = new { name = "codex-pet-plugin", version = "0.4.0" }, capabilities = new { tools = new { } } };
                }
                else if (method == "ping") result = new { };
                else if (method == "tools/list") result = new { tools = new object[] {
                    new { name = "pet_show", title = "显示插件桌宠", description = "显示插件自带的独立桌面浮窗，不控制已安装的桌宠软件。", inputSchema = new { type = "object", properties = new { }, additionalProperties = false }, annotations = new { readOnlyHint = false, destructiveHint = false, openWorldHint = false } },
                    new { name = "pet_status", title = "插件桌宠状态", description = "只读插件自带进程的运行状态，不调用模型或查询聊天。", inputSchema = new { type = "object", properties = new { }, additionalProperties = false }, annotations = new { readOnlyHint = true, destructiveHint = false, openWorldHint = false } }
                } };
                else if (method == "resources/list") result = new { resources = Array.Empty<object>() };
                else if (method == "resources/templates/list") result = new { resourceTemplates = Array.Empty<object>() };
                else if (method == "tools/call")
                {
                    string tool = request.GetProperty("params").GetProperty("name").GetString() ?? "";
                    if (tool is not ("pet_show" or "pet_status")) throw new InvalidOperationException("未知工具");
                    bool running = tool == "pet_show" ? EnsureRunning(true) : Running();
                    result = new { content = new[] { new { type = "text", text = running ? "插件桌宠已运行，右键人物设置。" : "插件桌宠未运行。" } }, structuredContent = new { running } };
                }
                else throw new InvalidOperationException("不支持的方法");
                Console.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }));
            }
            catch (Exception e)
            {
                if (id is not null) Console.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code = -32603, message = e.GetType().Name + ": " + e.Message } }));
            }
        }
        return 0;
    }
}
