using System.Text.Json;

namespace CodexPetPlugin;

public sealed class PetSettings
{
    private static string SettingsPath => PluginPaths.SettingsFile;
    public int LayoutVersion { get; set; }
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? BottomGap { get; set; }
    public double Scale { get; set; } = .8;
    public bool Animate { get; set; } = true;
    public bool Sound { get; set; } = true;
    public bool Topmost { get; set; } = true;
    public double QuoteChance { get; set; } = .15;
    public double BreatheSeconds { get; set; } = 4.4;
    public double BreatheAmplitude { get; set; } = 1;
    public double AhogeSeconds { get; set; } = 2.8;
    public double AhogeAmplitude { get; set; } = 1;
    public double Volume { get; set; } = .9;
    public string SoundSet { get; set; } = "duck";
    public string Skin { get; set; } = "dragon";
    public bool BubbleOn { get; set; } = true;
    public bool Mirror { get; set; }
    public static PetSettings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<PetSettings>(File.ReadAllText(SettingsPath)) ?? new();
            // Replace the previous card layout's size and location once.
            if (s.LayoutVersion < 3) { s.Scale = .8; s.Left = null; s.Top = null; }
            if (s.LayoutVersion < 4) { s.Left = null; s.Top = null; s.LayoutVersion = 4; }
            s.Scale = double.IsFinite(s.Scale) ? Math.Clamp(s.Scale, .6, 2.5) : .8;
            s.QuoteChance = double.IsFinite(s.QuoteChance) ? Math.Clamp(s.QuoteChance, 0, 1) : .15;
            s.Skin = PetSkins.Find(s.Skin).Id;
            s.BreatheSeconds = double.IsFinite(s.BreatheSeconds) ? Math.Clamp(s.BreatheSeconds, .1, 6) : 4.4;
            s.AhogeSeconds = double.IsFinite(s.AhogeSeconds) ? Math.Clamp(s.AhogeSeconds, .1, 6) : 2.8;
            if (s.Left.HasValue && !double.IsFinite(s.Left.Value)) s.Left = null;
            if (s.Top.HasValue && !double.IsFinite(s.Top.Value)) s.Top = null;
            if (s.BottomGap is { } gap && (!double.IsFinite(gap) || gap < 0 || gap > 28)) s.BottomGap = null;
            return s;
        }
        catch { return new(); }
    }
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath + ".tmp", JsonSerializer.Serialize(this));
            File.Move(SettingsPath + ".tmp", SettingsPath, true);
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
