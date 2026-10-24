namespace CodexPetPlugin;

public sealed class QuoteCatalog
{
    public string FilePath { get; }
    public QuoteCatalog(string? filePath = null)
    {
        FilePath = filePath ?? PluginPaths.QuotesFile;
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
}
