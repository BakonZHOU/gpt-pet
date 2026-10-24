using System.Windows;

namespace CodexPetPlugin;

public sealed record PetTheme(string Ink, string Outline, string Hint, string Fill);
public sealed record PetSkin(string Id, string Name, string BodyFile, string HairFile, Rect? BodyClip, Rect HairClip, Point HairPivot, PetTheme Theme, bool ExcludeHairFromBody = false);

public static class PetSkins
{
    public static IReadOnlyList<PetSkin> All { get; } = new[]
    {
        new PetSkin("whale", "鲸鱼娘 · 原版", "DSniang1-no-ahoge.png", "DSniang1.png", null, new(237, 10, 170, 60), new(407, 71), new("#536ba9", "#203170", "#9fb0d9", "#FFFFFF")),
        new PetSkin("mutsumi", "若叶睦 · 浅绿", "Skins/Mutsumi.png", "Skins/Mutsumi.png", new Rect(0, 110, 610, 493), new(0, 0, 610, 110), new(364, 110), new("#556B57", "#304B3A", "#8A9B86", "#F5F8EF")),
        new PetSkin("dragon", "GPT娘 · 白龙", "Skins/WhiteDragon.png", "Skins/WhiteDragon.png", new Rect(0, 0, 610, 603), new(232, 0, 171, 110), new(371, 110), new("#77668F", "#534264", "#A59AB8", "#F8F3FC"), true)
    };
    public static PetSkin Find(string? id) => All.FirstOrDefault(s => s.Id == id) ?? All[0];
}
