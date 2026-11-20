using System.Numerics;
using Raylib_cs;

namespace Z04;

/// <summary>Mesto: mrizka silnic (Grid+1 x Grid+1), mezi nimi bloky s budovami nebo parkem.</summary>
public class World
{
    public const int Grid = 12;
    public const float Cell = 500f;
    public const float Road = 140f;
    public const float Size = Grid * Cell + Road;

    public readonly List<Rectangle> Buildings = new();
    public readonly List<Color> BuildingColors = new();
    public readonly List<Rectangle> Parks = new();
    // rozsvicena okna v noci: pro kazdou budovu seznam oken (lokalni pozice v ramci budovy)
    public readonly List<List<Rectangle>> Windows = new();

    // specialni budovy: lakovna, banka, nemocnice, uhyt (dvere na silnici = misto, kam se zajede/dojde)
    public Rectangle Garage, Bank, Hospital, Safehouse;
    public Vector2 GarageDoor, BankDoor, HospitalDoor, SafehouseDoor;
    public const int GarageI = 1, GarageJ = 6, BankI = 6, BankJ = 2,
                     HospI = 9, HospJ = 9, SafeI = 3, SafeJ = 10;

    // dekorace strech (nemeni kolize)
    readonly List<List<(Rectangle R, int Kind)>> _roofDetails = new();
    readonly Random _deco;

    public float Night;   // 0 = den, 1 = plna noc (nastavuje Game kazdy snimek)
    public float Wet;     // 0 = sucho, 1 = mokra vozovka (dest)

    public World(int seed)
    {
        var r = new Random(seed);
        _deco = new Random(seed + 7919); // oddeleny generator - rozlozeni mesta zustava stejne
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

        Garage = MakeSpecial(r, GarageI, GarageJ, new Color(200, 70, 150, 255), out GarageDoor);
        Bank = MakeSpecial(r, BankI, BankJ, new Color(196, 160, 60, 255), out BankDoor);
        Hospital = MakeSpecial(r, HospI, HospJ, new Color(210, 70, 70, 255), out HospitalDoor);
        Safehouse = MakeSpecial(r, SafeI, SafeJ, new Color(70, 150, 90, 255), out SafehouseDoor);
    }

    /// <summary>Nahradi obsah bloku jednou velkou budovou, dvere jsou na silnici nad ni.</summary>
    Rectangle MakeSpecial(Random r, int i, int j, Color roof, out Vector2 door)
    {
        const float pad = 22f;
        float bs = Cell - Road;
        float bx = i * Cell + Road, by = j * Cell + Road;

        for (int k = Buildings.Count - 1; k >= 0; k--)
        {
            var b = Buildings[k];
            if (b.X >= bx && b.X < bx + bs && b.Y >= by && b.Y < by + bs)
            {
                Buildings.RemoveAt(k); BuildingColors.RemoveAt(k); _roofDetails.RemoveAt(k); Windows.RemoveAt(k);
            }
        }
        Parks.RemoveAll(pk => pk.X >= bx && pk.X < bx + bs && pk.Y >= by && pk.Y < by + bs);

        var rect = new Rectangle(bx + pad, by + pad, bs - 2 * pad, bs - 2 * pad);
        AddBuilding(r, rect.X, rect.Y, rect.Width, rect.Height);
        BuildingColors[^1] = roof;
        _roofDetails[^1].Clear();
        door = new Vector2(bx + bs / 2, by - 40);
        return rect;
    }

    void AddBuilding(Random r, float x, float y, float w, float h)
    {
        Buildings.Add(new Rectangle(x, y, w, h));
        BuildingColors.Add(Pal.Roofs[r.Next(Pal.Roofs.Length)]);

        var list = new List<(Rectangle, int)>();
        int n = 2 + _deco.Next(4);
        for (int k = 0; k < n; k++)
        {
            int kind = _deco.Next(3); // 0 klimatizace, 1 svetlik, 2 komin
            float sw = kind == 1 ? 34 : kind == 2 ? 16 : 26;
            float sh = kind == 1 ? 26 : kind == 2 ? 16 : 22;
            float rx = x + 24 + (float)_deco.NextDouble() * MathF.Max(1, w - 48 - sw);
            float ry = y + 24 + (float)_deco.NextDouble() * MathF.Max(1, h - 48 - sh);
            list.Add((new Rectangle(rx, ry, sw, sh), kind));
        }
        _roofDetails.Add(list);

        // nahodna rozsvicena okna po obvodu strechy (sviti v noci)
        var win = new List<Rectangle>();
        for (float wx = x + 20; wx < x + w - 28; wx += 26)
            for (float wy = y + 20; wy < y + h - 28; wy += 30)
                if (_deco.NextDouble() < 0.35) win.Add(new Rectangle(wx, wy, 10, 12));
        Windows.Add(win);
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
        var curb = new Color(105, 105, 102, 255);
        var edge = new Color(200, 200, 205, 120);
        var zebra = new Color(235, 235, 235, 200);

        // asfalt
        var road = Pal.Road;
        if (Wet > 0.01f)
        {
            // mokry asfalt je tmavsi a modrejsi
            road = new Color((byte)(road.R + (34 - road.R) * 0.7f * Wet),
                             (byte)(road.G + (40 - road.G) * 0.7f * Wet),
                             (byte)(road.B + (54 - road.B) * 0.7f * Wet), (byte)255);
        }
        Raylib.DrawRectangle(0, 0, (int)Size, (int)Size, road);

        // chodniky (bloky) s obrubnikem
        for (int i = 0; i < Grid; i++)
        {
            for (int j = 0; j < Grid; j++)
            {
                float bx = i * Cell + Road, by = j * Cell + Road;
                if (bx > x1 || bx + bs < x0 || by > y1 || by + bs < y0) continue;
                Raylib.DrawRectangle((int)bx - 3, (int)by - 3, (int)bs + 6, (int)bs + 6, curb);
                Raylib.DrawRectangle((int)bx, (int)by, (int)bs, (int)bs, Pal.Sidewalk);
                // jemna dlazba
                for (float t = 40; t < bs; t += 40)
                {
                    Raylib.DrawLine((int)(bx + t), (int)by, (int)(bx + t), (int)(by + bs), Raylib.Fade(Pal.Black, 0.06f));
                    Raylib.DrawLine((int)bx, (int)(by + t), (int)(bx + bs), (int)(by + t), Raylib.Fade(Pal.Black, 0.06f));
                }
                // krajnice silnice
                Raylib.DrawRectangleLinesEx(new Rectangle(bx - 10, by - 10, bs + 20, bs + 20), 2, edge);
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

        // prechody pro chodce u krizovatek
        for (int i = 0; i <= Grid; i++)
        {
            for (int j = 0; j <= Grid; j++)
            {
                float ix = i * Cell, iy = j * Cell;
                if (ix > x1 + 40 || ix + Cell < x0 - 40 || iy > y1 + 40 || iy + Cell < y0 - 40) continue;
                for (int k = 0; k < 7; k++)
                {
                    float o = 12 + k * 17;
                    if (i < Grid) Raylib.DrawRectangle((int)(ix + Road + 6), (int)(iy + o), 22, 9, zebra);
                    if (i > 0) Raylib.DrawRectangle((int)(ix - 28), (int)(iy + o), 22, 9, zebra);
                    if (j < Grid) Raylib.DrawRectangle((int)(ix + o), (int)(iy + Road + 6), 9, 22, zebra);
                    if (j > 0) Raylib.DrawRectangle((int)(ix + o), (int)(iy - 28), 9, 22, zebra);
                }
            }
        }

        // parky
        foreach (var p in Parks)
        {
            if (p.X > x1 || p.X + p.Width < x0 || p.Y > y1 || p.Y + p.Height < y0) continue;
            Raylib.DrawRectangleRec(p, Pal.Park);
            Raylib.DrawRectangleLinesEx(p, 4, new Color(30, 90, 42, 255));

            // krizove cesticky
            var path = new Color(185, 170, 130, 255);
            Raylib.DrawRectangle((int)(p.X + p.Width / 2 - 9), (int)p.Y + 4, 18, (int)p.Height - 8, path);
            Raylib.DrawRectangle((int)p.X + 4, (int)(p.Y + p.Height / 2 - 9), (int)p.Width - 8, 18, path);
            Raylib.DrawCircle((int)(p.X + p.Width / 2), (int)(p.Y + p.Height / 2), 30, path);
            Raylib.DrawCircle((int)(p.X + p.Width / 2), (int)(p.Y + p.Height / 2), 18, new Color(70, 140, 190, 255));

            for (float tx = p.X + 30; tx < p.X + p.Width - 10; tx += 62)
            {
                for (float ty = p.Y + 30; ty < p.Y + p.Height - 10; ty += 62)
                {
                    float cx = p.X + p.Width / 2, cy = p.Y + p.Height / 2;
                    if (MathF.Abs(tx - cx) < 26 || MathF.Abs(ty - cy) < 26) continue; // nechat cestu volnou
                    Raylib.DrawCircle((int)tx + 5, (int)ty + 6, 15, Raylib.Fade(Pal.Black, 0.25f));
                    Raylib.DrawCircle((int)tx, (int)ty, 15, Pal.Tree);
                    Raylib.DrawCircle((int)tx - 4, (int)ty - 4, 8, new Color(40, 118, 54, 255));
                }
            }
        }

        // budovy (stin + strecha + detaily)
        for (int i = 0; i < Buildings.Count; i++)
        {
            var b = Buildings[i];
            if (b.X > x1 || b.X + b.Width < x0 || b.Y > y1 || b.Y + b.Height < y0) continue;
            Raylib.DrawRectangle((int)b.X + 10, (int)b.Y + 10, (int)b.Width, (int)b.Height, Raylib.Fade(Pal.Black, 0.32f));
            Raylib.DrawRectangleRec(b, BuildingColors[i]);
            Raylib.DrawRectangle((int)b.X + 12, (int)b.Y + 12, (int)b.Width - 24, (int)b.Height - 24, Raylib.Fade(Pal.White, 0.07f));
            Raylib.DrawRectangleLinesEx(
                new Rectangle(b.X + 12, b.Y + 12, b.Width - 24, b.Height - 24), 2, Raylib.Fade(Pal.Black, 0.20f));

            foreach (var (rr, kind) in _roofDetails[i])
            {
                Raylib.DrawRectangle((int)rr.X + 4, (int)rr.Y + 4, (int)rr.Width, (int)rr.Height, Raylib.Fade(Pal.Black, 0.25f));
                switch (kind)
                {
                    case 0: // klimatizace
                        Raylib.DrawRectangleRec(rr, new Color(170, 172, 178, 255));
                        Raylib.DrawCircle((int)(rr.X + rr.Width / 2), (int)(rr.Y + rr.Height / 2), rr.Height / 2 - 3, new Color(70, 72, 78, 255));
                        break;
                    case 1: // svetlik
                        Raylib.DrawRectangleRec(rr, new Color(120, 170, 200, 255));
                        Raylib.DrawRectangleLinesEx(rr, 2, new Color(60, 70, 80, 255));
                        break;
                    default: // komin
                        Raylib.DrawRectangleRec(rr, new Color(110, 70, 60, 255));
                        Raylib.DrawRectangle((int)rr.X + 4, (int)rr.Y + 4, (int)rr.Width - 8, (int)rr.Height - 8, Pal.Dark);
                        break;
                }
            }

            Raylib.DrawRectangleLinesEx(b, 3, Raylib.Fade(Pal.Black, 0.45f));

            // rozsvicena okna v noci
            if (Night > 0.05f)
            {
                var glow = new Color(255, 224, 140, 255);
                foreach (var wr in Windows[i])
                    Raylib.DrawRectangleRec(wr, Raylib.Fade(glow, Night * 0.9f));
            }
        }

        DrawSpecial(Garage, "LAKOVNA", new Color(255, 120, 190, 255), x0, y0, x1, y1);
        DrawSpecial(Bank, "BANKA", new Color(250, 215, 80, 255), x0, y0, x1, y1);
        DrawSpecial(Hospital, "NEMOCNICE", new Color(250, 120, 120, 255), x0, y0, x1, y1);
        DrawSpecial(Safehouse, "UKRYT", new Color(120, 220, 150, 255), x0, y0, x1, y1);
    }

    void DrawSpecial(Rectangle b, string label, Color accent, float x0, float y0, float x1, float y1)
    {
        if (b.X > x1 || b.X + b.Width < x0 || b.Y - 30 > y1 || b.Y + b.Height < y0) return;
        float cx = b.X + b.Width / 2;

        Raylib.DrawRectangle((int)cx - 36, (int)(b.Y - 22), 72, 22, new Color(70, 70, 76, 255));
        Raylib.DrawRectangle((int)cx - 30, (int)b.Y - 4, 60, 12, accent);
        Raylib.DrawRectangleLinesEx(new Rectangle(cx - 30, b.Y - 4, 60, 12), 2, Raylib.Fade(Pal.Black, 0.6f));

        for (float k = 0; k < b.Width - 40; k += 40)
            Raylib.DrawRectangle((int)(b.X + 20 + k), (int)b.Y + 14, 20, 8, Raylib.Fade(Pal.Black, 0.20f));

        int fs = 56;
        int tw = Raylib.MeasureText(label, fs);
        int ty = (int)(b.Y + b.Height / 2 - fs / 2);
        Raylib.DrawText(label, (int)(cx - tw / 2) + 3, ty + 3, fs, Raylib.Fade(Pal.Black, 0.5f));
        Raylib.DrawText(label, (int)(cx - tw / 2), ty, fs, Pal.White);
    }
}
