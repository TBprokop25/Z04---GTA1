using System.Numerics;
using System.Text.Json;
using Raylib_cs;

namespace Z04;

/// <summary>
/// Mesto nactene z rucne navrzene mapy (assets/07_svet/mapa/chaoscity_map.json).
/// Silnice, bloky budov a graf krizovatek se generuji ze souradnic dlazdic,
/// takze svet presne odpovida vyrenderovanemu PNG (chaoscity_map_4096.png).
/// </summary>
public class World
{
    public const float Tile = 140f;         // hernich jednotek na dlazdici (mapa 64x48 dlazdic)
    public float WPx = 64 * Tile, HPx = 48 * Tile;
    public float Size => MathF.Max(WPx, HPx);   // kompat. pro ctvercove meze
    public const string MapTex = "07_svet/mapa/chaoscity_map_4096.png";

    public readonly List<Rectangle> Roads = new();        // pro OnRoad a spawn
    public readonly List<Rectangle> Buildings = new();    // kolize
    public readonly List<Color> BuildingColors = new();
    public readonly List<Rectangle> Parks = new();

    // graf silnic (uzly = krizovatky/konce, hrany podel silnic) - pro AI a questy
    public readonly List<Vector2> Nodes = new();
    public readonly List<int[]> Adj = new();

    // specialni budovy
    public Rectangle Garage, Bank, Hospital, Safehouse, GunShop, Store, Gas, Villa;
    public Vector2 GarageDoor, BankDoor, HospitalDoor, SafehouseDoor, GunShopDoor, StoreDoor, GasDoor, VillaDoor;
    public Vector2 Spawn;

    public float Night, Wet;

    // pomocne segmenty silnic (v dlazdicich) pro generovani
    struct Seg { public float X0, Y0, X1, Y1, W; public bool Vert; }
    readonly List<Seg> _segs = new();
    readonly List<Seg> _hwy = new();    // dalnice (nadjezdy) - kresli se nad auty

    public World(int seed)
    {
        var r = new Random(seed);
        try { Build(r); }
        catch { BuildFallback(r); }
        if (Nodes.Count == 0) BuildFallback(r);
        Spawn = Nodes[NearestNode(new Vector2(WPx * 0.47f, HPx * 0.55f))];
    }

    // ------------------------------------------------------------ nacteni + generovani

    static float F(float t) => t * Tile;

    void Build(Random r)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "07_svet", "mapa", "chaoscity_map.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        // silnice
        foreach (var rd in root.GetProperty("roads").EnumerateArray())
        {
            var f = rd.GetProperty("from"); var t = rd.GetProperty("to");
            float x0 = f[0].GetSingle(), y0 = f[1].GetSingle(), x1 = t[0].GetSingle(), y1 = t[1].GetSingle();
            float w = rd.GetProperty("widthTiles").GetSingle();
            string type = rd.TryGetProperty("type", out var tp) ? (tp.GetString() ?? "") : "";
            bool vert = MathF.Abs(x0 - x1) < MathF.Abs(y0 - y1);
            var seg = new Seg { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, W = w, Vert = vert };
            _segs.Add(seg);
            if (type == "hwy") _hwy.Add(seg);

            float hw = w / 2f;
            Rectangle rect = vert
                ? new Rectangle(F(x0 - hw), F(MathF.Min(y0, y1)), F(w), F(MathF.Abs(y1 - y0)))
                : new Rectangle(F(MathF.Min(x0, x1)), F(y0 - hw), F(MathF.Abs(x1 - x0)), F(w));
            Roads.Add(rect);
        }

        BuildGraph();

        // POI pozice (dlazdice)
        var pois = new Dictionary<string, Vector2>();
        Vector2? firstGas = null;
        foreach (var p in root.GetProperty("pois").EnumerateArray())
        {
            string id = p.GetProperty("id").GetString() ?? "";
            var pos = p.GetProperty("pos");
            var v = new Vector2(F(pos[0].GetSingle()), F(pos[1].GetSingle()));
            if (id == "gas") { firstGas ??= v; }
            else pois[id] = v;
            string? name = p.GetProperty("name").GetString();
            if (name == "Centrální park" || name == "Park")
                Parks.Add(new Rectangle(v.X - F(1.6f), v.Y - F(1.6f), F(3.2f), F(3.2f)));
        }

        // kolizni bloky mezi silnicemi (po ctvrtich)
        BuildBlocks(root, pois, firstGas);

        // specialni budovy na POI
        Bank = MakeSpecial(pois.GetValueOrDefault("bank", new Vector2(F(34), F(18))), new Color(196, 160, 60, 255), out BankDoor);
        Hospital = MakeSpecial(pois.GetValueOrDefault("hosp", new Vector2(F(30), F(30))), new Color(210, 70, 70, 255), out HospitalDoor);
        GunShop = MakeSpecial(pois.GetValueOrDefault("gun", new Vector2(F(13.3f), F(41.4f))), new Color(150, 90, 50, 255), out GunShopDoor);
        Store = MakeSpecial(pois.GetValueOrDefault("store", new Vector2(F(14.8f), F(41.4f))), new Color(60, 150, 160, 255), out StoreDoor);
        Safehouse = MakeSpecial(pois.GetValueOrDefault("safe", new Vector2(F(21), F(41))), new Color(70, 150, 90, 255), out SafehouseDoor);
        Villa = MakeSpecial(pois.GetValueOrDefault("villa", new Vector2(F(28.6f), F(6.4f))), new Color(150, 110, 200, 255), out VillaDoor);
        Gas = MakeSpecial(firstGas ?? new Vector2(F(51.5f), F(14.4f)), new Color(200, 60, 60, 255), out GasDoor);
        Garage = MakeSpecial(pois.GetValueOrDefault("police", new Vector2(F(41.9f), F(30))) + new Vector2(F(2.4f), 0), new Color(200, 70, 150, 255), out GarageDoor);
    }

    // graf: uzly v koncich a prusecich segmentu, hrany podel segmentu
    void BuildGraph()
    {
        var map = new Dictionary<(int, int), int>();
        int NodeAt(float px, float py)
        {
            var key = ((int)MathF.Round(px / 6f), (int)MathF.Round(py / 6f));
            if (map.TryGetValue(key, out int idx)) return idx;
            idx = Nodes.Count; Nodes.Add(new Vector2(px, py)); map[key] = idx; return idx;
        }

        // pro kazdy segment seznam bodu (v dlazdicich) lezicich na nem
        var onSeg = new List<List<float>>();
        for (int i = 0; i < _segs.Count; i++)
        {
            var s = _segs[i];
            var pts = new List<float> { s.Vert ? MathF.Min(s.Y0, s.Y1) : MathF.Min(s.X0, s.X1),
                                        s.Vert ? MathF.Max(s.Y0, s.Y1) : MathF.Max(s.X0, s.X1) };
            onSeg.Add(pts);
        }

        // pruseciky kolmych segmentu
        for (int i = 0; i < _segs.Count; i++)
            for (int j = i + 1; j < _segs.Count; j++)
            {
                var a = _segs[i]; var b = _segs[j];
                if (a.Vert == b.Vert) continue;
                var v = a.Vert ? a : b; var h = a.Vert ? b : a;
                float vx = v.X0, hy = h.Y0;
                if (vx >= MathF.Min(h.X0, h.X1) - 0.05f && vx <= MathF.Max(h.X0, h.X1) + 0.05f &&
                    hy >= MathF.Min(v.Y0, v.Y1) - 0.05f && hy <= MathF.Max(v.Y0, v.Y1) + 0.05f)
                {
                    onSeg[a.Vert ? i : j].Add(hy);  // vertical seg: pridej y
                    onSeg[a.Vert ? j : i].Add(vx);  // horizontal seg: pridej x
                }
            }

        // uzly + hrany podel kazdeho segmentu
        var adjSet = new Dictionary<int, HashSet<int>>();
        void Link(int n1, int n2)
        {
            if (n1 == n2) return;
            if (!adjSet.TryGetValue(n1, out var s1)) adjSet[n1] = s1 = new();
            if (!adjSet.TryGetValue(n2, out var s2)) adjSet[n2] = s2 = new();
            s1.Add(n2); s2.Add(n1);
        }
        for (int i = 0; i < _segs.Count; i++)
        {
            var s = _segs[i];
            var pts = onSeg[i];
            pts.Sort();
            int prev = -1;
            float lastVal = float.NaN;
            foreach (var val in pts)
            {
                if (!float.IsNaN(lastVal) && MathF.Abs(val - lastVal) < 0.03f) continue;
                lastVal = val;
                int id = s.Vert ? NodeAt(F(s.X0), F(val)) : NodeAt(F(val), F(s.Y0));
                if (prev >= 0) Link(prev, id);
                prev = id;
            }
        }

        for (int i = 0; i < Nodes.Count; i++)
            Adj.Add(adjSet.TryGetValue(i, out var set) ? set.ToArray() : Array.Empty<int>());
    }

    void BuildBlocks(JsonElement root, Dictionary<string, Vector2> pois, Vector2? firstGas)
    {
        const float margin = 0.26f, minCell = 0.8f, maxCell = 5.6f;
        var specials = new List<Vector2>(pois.Values);
        if (firstGas is Vector2 g) specials.Add(g);
        var parksT = new List<Vector2>();
        foreach (var pk in Parks) parksT.Add(new Vector2((pk.X + pk.Width / 2) / Tile, (pk.Y + pk.Height / 2) / Tile));

        foreach (var d in root.GetProperty("districts").EnumerateArray())
        {
            string name = d.GetProperty("name").GetString() ?? "";
            if (name.Contains("Pláž")) continue;    // more/plaz - bez budov
            var rc = d.GetProperty("rect");
            float dx = rc[0].GetSingle(), dy = rc[1].GetSingle(), dw = rc[2].GetSingle(), dh = rc[3].GetSingle();

            var xs = new SortedSet<float> { dx, dx + dw };
            var ys = new SortedSet<float> { dy, dy + dh };
            foreach (var s in _segs)
            {
                if (s.Vert)
                {
                    if (s.X0 >= dx - 0.1f && s.X0 <= dx + dw + 0.1f &&
                        !(MathF.Max(s.Y0, s.Y1) < dy || MathF.Min(s.Y0, s.Y1) > dy + dh))
                    { xs.Add(s.X0 - s.W / 2); xs.Add(s.X0 + s.W / 2); }
                }
                else
                {
                    if (s.Y0 >= dy - 0.1f && s.Y0 <= dy + dh + 0.1f &&
                        !(MathF.Max(s.X0, s.X1) < dx || MathF.Min(s.X0, s.X1) > dx + dw))
                    { ys.Add(s.Y0 - s.W / 2); ys.Add(s.Y0 + s.W / 2); }
                }
            }
            var xa = xs.ToList(); var ya = ys.ToList();
            for (int i = 0; i < xa.Count - 1; i++)
                for (int j = 0; j < ya.Count - 1; j++)
                {
                    float bx0 = xa[i] + margin, bx1 = xa[i + 1] - margin;
                    float by0 = ya[j] + margin, by1 = ya[j + 1] - margin;
                    float cw = bx1 - bx0, ch = by1 - by0;
                    if (cw < minCell || ch < minCell) continue;
                    if (cw > maxCell || ch > maxCell) continue;   // velke plochy nechat prujezdne
                    float cx = (bx0 + bx1) / 2, cy = (by0 + by1) / 2;
                    bool nearSpecial = specials.Any(s => MathF.Abs(cx - s.X / Tile) < 1.6f && MathF.Abs(cy - s.Y / Tile) < 1.6f);
                    bool nearPark = parksT.Any(p => MathF.Abs(cx - p.X) < 2.2f && MathF.Abs(cy - p.Y) < 2.2f);
                    if (nearSpecial || nearPark) continue;
                    Buildings.Add(new Rectangle(F(bx0), F(by0), F(cw), F(ch)));
                    BuildingColors.Add(Pal.Roofs[(i * 7 + j * 3) % Pal.Roofs.Length]);
                }
        }
    }

    Rectangle MakeSpecial(Vector2 center, Color roof, out Vector2 door)
    {
        float w = F(2.0f), h = F(1.8f);
        var rect = new Rectangle(center.X - w / 2, center.Y - h / 2, w, h);
        // odstran kolizni bloky, ktere by se prekryvaly
        for (int k = Buildings.Count - 1; k >= 0; k--)
            if (Overlap(Buildings[k], rect, 6f)) { Buildings.RemoveAt(k); BuildingColors.RemoveAt(k); }
        Buildings.Add(rect);
        BuildingColors.Add(roof);
        // vchod na strane privracene k nejblizsi silnici (ne nahodne ze strany)
        var dir = NearestRoadDir(center);
        door = center + dir * (dir.X != 0 ? w / 2 + F(0.55f) : h / 2 + F(0.55f));
        return rect;
    }

    // Smer (jeden ze 4) od bodu k nejblizsi silnici - pro umisteni vchodu.
    Vector2 NearestRoadDir(Vector2 p)
    {
        Vector2 best = new(0, 1); float bd = float.MaxValue;
        foreach (var rd in Roads)
        {
            float cx = Math.Clamp(p.X, rd.X, rd.X + rd.Width);
            float cy = Math.Clamp(p.Y, rd.Y, rd.Y + rd.Height);
            var d = new Vector2(cx - p.X, cy - p.Y);
            float len = d.LengthSquared();
            if (len < bd && len > 0.01f)
            {
                bd = len;
                best = MathF.Abs(d.X) > MathF.Abs(d.Y)
                    ? new Vector2(MathF.Sign(d.X), 0)
                    : new Vector2(0, MathF.Sign(d.Y));
            }
        }
        return best;
    }

    static bool Overlap(Rectangle a, Rectangle b, float pad) =>
        a.X - pad < b.X + b.Width && a.X + a.Width + pad > b.X &&
        a.Y - pad < b.Y + b.Height && a.Y + a.Height + pad > b.Y;

    void BuildFallback(Random r)
    {
        // nouzova sit 8x6 pokud JSON chybi
        Nodes.Clear(); Adj.Clear(); Roads.Clear(); Buildings.Clear(); BuildingColors.Clear();
        var map = new Dictionary<(int, int), int>();
        for (int i = 0; i <= 8; i++) for (int j = 0; j <= 6; j++)
        { map[(i, j)] = Nodes.Count; Nodes.Add(new Vector2(F(4 + i * 7), F(4 + j * 7))); }
        for (int i = 0; i <= 8; i++) for (int j = 0; j <= 6; j++)
        {
            var nb = new List<int>();
            if (i > 0) nb.Add(map[(i - 1, j)]); if (i < 8) nb.Add(map[(i + 1, j)]);
            if (j > 0) nb.Add(map[(i, j - 1)]); if (j < 6) nb.Add(map[(i, j + 1)]);
            Adj.Add(nb.ToArray());
        }
        Bank = MakeSpecial(new Vector2(F(18), F(18)), new Color(196, 160, 60, 255), out BankDoor);
        Hospital = MakeSpecial(new Vector2(F(32), F(25)), new Color(210, 70, 70, 255), out HospitalDoor);
        GunShop = MakeSpecial(new Vector2(F(25), F(32)), new Color(150, 90, 50, 255), out GunShopDoor);
        Store = MakeSpecial(new Vector2(F(39), F(18)), new Color(60, 150, 160, 255), out StoreDoor);
        Safehouse = MakeSpecial(new Vector2(F(11), F(32)), new Color(70, 150, 90, 255), out SafehouseDoor);
        Villa = MakeSpecial(new Vector2(F(46), F(11)), new Color(150, 110, 200, 255), out VillaDoor);
        Gas = MakeSpecial(new Vector2(F(46), F(25)), new Color(200, 60, 60, 255), out GasDoor);
        Garage = MakeSpecial(new Vector2(F(18), F(39)), new Color(200, 70, 150, 255), out GarageDoor);
    }

    // ------------------------------------------------------------ graf helpery

    public int NearestNode(Vector2 p)
    {
        int best = 0; float bd = float.MaxValue;
        for (int i = 0; i < Nodes.Count; i++)
        {
            float d = Vector2.DistanceSquared(Nodes[i], p);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    public Vector2 RandomRoadPoint(Random r) => Nodes[r.Next(Nodes.Count)];

    // ------------------------------------------------------------ dotazy

    public bool OnRoad(Vector2 p)
    {
        if (p.X < 0 || p.Y < 0 || p.X > WPx || p.Y > HPx) return false;
        foreach (var rd in Roads)
            if (p.X >= rd.X && p.X <= rd.X + rd.Width && p.Y >= rd.Y && p.Y <= rd.Y + rd.Height) return true;
        return false;
    }

    public bool PointInBuilding(Vector2 p)
    {
        foreach (var b in Buildings)
            if (p.X >= b.X && p.X <= b.X + b.Width && p.Y >= b.Y && p.Y <= b.Y + b.Height) return true;
        return false;
    }

    /// <summary>Vytlaci kruh (stred c, polomer r) z budov a z hranic sveta.</summary>
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
                float l = c.X - b.X, rt = b.X + b.Width - c.X;
                float t = c.Y - b.Y, bt = b.Y + b.Height - c.Y;
                float m = MathF.Min(MathF.Min(l, rt), MathF.Min(t, bt));
                if (m == l) push = new Vector2(-(l + r), 0);
                else if (m == rt) push = new Vector2(rt + r, 0);
                else if (m == t) push = new Vector2(0, -(t + r));
                else push = new Vector2(0, bt + r);
            }
            c += push; total += push;
        }

        float minX = r, maxX = WPx - r, minY = r, maxY = HPx - r;
        if (c.X < minX) { total.X += minX - c.X; c.X = minX; }
        if (c.X > maxX) { total.X += maxX - c.X; c.X = maxX; }
        if (c.Y < minY) { total.Y += minY - c.Y; c.Y = minY; }
        if (c.Y > maxY) { total.Y += maxY - c.Y; c.Y = maxY; }
        return total;
    }

    // ------------------------------------------------------------ kresleni

    public void Draw(float x0, float y0, float x1, float y1)
    {
        var mt = Assets.Get(MapTex);
        if (mt.Id != 0)
        {
            // PNG -> svet: png px = dlazdice * (sirka_png/64)
            float pxPerTile = mt.Width / 64f;
            float k = pxPerTile / Tile;                  // png px na jednu hernu jednotku
            float sx0 = Math.Clamp(x0 * k, 0, mt.Width);
            float sy0 = Math.Clamp(y0 * k, 0, mt.Height);
            float sx1 = Math.Clamp(x1 * k, 0, mt.Width);
            float sy1 = Math.Clamp(y1 * k, 0, mt.Height);
            var src = new Rectangle(sx0, sy0, sx1 - sx0, sy1 - sy0);
            var dst = new Rectangle(sx0 / k, sy0 / k, (sx1 - sx0) / k, (sy1 - sy0) / k);
            Raylib.DrawTexturePro(mt, src, dst, Vector2.Zero, 0, Pal.White);
        }
        else
        {
            Raylib.DrawRectangle((int)x0, (int)y0, (int)(x1 - x0), (int)(y1 - y0), new Color(40, 46, 40, 255));
            foreach (var rd in Roads)
            {
                if (rd.X > x1 || rd.X + rd.Width < x0 || rd.Y > y1 || rd.Y + rd.Height < y0) continue;
                Raylib.DrawRectangleRec(rd, Pal.Road);
            }
            foreach (var b in Buildings)
            {
                if (b.X > x1 || b.X + b.Width < x0 || b.Y > y1 || b.Y + b.Height < y0) continue;
                Raylib.DrawRectangleRec(b, new Color(96, 96, 102, 255));
            }
        }

        // cedule specialnich budov (PNG je neoznacuje)
        DrawSpecial(Bank, "BANKA", new Color(250, 215, 80, 255), x0, y0, x1, y1);
        DrawSpecial(Hospital, "NEMOCNICE", new Color(250, 120, 120, 255), x0, y0, x1, y1);
        DrawSpecial(Safehouse, "UKRYT", new Color(120, 220, 150, 255), x0, y0, x1, y1);
        DrawSpecial(GunShop, "ZBROJNICE", new Color(255, 170, 80, 255), x0, y0, x1, y1);
        DrawSpecial(Store, "VECERKA", new Color(120, 220, 230, 255), x0, y0, x1, y1);
        DrawSpecial(Gas, "BENZINKA", new Color(255, 110, 110, 255), x0, y0, x1, y1);
        DrawSpecial(Villa, "VILA", new Color(200, 160, 255, 255), x0, y0, x1, y1);
        DrawSpecial(Garage, "LAKOVNA", new Color(255, 120, 190, 255), x0, y0, x1, y1);
    }

    /// <summary>Deska dalnic (nadjezd) - kresli se az NAD auty, takze projizdejici auta jedou pod ni.</summary>
    public void DrawOverpass(float x0, float y0, float x1, float y1)
    {
        var rail = new Color(150, 150, 158, 255);
        var deckCol = new Color(74, 74, 82, 255);
        foreach (var s in _hwy)
        {
            float hw = s.W / 2f;
            Rectangle deck = s.Vert
                ? new Rectangle(F(s.X0 - hw), F(MathF.Min(s.Y0, s.Y1)), F(s.W), F(MathF.Abs(s.Y1 - s.Y0)))
                : new Rectangle(F(MathF.Min(s.X0, s.X1)), F(s.Y0 - hw), F(MathF.Abs(s.X1 - s.X0)), F(s.W));
            if (deck.X > x1 || deck.X + deck.Width < x0 || deck.Y > y1 || deck.Y + deck.Height < y0) continue;

            // stin mostu dopadajici na auta pod nim
            Raylib.DrawRectangle((int)deck.X + 10, (int)deck.Y + 12, (int)deck.Width, (int)deck.Height, Raylib.Fade(Pal.Black, 0.33f));
            Raylib.DrawRectangleRec(deck, deckCol);
            if (s.Vert)
            {
                float cx = deck.X + deck.Width / 2;
                for (float y = deck.Y; y < deck.Y + deck.Height; y += 48)
                    Raylib.DrawRectangle((int)cx - 3, (int)y + 10, 6, 26, Pal.Line);
                Raylib.DrawRectangle((int)deck.X, (int)deck.Y, 9, (int)deck.Height, rail);
                Raylib.DrawRectangle((int)(deck.X + deck.Width) - 9, (int)deck.Y, 9, (int)deck.Height, rail);
            }
            else
            {
                float cy = deck.Y + deck.Height / 2;
                for (float x = deck.X; x < deck.X + deck.Width; x += 48)
                    Raylib.DrawRectangle((int)x + 10, (int)cy - 3, 26, 6, Pal.Line);
                Raylib.DrawRectangle((int)deck.X, (int)deck.Y, (int)deck.Width, 9, rail);
                Raylib.DrawRectangle((int)deck.X, (int)(deck.Y + deck.Height) - 9, (int)deck.Width, 9, rail);
            }
        }
    }

    void DrawSpecial(Rectangle b, string label, Color accent, float x0, float y0, float x1, float y1)
    {
        if (b.X > x1 || b.X + b.Width < x0 || b.Y - 40 > y1 || b.Y + b.Height < y0) return;
        float cx = b.X + b.Width / 2;
        // zvyraznit budovu barevnym ramem (grafika mapy prosvita)
        Raylib.DrawRectangleRec(b, Raylib.Fade(accent, 0.28f));
        Raylib.DrawRectangleLinesEx(b, 3, accent);

        // poutac s nazvem nad budovou
        int fs = 20;
        int tw = Raylib.MeasureText(label, fs);
        int bx = (int)(cx - tw / 2) - 8, by = (int)(b.Y - 30);
        Raylib.DrawRectangle(bx, by, tw + 16, 26, Raylib.Fade(Pal.Black, 0.72f));
        Raylib.DrawRectangle(bx, by, tw + 16, 4, accent);
        Raylib.DrawText(label, (int)(cx - tw / 2), by + 5, fs, Pal.White);
    }

    public static Vector2 Node(int i, int j) => new(i * Tile * 4, j * Tile * 4); // nepouzivane, kompat.
}
