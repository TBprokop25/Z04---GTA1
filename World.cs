using System.Numerics;
using Raylib_cs;

namespace Z04;

/// <summary>Mesto: mrizka silnic (Grid+1 x Grid+1), mezi nimi bloky s budovami nebo parkem.</summary>
public class World
{
    public const int Grid = 8;
    public const float Cell = 500f;
    public const float Road = 140f;
    public const float Size = Grid * Cell + Road;

    public readonly List<Rectangle> Buildings = new();
    public readonly List<Color> BuildingColors = new();
    public readonly List<Rectangle> Parks = new();

    public World(int seed)
    {
        var r = new Random(seed);
        const float pad = 22f, gap = 24f;
        float bs = Cell - Road;

        for (int i = 0; i < Grid; i++)
        {
            for (int j = 0; j < Grid; j++)
            {
                float bx = i * Cell + Road, by = j * Cell + Road;
                double roll = r.NextDouble();

                if (roll < 0.15)
                {
                    Parks.Add(new Rectangle(bx + pad, by + pad, bs - 2 * pad, bs - 2 * pad));
                }
                else if (roll < 0.55)
                {
                    if (r.Next(2) == 0)
                    {
                        float w = (bs - 2 * pad - gap) / 2;
                        AddBuilding(r, bx + pad, by + pad, w, bs - 2 * pad);
                        AddBuilding(r, bx + pad + w + gap, by + pad, w, bs - 2 * pad);
                    }
                    else
                    {
                        float h = (bs - 2 * pad - gap) / 2;
                        AddBuilding(r, bx + pad, by + pad, bs - 2 * pad, h);
                        AddBuilding(r, bx + pad, by + pad + h + gap, bs - 2 * pad, h);
                    }
                }
                else
                {
                    AddBuilding(r, bx + pad, by + pad, bs - 2 * pad, bs - 2 * pad);
                }
            }
        }
    }

    void AddBuilding(Random r, float x, float y, float w, float h)
    {
        Buildings.Add(new Rectangle(x, y, w, h));
        BuildingColors.Add(Pal.Roofs[r.Next(Pal.Roofs.Length)]);
    }

    public static Vector2 Node(int i, int j) => new(i * Cell + Road / 2, j * Cell + Road / 2);
    public static int NodeId(int i, int j) => j * (Grid + 1) + i;

    public bool OnRoad(Vector2 p)
    {
        if (p.X < 0 || p.Y < 0 || p.X > Size || p.Y > Size) return false;
        return p.X % Cell < Road || p.Y % Cell < Road;
    }

    public bool PointInBuilding(Vector2 p)
    {
        foreach (var b in Buildings)
            if (p.X >= b.X && p.X <= b.X + b.Width && p.Y >= b.Y && p.Y <= b.Y + b.Height)
                return true;
        return false;
    }

    /// <summary>Vytlaci kruh (stred c, polomer r) z budov a z hranic sveta. Vraci celkovy posun.</summary>
    public Vector2 Resolve(ref Vector2 c, float r)
    {
        Vector2 total = Vector2.Zero;

        foreach (var b in Buildings)
        {
            if (c.X < b.X - r - 1 || c.X > b.X + b.Width + r + 1 ||
                c.Y < b.Y - r - 1 || c.Y > b.Y + b.Height + r + 1) continue;

            float cx = Math.Clamp(c.X, b.X, b.X + b.Width);
            float cy = Math.Clamp(c.Y, b.Y, b.Y + b.Height);
            var d = new Vector2(c.X - cx, c.Y - cy);
            float len = d.Length();
            Vector2 push;

            if (len > 0.0001f)
            {
                if (len >= r) continue;
                push = d / len * (r - len);
            }
            else
            {
                // stred uvnitr budovy - vytlacit nejblizsi stranou
                float l = c.X - b.X, rt = b.X + b.Width - c.X;
                float t = c.Y - b.Y, bt = b.Y + b.Height - c.Y;
                float m = MathF.Min(MathF.Min(l, rt), MathF.Min(t, bt));
                if (m == l) push = new Vector2(-(l + r), 0);
                else if (m == rt) push = new Vector2(rt + r, 0);
                else if (m == t) push = new Vector2(0, -(t + r));
                else push = new Vector2(0, bt + r);
            }

            c += push;
            total += push;
        }

        float min = r, max = Size - r;
        if (c.X < min) { total.X += min - c.X; c.X = min; }
        if (c.X > max) { total.X += max - c.X; c.X = max; }
        if (c.Y < min) { total.Y += min - c.Y; c.Y = min; }
        if (c.Y > max) { total.Y += max - c.Y; c.Y = max; }

        return total;
    }

    public void Draw(float x0, float y0, float x1, float y1)
    {
        float bs = Cell - Road;

        // asfalt + chodniky (bloky)
        Raylib.DrawRectangle(0, 0, (int)Size, (int)Size, Pal.Road);
        for (int i = 0; i < Grid; i++)
        {
            for (int j = 0; j < Grid; j++)
            {
                float bx = i * Cell + Road, by = j * Cell + Road;
                if (bx > x1 || bx + bs < x0 || by > y1 || by + bs < y0) continue;
                Raylib.DrawRectangle((int)bx, (int)by, (int)bs, (int)bs, Pal.Sidewalk);
            }
        }

        // prerusovana cara uprostred silnic
        for (int k = 0; k <= Grid; k++)
        {
            float c = k * Cell + Road / 2;
            if (c > y0 - 4 && c < y1 + 4)
            {
                for (float x = MathF.Floor(MathF.Max(x0, 0) / 50) * 50; x < MathF.Min(x1, Size); x += 50)
                    if (x % Cell >= Road + 8) Raylib.DrawRectangle((int)x, (int)c - 2, 26, 4, Pal.Line);
            }
            if (c > x0 - 4 && c < x1 + 4)
            {
                for (float y = MathF.Floor(MathF.Max(y0, 0) / 50) * 50; y < MathF.Min(y1, Size); y += 50)
                    if (y % Cell >= Road + 8) Raylib.DrawRectangle((int)c - 2, (int)y, 4, 26, Pal.Line);
            }
        }

        // parky
        foreach (var p in Parks)
        {
            if (p.X > x1 || p.X + p.Width < x0 || p.Y > y1 || p.Y + p.Height < y0) continue;
            Raylib.DrawRectangleRec(p, Pal.Park);
            for (float tx = p.X + 30; tx < p.X + p.Width - 10; tx += 62)
                for (float ty = p.Y + 30; ty < p.Y + p.Height - 10; ty += 62)
                    Raylib.DrawCircle((int)tx, (int)ty, 15, Pal.Tree);
        }

        // budovy (stin + strecha)
        for (int i = 0; i < Buildings.Count; i++)
        {
            var b = Buildings[i];
            if (b.X > x1 || b.X + b.Width < x0 || b.Y > y1 || b.Y + b.Height < y0) continue;
            Raylib.DrawRectangle((int)b.X + 8, (int)b.Y + 8, (int)b.Width, (int)b.Height, Raylib.Fade(Pal.Black, 0.30f));
            Raylib.DrawRectangleRec(b, BuildingColors[i]);
            Raylib.DrawRectangle((int)b.X + 12, (int)b.Y + 12, (int)b.Width - 24, (int)b.Height - 24, Raylib.Fade(Pal.White, 0.07f));
            Raylib.DrawRectangleLinesEx(b, 3, Raylib.Fade(Pal.Black, 0.45f));
        }
    }
}
