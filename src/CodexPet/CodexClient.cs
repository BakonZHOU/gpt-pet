using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace CodexPet;

// One metadata-only connection: push notifications plus serialized, deduplicated reads.
public sealed class CodexClient : IDisposable
{
    private sealed class Session
    {
        public required Process Process;
        public readonly CancellationTokenSource Stop = new();
        public readonly ConcurrentDictionary<int, TaskCompletionSource<JsonDocument>> Pending = new();
    }
    private Session? session;
    private QuotaSnapshot? latest;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;
    private int nextId;
    public event Action<QuotaSnapshot>? QuotaChanged;
    internal int? ServerProcessId => session?.Process is { HasExited: false } p ? p.Id : null;
    internal static bool IsAllowedMethod(string? method) => method is "initialize" or "initialized" or "account/rateLimits/read";
    public static string FindExecutable()
    {
        string? configured = Environment.GetEnvironmentVariable("CODEX_PET_CODEX");
        if (!string.IsNullOrWhiteSpace(configured))
            return File.Exists(configured) ? configured : throw new InvalidOperationException("CODEX_PET_CODEX 指向的文件不存在。");
        var candidates = new List<string>();
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (Directory.Exists(root)) candidates.AddRange(Directory.GetFiles(root, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc));
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (!string.IsNullOrWhiteSpace(directory)) candidates.Add(Path.Combine(directory.Trim('"'), "codex.exe"));
        return candidates.FirstOrDefault(File.Exists) ?? throw new InvalidOperationException("未找到 Codex。安装 Codex 或设置 CODEX_PET_CODEX 为 codex.exe 完整路径。");
    }


    public async Task<QuotaSnapshot> ReadAsync(CancellationToken stop = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        await gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (session is null || session.Process.HasExited)
            {
                Stop();
                var psi = new ProcessStartInfo(FindExecutable())
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                    RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = AppContext.BaseDirectory
                };
                psi.ArgumentList.Add("app-server"); psi.ArgumentList.Add("--listen"); psi.ArgumentList.Add("stdio://");
                var active = new Session { Process = new Process { StartInfo = psi } };
                session = active;
                if (!active.Process.Start()) throw new InvalidOperationException("Codex 用量服务未能启动。");
                _ = ReceiveAsync(active);
                _ = DrainAsync(active.Process.StandardError, active.Stop.Token);
                using var init = await RequestAsync(active, "initialize", new { clientInfo = new { name = "codex_pet", title = "Codex Pet", version = "0.1.0" } }, token);
                await SendAsync(active.Process, new { method = "initialized" }, token);
            }
            var current = session;
            using var response = await RequestAsync(current, "account/rateLimits/read", null, token);
            var result = QuotaSnapshot.Parse(response.RootElement.GetProperty("result"));
            latest = result;
            return result;
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        { Stop(); throw new InvalidOperationException("查询超时，请检查 Codex 登录和网络。"); }
        catch { Stop(); throw; }
        finally { gate.Release(); }
    }
    private async Task<JsonDocument> RequestAsync(Session active, string method, object? parameters, CancellationToken token)
    {
        int id = Interlocked.Increment(ref nextId);
        var completion = new TaskCompletionSource<JsonDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        active.Pending[id] = completion;
        try
        {
            await SendAsync(active.Process, new { id, method, @params = parameters }, token);
            var document = await completion.Task.WaitAsync(token);
            if (document.RootElement.TryGetProperty("error", out _))
            { document.Dispose(); throw new InvalidOperationException("Codex 未能提供用量，请检查订阅账号登录。"); }
            return document;
        }
        finally { active.Pending.TryRemove(id, out _); }
    }
    private async Task ReceiveAsync(Session active)
    {
        try
        {
            while (await active.Process.StandardOutput.ReadLineAsync(active.Stop.Token).ConfigureAwait(false) is { } line)
            {
                JsonDocument document;
                try { document = JsonDocument.Parse(line); } catch (JsonException) { continue; }
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out int n) &&
                    active.Pending.TryRemove(n, out var completion))
                {
                    if (!completion.TrySetResult(document)) document.Dispose();
                    continue;
                }
                using (document)
                    if (session == active && root.TryGetProperty("method", out var method) && method.GetString() == "account/rateLimits/updated" &&
                        root.TryGetProperty("params", out var payload) && payload.ValueKind == JsonValueKind.Object)
                    {
                        try
                        {
                            var updated = QuotaSnapshot.MergeNotification(payload, latest);
                            latest = updated;
                            QuotaChanged?.Invoke(updated);
                        }
                        catch (JsonException) { }
                        catch (InvalidOperationException) { }
                    }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            foreach (var entry in active.Pending)
                if (active.Pending.TryRemove(entry.Key, out var pending))
                    pending.TrySetException(new InvalidOperationException("Codex 用量连接已断开。"));
        }
    }
    private static async Task DrainAsync(StreamReader reader, CancellationToken token)
    { try { while (await reader.ReadLineAsync(token).ConfigureAwait(false) is not null) { } } catch (OperationCanceledException) { } catch (IOException) { } catch (ObjectDisposedException) { } }
    private static Task SendAsync(Process proc, object message, CancellationToken token)
    {
        string json = JsonSerializer.Serialize(message);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("method", out var method) || !IsAllowedMethod(method.GetString()))
            throw new InvalidOperationException("桌宠仅允许读取额度，已拒绝其他调用。");
        return proc.StandardInput.WriteLineAsync(json.AsMemory(), token);
    }
    public void Stop()
    {
        var active = session; session = null; latest = null;
        if (active is null) return;
        active.Stop.Cancel();
        foreach (var entry in active.Pending)
            if (active.Pending.TryRemove(entry.Key, out var pending))
                pending.TrySetException(new InvalidOperationException("用量查询已暂停。"));
        try { if (!active.Process.HasExited) active.Process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        active.Process.Dispose();
    }
    public void Dispose() { disposed = true; Stop(); }
}
