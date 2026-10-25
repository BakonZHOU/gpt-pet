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

        var install = await RunCodexAsync(["plugin", "add", PluginPaths.Id, "--json"], cancellationToken);
        if (install.ExitCode != 0)
            return new("安装失败", current, ShortError(install.Error));

        string latest = current;
        try
        {
            using var json = JsonDocument.Parse(install.Output);
            latest = json.RootElement.GetProperty("version").GetString() ?? current;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { }

        bool changed = Version.TryParse(latest, out var newest) && Version.TryParse(PluginPaths.Version, out var running) && newest > running;
        return changed
            ? new("已更新", "v" + latest, "重启GPT后生效")
            : new("已是最新", "v" + latest, "GPT原生市场已检查");
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
