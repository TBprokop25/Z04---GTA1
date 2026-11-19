using System.Numerics;
using Raylib_cs;

namespace Z04;

/// <summary>
/// Spravce textur: nacita PNG z ./assets na pozadani a kesuje je.
/// Pokud slozka chybi nebo soubor neexistuje, vraci prazdnou texturu (Id 0)
/// a hra spadne zpet na puvodni kresleni primitivy.
/// </summary>
public static class Assets
{
    static string Root => Path.Combine(AppContext.BaseDirectory, "assets");
    static readonly Dictionary<string, Texture2D> _cache = new();
    public static bool Ready;

    public static void Init() => Ready = Directory.Exists(Root);

    public static Texture2D Get(string rel)
    {
        if (_cache.TryGetValue(rel, out var cached)) return cached;
        Texture2D tex = default;
        try
        {
            var full = Path.Combine(Root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full))
            {
                tex = Raylib.LoadTexture(full);
                if (tex.Id != 0) Raylib.SetTextureFilter(tex, TextureFilter.Bilinear);
            }
        }
        catch { tex = default; }
        _cache[rel] = tex;
        return tex;
    }

    public static bool Have(string rel) => Get(rel).Id != 0;

    /// <summary>Vykresli texturu vycentrovanou na pos, otocenou (stupne) a zmensenou.</summary>
    public static void DrawCentered(string rel, Vector2 pos, float rotDeg, float scale, Color tint)
    {
        var t = Get(rel);
        if (t.Id == 0) return;
        float w = t.Width * scale, h = t.Height * scale;
        Raylib.DrawTexturePro(t, new Rectangle(0, 0, t.Width, t.Height),
            new Rectangle(pos.X, pos.Y, w, h), new Vector2(w / 2, h / 2), rotDeg, tint);
    }

    /// <summary>Vykresli texturu s pivotem danym v pixelech zdroje (napr. vez tanku).</summary>
    public static void DrawPivot(string rel, Vector2 pos, float rotDeg, float scale, Vector2 pivotPx, Color tint)
    {
        var t = Get(rel);
        if (t.Id == 0) return;
        float w = t.Width * scale, h = t.Height * scale;
        Raylib.DrawTexturePro(t, new Rectangle(0, 0, t.Width, t.Height),
            new Rectangle(pos.X, pos.Y, w, h), new Vector2(pivotPx.X * scale, pivotPx.Y * scale), rotDeg, tint);
    }

    /// <summary>Roztáhne texturu do obdelniku (pro podlahy, pozadi, panely).</summary>
    public static void DrawStretch(string rel, Rectangle dst, Color tint)
    {
        var t = Get(rel);
        if (t.Id == 0) return;
        Raylib.DrawTexturePro(t, new Rectangle(0, 0, t.Width, t.Height), dst, Vector2.Zero, 0, tint);
    }

    public static void Unload()
    {
        foreach (var t in _cache.Values) if (t.Id != 0) Raylib.UnloadTexture(t);
        _cache.Clear();
    }
}
