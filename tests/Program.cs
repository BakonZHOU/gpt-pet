using System.Text.Json;
using CodexPet;

if (args.Contains("app-server")) { await FakeServer(); return; }
int passed = 0;
void Check(bool value, string description)
{
    if (!value) throw new Exception("FAILED: " + description);
    Console.WriteLine("PASS " + description); passed++;
}
QuotaSnapshot Parse(string json) { using var doc = JsonDocument.Parse(json); return QuotaSnapshot.Parse(doc.RootElement); }

Check(HostLifetime.IsDesktopPath(@"C:\Program Files\WindowsApps\OpenAI.Codex_26.1013_x64__test\app\ChatGPT.exe") &&
    !HostLifetime.IsDesktopPath(@"C:\Users\test\AppData\Local\OpenAI\Codex\bin\v1\codex.exe"), "Desktop host detection excludes the pet's own app-server CLI");
var host = new HostLifetimeState();
Check(host.Observe(false) == HostAction.None && host.Observe(true) == HostAction.StartPet && host.Observe(true) == HostAction.None,
    "Desktop launch starts pet once; manual pet exit is respected within the same session");
Check(host.Observe(false) == HostAction.None && host.Observe(true) == HostAction.None,
    "Brief host restart does not close or reopen pet");
Check(host.Observe(false) == HostAction.None && host.Observe(null) == HostAction.None && host.Observe(false) == HostAction.None,
    "Unknown process state cannot close the pet");
Check(host.Observe(false) == HostAction.StopPet && host.Observe(false) == HostAction.None && host.Observe(true) == HostAction.StartPet,
    "Confirmed host exit closes pet once and next desktop launch starts it again");

var q = Parse("""
{"rateLimits":{"primary":{"usedPercent":99,"windowDurationMins":300}},
 "rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":26,"windowDurationMins":10080,"resetsAt":1793087603},"secondary":{"usedPercent":24,"windowDurationMins":300,"resetsAt":1792594903}},"other":{"primary":{"usedPercent":0,"windowDurationMins":300}}},
 "rateLimitResetCredits":{"availableCount":2,"credits":[{"status":"available","expiresAt":1793297962},{"status":"available","expiresAt":1792693758},{"status":"used","expiresAt":1}]}}
""");
Check(q.FiveHour.Remaining == 76 && q.Weekly.Remaining == 74, "Select codex pool, identify windows by duration even when reversed");
Check(q.ResetCount == 2 && q.FirstCreditExpiry?.ToUnixTimeSeconds() == 1792693758, "Authoritative reset count and nearest available expiry");
q = Parse("""{"rateLimits":{"primary":{"windowDurationMins":300,"usedPercent":-2},"secondary":{"windowDurationMins":10080,"usedPercent":110}},"rateLimitResetCredits":{"availableCount":0,"credits":[]}}""");
Check(q.FiveHour.Remaining == 100 && q.Weekly.Remaining == 0 && q.ResetCount == 0, "Legacy fallback, clamp percentages and preserve genuine zero");
q = Parse("""{"rateLimits":{"primary":{"windowDurationMins":300,"usedPercent":null,"resetsAt":9223372036854775807},"secondary":null},"rateLimitResetCredits":null}""");
Check(q.FiveHour.Remaining is null && q.FiveHour.ResetsAt is null && q.ResetCount is null, "Null and invalid times remain unknown");
q = Parse("""{"rateLimits":{"limitId":"other","primary":{"usedPercent":0,"windowDurationMins":300}},"rateLimitsByLimitId":{"other":{"primary":{"usedPercent":0,"windowDurationMins":10080}}}}""");
Check(q.FiveHour.Remaining is null && q.Weekly.Remaining is null, "Do not substitute other model pools");
q = Parse("""{"rateLimits":{"primary":{"usedPercent":10,"windowDurationMins":15},"secondary":{"usedPercent":null,"windowDurationMins":10080}},"rateLimitResetCredits":{"availableCount":null}}""");
Check(q.FiveHour.Remaining is null && q.Weekly.Remaining is null && q.ResetCount is null, "Do not invent missing values or mislabel other durations");
Check(QuotaText.Percent(null) == "--", "Missing percentage has explicit placeholder");
string quoteFile = Path.Combine(Path.GetTempPath(), "CodexPetQuotes-" + Guid.NewGuid().ToString("N") + ".txt");
try
{
    var catalog = new QuoteCatalog(quoteFile);
    File.WriteAllText(quoteFile, "# comment\n好模型... ↓\n\n");
    Check(catalog.Read().SequenceEqual(new[] { "好模型... ↓" }), "Editable quotes ignore comments and blank lines");
    Check(catalog.Pick(.15, .9) is null && catalog.Pick(1, 0) == "好模型... ↓", "Normal outcome is reset count; hidden quotes use configurable probability");
    File.WriteAllText(quoteFile, "我自己修改的语录");
    Check(catalog.Pick(1, 0) == "我自己修改的语录", "Saved quote changes take effect without restart");
    File.WriteAllText(quoteFile, "# no quotes");
    Check(catalog.Pick(1, 0) is null, "Empty quote file falls back to reset count");
}
finally { File.Delete(quoteFile); }
Check(QuotaText.Reset(new(20, DateTimeOffset.Now.AddMinutes(-1)), DateTimeOffset.Now).Contains("等待更新"), "Expired reset time never implies restored quota");
var tracker = new QuotaDropTracker();
var reset = DateTimeOffset.UtcNow.AddHours(1);
QuotaSnapshot Sample(double? five, double? weekly = 74, DateTimeOffset? at = null) => new(new(five, at ?? reset), new(weekly, reset), 2, null, DateTimeOffset.Now);
Check(tracker.Observe(Sample(76.5)).Count == 0, "Initial fetch establishes baseline without popping");
Check(tracker.Observe(Sample(76.1)).Count == 0 && tracker.Observe(Sample(75.8)).Count == 0 && tracker.Observe(Sample(75.5)).Count == 1, "Fractional changes accumulate to one percentage point");
Check(tracker.Observe(Sample(75.5)).Count == 0 && tracker.Observe(Sample(72.5)).Count == 3, "No duplicates; three-point drop queues three separate reminders");
Check(tracker.Observe(Sample(100)).Count == 0 && tracker.Observe(Sample(99)).Count == 1, "Replenishment rebases and next point triggers normally");
Check(tracker.Observe(Sample(null)).Count == 0 && tracker.Observe(Sample(50)).Count == 0, "Missing data cannot fabricate a quota drop");
Check(tracker.Observe(Sample(40, at: reset.AddHours(5))).Count == 0, "A different quota window never replays old usage");
tracker.Reset(); tracker.Observe(Sample(76, 74));
Check(tracker.Observe(Sample(75, 73)).Select(x => x.Kind).Distinct().Count() == 2, "Five-hour and weekly drops are tracked separately");
using (var notification = JsonDocument.Parse("""{"rateLimits":{"limitId":"codex","primary":{"usedPercent":27,"windowDurationMins":300,"resetsAt":1792594903}}}"""))
{
    var merged = QuotaSnapshot.MergeNotification(notification.RootElement, Sample(76));
    Check(merged.FiveHour.Remaining == 73 && merged.Weekly.Remaining == 74 && merged.ResetCount == 2, "Partial push preserves missing weekly quota and reset count");
}
string? originalExecutable = Environment.GetEnvironmentVariable("CODEX_PET_CODEX");
try
{
    Environment.SetEnvironmentVariable("CODEX_PET_CODEX", Environment.ProcessPath);
    using var pushClient = new CodexClient();
    var pushed = new TaskCompletionSource<QuotaSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
    pushClient.QuotaChanged += s => pushed.TrySetResult(s);
    var initial = await pushClient.ReadAsync();
    var update = await pushed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(initial.FiveHour.Remaining == 75 && update.FiveHour.Remaining == 74 && update.Weekly.Remaining == 74 && update.ResetCount == 2,
        "Idle connection receives a push without another read and merges partial fields");
}
finally { Environment.SetEnvironmentVariable("CODEX_PET_CODEX", originalExecutable); }
Check(!CodexClient.IsAllowedMethod("turn/start") && !CodexClient.IsAllowedMethod("thread/start") &&
      !CodexClient.IsAllowedMethod("account/rateLimitResetCredit/consume"), "Transport rejects model turns, conversations and reset redemption");
if (args.Contains("--live"))
{
    using var client = new CodexClient();
    var live = await client.ReadAsync();
    int? firstServer = client.ServerProcessId;
    await client.ReadAsync();
    Check(firstServer.HasValue && client.ServerProcessId == firstServer, "Repeated metadata reads reuse one app-server process");
    Check(live.FiveHour.Remaining is >= 0 and <= 100 && live.Weekly.Remaining is >= 0 and <= 100, "LIVE authenticated Codex read-only RPC returns both quota windows");
    Console.WriteLine($"LIVE 5h={QuotaText.Percent(live.FiveHour.Remaining)}, weekly={QuotaText.Percent(live.Weekly.Remaining)}, resets={live.ResetCount?.ToString() ?? "unknown"}");
    client.Stop();
    Check(client.ServerProcessId is null, "Hiding/closing releases the read-only service");
}
Console.WriteLine($"{passed} checks passed.");

static async Task FakeServer()
{
    while (await Console.In.ReadLineAsync() is { } line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        string? method = root.GetProperty("method").GetString();
        if (!CodexClient.IsAllowedMethod(method)) throw new Exception("Unexpected non-metadata RPC");
        if (!root.TryGetProperty("id", out var id)) continue;
        if (method == "initialize") await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { id = id.GetInt32(), result = new { } }));
        if (method == "account/rateLimits/read")
        {
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new
            {
                id = id.GetInt32(), result = new
                {
                    rateLimits = new { limitId = "codex", primary = new { usedPercent = 25, windowDurationMins = 300, resetsAt = 1792594903 }, secondary = new { usedPercent = 26, windowDurationMins = 10080, resetsAt = 1793087603 } },
                    rateLimitResetCredits = new { availableCount = 2 }
                }
            }));
            await Task.Delay(250);
            await Console.Out.WriteLineAsync("""{"method":"account/rateLimits/updated","params":{"rateLimits":{"limitId":"codex","primary":{"usedPercent":26,"windowDurationMins":300,"resetsAt":1792594903}}}}""");
        }
    }
}
