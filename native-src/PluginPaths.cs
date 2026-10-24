using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexPetPlugin;

public static class PluginPaths
{
    public static string Root { get; private set; } = "";
    public static string Id { get; private set; } = "";
    public static string Data { get; private set; } = "";
    public static string Key { get; private set; } = "";
    public static string MutexName => "Local\\CodexPet.Plugin." + Key;
    public static string PipeName => "CodexPet.Plugin.Control." + Key;
    public static string SettingsFile => Path.Combine(Data, "settings.json");
    public static string QuotesFile(string? skinId) => Path.Combine(Data, PetSkins.Find(skinId).Id switch
    {
        "mutsumi" => "语录-若叶睦.txt",
        "dragon" => "语录-GPT娘白龙.txt",
        _ => "语录-鲸鱼娘.txt"
    });
    public static string BundledQuotesFile(string? skinId) => Path.Combine(Root, "quotes", PetSkins.Find(skinId).Id + ".txt");
    public static string ConfigFile => Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"), "config.toml");
    public static string Version => typeof(PluginPaths).Assembly.GetName().Version?.ToString() ?? "0.3.0";

    public static string? Argument(string[] args, string name)
    {
        int at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
    public static void Configure(string[] args)
    {
        Root = Path.GetFullPath(Argument(args, "--plugin-root") ?? Path.Combine(AppContext.BaseDirectory, ".."));
        string name = "codex-pet-app";
        try { using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "plugin.json"))); name = doc.RootElement.GetProperty("name").GetString() ?? name; } catch { }
        var directory = new DirectoryInfo(Root);
        string market = directory.Parent?.Name == name ? directory.Parent.Parent?.Name ?? "local-whale-pet" : "local-whale-pet";
        Id = Argument(args, "--plugin-id") ?? name + "@" + market;
        Key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Id)))[..20];
        Data = Path.GetFullPath(Argument(args, "--data-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexPetPlugin", Key));
        Directory.CreateDirectory(Data);
    }
    public static bool Enabled()
    {
        try { return Directory.Exists(Root) && File.Exists(Path.Combine(Root, "plugin.json")) && EnabledIn(File.ReadAllText(ConfigFile), Id); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    public static bool EnabledIn(string config, string id)
    {
        string escaped = Regex.Escape(id);
        var section = Regex.Match(config, "(?ms)^\\s*\\[plugins\\.(?:\"" + escaped + "\"|'" + escaped + "')\\][ \\t]*\\r?\\n(?<body>.*?)(?=^\\s*\\[|\\z)");
        if (!section.Success) return false;
        var enabled = Regex.Match(section.Groups["body"].Value, @"(?m)^\s*enabled\s*=\s*(true|false)\s*(?:#.*)?$");
        return enabled.Success && enabled.Groups[1].Value == "true";
    }
    public static void PrepareUserFiles()
    {
        // Migrate only this plugin's previous in-app data, never the installed desktop app.
        string? previous = Environment.GetEnvironmentVariable("PLUGIN_DATA");
        if (!File.Exists(SettingsFile) && previous is not null)
        {
            try
            {
                string json = File.ReadAllText(Path.Combine(previous, "preferences.json"));
                var settings = JsonSerializer.Deserialize<PetSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("bubbles", out var bubbles)) settings.BubbleOn = bubbles.GetBoolean();
                settings.LayoutVersion = 4; settings.Save();
            }
            catch (Exception e) when (e is IOException or JsonException or InvalidOperationException) { }
        }
        string legacyFile = Path.Combine(Data, "语录.txt");
        bool legacyCustomized = File.Exists(legacyFile) && !SameQuotesExceptRequired(legacyFile, Path.Combine(Root, "quotes.txt"));
        string[]? previousLines = null;
        if (!File.Exists(legacyFile) && previous is not null)
        {
            try { previousLines = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(previous, "quotes.json"))); } catch (Exception e) when (e is IOException or JsonException) { }
        }
        foreach (var skin in PetSkins.All)
        {
            string target = QuotesFile(skin.Id);
            if (!File.Exists(target))
            {
                if (legacyCustomized) File.Copy(legacyFile, target, false);
                else if (previousLines is not null) File.WriteAllLines(target, previousLines);
                else File.Copy(BundledQuotesFile(skin.Id), target, false);
            }
            if (!File.ReadLines(target).Select(line => line.Trim()).Any(line => line.StartsWith("区", StringComparison.Ordinal)))
                File.AppendAllText(target, Environment.NewLine + "区…" + Environment.NewLine);
        }
    }
    private static bool SameQuotesExceptRequired(string first, string second)
    {
        static string[] Lines(string file) => File.ReadLines(file).Select(line => line.Trim())
            .Where(line => line.Length != 0 && !line.StartsWith('#') && !line.StartsWith("区", StringComparison.Ordinal)).ToArray();
        try { return Lines(first).SequenceEqual(Lines(second)); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    public static string SessionStamp()
    {
        string host = HostLifetime.DesktopSession() ?? "unknown";
        long changed = File.Exists(ConfigFile) ? File.GetLastWriteTimeUtc(ConfigFile).Ticks : 0;
        return host + "|" + changed + "|" + Version;
    }
    public static bool Suppressed()
    {
        try { return File.ReadAllText(Path.Combine(Data, "manual-exit.txt")) == SessionStamp(); } catch (IOException) { return false; }
    }
    public static void Suppress() => File.WriteAllText(Path.Combine(Data, "manual-exit.txt"), SessionStamp());
    public static void ClearSuppression() { string file = Path.Combine(Data, "manual-exit.txt"); if (File.Exists(file)) File.Delete(file); }
}
