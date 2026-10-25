using System.Diagnostics;
using System.Text.Json;

namespace CodexPetPlugin;

public sealed record PluginUpdateResult(string Title, string Version, string Detail);

public static class PluginUpdater
{
    public static async Task<PluginUpdateResult> CheckAndInstallAsync(CancellationToken cancellationToken)
    {
        string[] identity = PluginPaths.Id.Split('@', 2);
        if (identity.Length != 2)
            return new("检查失败", "--", "插件来源无效");

        string current = PluginPaths.Version.TrimEnd('0').TrimEnd('.');
        var refresh = await RunCodexAsync(["plugin", "marketplace", "upgrade", identity[1], "--json"], cancellationToken);
        if (refresh.ExitCode != 0)
            return new("无法更新", current, ShortError(refresh.Error));

        string latest;
        try { latest = await ReadMarketplaceVersionAsync(identity[0], identity[1], cancellationToken); }
        catch { return new("检查失败", current, "无法读取市场版本"); }

        if (!Version.TryParse(latest, out var newest) || !Version.TryParse(PluginPaths.Version, out var running) || newest <= running)
            return new("已是最新", "v" + latest, "GPT原生市场已检查");

        return ScheduleInstall()
            ? new("发现新版", "v" + latest, "关闭GPT后自动更新")
            : new("等待更新", "v" + latest, "更新任务已在运行");
    }

    private static bool ScheduleInstall()
    {
        string? executable = Environment.ProcessPath;
        if (executable is null) return false;
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (string value in new[] { "--apply-update", "--plugin-root", PluginPaths.Root, "--plugin-id", PluginPaths.Id, "--data-dir", PluginPaths.Data })
            start.ArgumentList.Add(value);
        using var helper = Process.Start(start);
        return helper is not null;
    }

    public static async Task<int> ApplyAfterDesktopExitAsync()
    {
        using var singleton = new Mutex(true, PluginPaths.MutexName + ".Updater", out bool first);
        if (!first) return 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromHours(24));
        try
        {
            while (HostLifetime.DesktopRunning() is not false)
                await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
            await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
            string marketplace = PluginPaths.Id.Split('@', 2).Last();
            var refresh = await RunCodexAsync(["plugin", "marketplace", "upgrade", marketplace, "--json"], timeout.Token);
            if (refresh.ExitCode != 0) return 2;
            var install = await RunCodexAsync(["plugin", "add", PluginPaths.Id, "--json"], timeout.Token);
            return install.ExitCode == 0 ? 0 : 3;
        }
        catch (OperationCanceledException) { return 4; }
    }

    private static async Task<string> ReadMarketplaceVersionAsync(string pluginName, string marketplaceName, CancellationToken cancellationToken)
    {
        var listing = await RunCodexAsync(["plugin", "marketplace", "list", "--json"], cancellationToken);
        if (listing.ExitCode != 0) throw new InvalidOperationException(listing.Error);
        using var listJson = JsonDocument.Parse(listing.Output);
        string root = listJson.RootElement.GetProperty("marketplaces").EnumerateArray()
            .First(item => item.GetProperty("name").GetString() == marketplaceName).GetProperty("root").GetString()
            ?? throw new InvalidOperationException("Marketplace root missing");
        string rootFull = Path.GetFullPath(root);
        using var marketJson = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(rootFull, ".agents", "plugins", "marketplace.json"), cancellationToken));
        string relative = marketJson.RootElement.GetProperty("plugins").EnumerateArray()
            .First(item => item.GetProperty("name").GetString() == pluginName).GetProperty("source").GetProperty("path").GetString()
            ?? throw new InvalidOperationException("Plugin source missing");
        string pluginRoot = Path.GetFullPath(Path.Combine(rootFull, relative));
        if (!pluginRoot.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Plugin source escaped marketplace root");
        using var pluginJson = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(pluginRoot, "plugin.json"), cancellationToken));
        return pluginJson.RootElement.GetProperty("version").GetString() ?? throw new InvalidOperationException("Plugin version missing");
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunCodexAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("codex")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动Codex");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await output, await error);
    }

    private static string ShortError(string text)
    {
        string line = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "请在GPT里重试";
        return line.Length <= 18 ? line : line[..18] + "…";
    }
}
