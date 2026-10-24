namespace CodexPet;

public enum QuotaKind { FiveHour, Weekly }
public sealed record QuotaDrop(QuotaKind Kind, DateTimeOffset? WindowReset);

// Compare quota points, never estimate remaining tokens from activity counters.
public sealed class QuotaDropTracker
{
    private readonly Dictionary<QuotaKind, (double Mark, double Last, DateTimeOffset? Reset)> baselines = new();
    public void Reset() => baselines.Clear();
    public IReadOnlyList<QuotaDrop> Observe(QuotaSnapshot snapshot)
    {
        var changes = new List<QuotaDrop>();
        foreach (var (kind, window) in new[] { (QuotaKind.FiveHour, snapshot.FiveHour), (QuotaKind.Weekly, snapshot.Weekly) })
        {
            if (window.Remaining is not { } value) { baselines.Remove(kind); continue; }
            if (!baselines.TryGetValue(kind, out var old) || value > old.Last + .000001 || old.Reset != window.ResetsAt)
            { baselines[kind] = (value, value, window.ResetsAt); continue; }
            int points = Math.Clamp((int)Math.Floor(old.Mark - value + .000001), 0, 100);
            for (int i = 0; i < points; i++) changes.Add(new(kind, window.ResetsAt));
            baselines[kind] = (old.Mark - points, value, window.ResetsAt);
        }
        return changes;
    }
}
