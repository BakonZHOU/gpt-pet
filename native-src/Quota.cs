using System.Text.Json;

namespace CodexPetPlugin;

public sealed record QuotaWindow(double? Remaining, DateTimeOffset? ResetsAt);
public sealed record QuotaSnapshot(QuotaWindow FiveHour, QuotaWindow Weekly, int? ResetCount, DateTimeOffset? FirstCreditExpiry, DateTimeOffset FetchedAt)
{
    public static QuotaSnapshot MergeNotification(JsonElement notification, QuotaSnapshot? previous)
    {
        var parsed = Parse(notification);
        if (previous is null) return parsed;
        JsonElement bucket = default;
        if (notification.TryGetProperty("rateLimitsByLimitId", out var pools) && pools.ValueKind == JsonValueKind.Object)
            pools.TryGetProperty("codex", out bucket);
        if (bucket.ValueKind != JsonValueKind.Object && notification.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object &&
            (!legacy.TryGetProperty("limitId", out var id) || id.ValueKind == JsonValueKind.Null || id.GetString() == "codex")) bucket = legacy;
        bool ContainsWindow(int duration) => bucket.ValueKind == JsonValueKind.Object && new[] { "primary", "secondary" }.Any(key =>
            bucket.TryGetProperty(key, out var w) && w.ValueKind == JsonValueKind.Object && w.TryGetProperty("windowDurationMins", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetInt32(out int minutes) && minutes == duration);
        bool creditsPresent = notification.TryGetProperty("rateLimitResetCredits", out _);
        bool fivePresent = ContainsWindow(300), weekPresent = ContainsWindow(10080);
        if (!fivePresent && !weekPresent && !creditsPresent) return previous;
        return new(fivePresent ? parsed.FiveHour : previous.FiveHour, weekPresent ? parsed.Weekly : previous.Weekly,
            creditsPresent ? parsed.ResetCount : previous.ResetCount, creditsPresent ? parsed.FirstCreditExpiry : previous.FirstCreditExpiry, DateTimeOffset.Now);
    }
    public static QuotaSnapshot Parse(JsonElement result)
    {
        JsonElement bucket = default;
        // Never combine quotas belonging to different metered pools/models.
        if (result.TryGetProperty("rateLimitsByLimitId", out var pools) && pools.ValueKind == JsonValueKind.Object &&
            pools.TryGetProperty("codex", out var codex) && codex.ValueKind == JsonValueKind.Object) bucket = codex;
        else if (result.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object &&
                 (!legacy.TryGetProperty("limitId", out var id) || id.ValueKind == JsonValueKind.Null || id.GetString() == "codex")) bucket = legacy;

        QuotaWindow five = new(null, null), week = new(null, null);
        if (bucket.ValueKind == JsonValueKind.Object)
            foreach (var key in new[] { "primary", "secondary" })
                if (bucket.TryGetProperty(key, out var w) && w.ValueKind == JsonValueKind.Object &&
                    w.TryGetProperty("windowDurationMins", out var mins) && mins.ValueKind == JsonValueKind.Number && mins.TryGetInt32(out int duration))
                {
                    double? remaining = w.TryGetProperty("usedPercent", out var used) && used.ValueKind == JsonValueKind.Number && used.TryGetDouble(out double n) && double.IsFinite(n)
                        ? Math.Clamp(100 - n, 0, 100) : null;
                    var window = new QuotaWindow(remaining, Timestamp(w, "resetsAt"));
                    if (duration == 300) five = window;
                    if (duration == 10080) week = window;
                }
        int? count = null;
        DateTimeOffset? expiry = null;
        if (result.TryGetProperty("rateLimitResetCredits", out var credits) && credits.ValueKind == JsonValueKind.Object)
        {
            if (credits.TryGetProperty("availableCount", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out int v) && v >= 0) count = v;
            if (credits.TryGetProperty("credits", out var rows) && rows.ValueKind == JsonValueKind.Array)
                foreach (var row in rows.EnumerateArray())
                    if (row.ValueKind == JsonValueKind.Object && row.TryGetProperty("status", out var status) && status.GetString() == "available")
                    {
                        var t = Timestamp(row, "expiresAt");
                        if (t.HasValue && (!expiry.HasValue || t < expiry)) expiry = t;
                    }
        }
        return new(five, week, count, expiry, DateTimeOffset.Now);
    }

    private static DateTimeOffset? Timestamp(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var t) || t.ValueKind != JsonValueKind.Number || !t.TryGetInt64(out long value)) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(value); } catch (ArgumentOutOfRangeException) { return null; }
    }
}

public static class QuotaText
{
    public static string Percent(double? value) => value is { } n ? $"{n:0.#}%" : "--";
    public static string Reset(QuotaWindow? window, DateTimeOffset now)
    {
        if (window?.ResetsAt is not { } time) return "重置时间未提供";
        var left = time - now;
        if (left <= TimeSpan.Zero) return "已到重置时间，等待更新";
        string delta = left.TotalDays >= 1 ? $"{(int)left.TotalDays}天 {left.Hours}小时" : left.TotalHours >= 1
            ? $"{(int)left.TotalHours}小时 {left.Minutes}分" : $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))}分钟";
        return $"{delta}后重置 · {time.ToLocalTime():MM/dd HH:mm}";
    }
}
