namespace CodexPetPlugin;

public sealed class QuoteCatalog
{
    private readonly bool followsSkin;
    public string FilePath { get; private set; }
    public QuoteCatalog(string? filePath = null)
    {
        followsSkin = filePath is null;
        FilePath = filePath ?? PluginPaths.QuotesFile("whale");
    }
    public void UseSkin(string? skinId)
    {
        if (followsSkin) FilePath = PluginPaths.QuotesFile(skinId);
    }
    public IReadOnlyList<string> Read()
    {
        try
        {
            return File.ReadAllLines(FilePath).Select(line => line.Trim()).Where(line => line.Length != 0 && !line.StartsWith('#')).ToArray();
        }
        catch (IOException) { return Array.Empty<string>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
    }
    public string? Pick(double probability, double roll)
    {
        if (roll >= probability) return null;
        var lines = Read();
        return lines.Count != 0 ? lines[Random.Shared.Next(lines.Count)] : null;
    }
    public string? PickRequired(string prefix)
    {
        var lines = Read();
        if (lines.Count == 0) return null;
        return lines.FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal)) ?? lines[Random.Shared.Next(lines.Count)];
    }
}
