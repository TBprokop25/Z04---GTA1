using System.Numerics;
using Raylib_cs;

namespace Z04;

// Interiéry budov - samostatný režim, do kterého se vejde klávesou E a dá se tam něco dělat.
public partial class Game
{
    public enum Place { None, Bank, Hospital, Safehouse, GunShop, Store }

    Place place;
    Vector2 ipPos, worldReturn;     // pozice hrace v mistnosti (lokalni) a navratova pozice ve svete
    float ipWalk, ipFace = -MathF.PI / 2;
    int savings;                    // ulozene penize v bance (bezpecne pri smrti)
    Color playerCol = new(210, 150, 40, 255);

    class Station { public Vector2 Pos; public string Label = ""; public int Action; public Color Col; }
    readonly List<Station> stations = new();

    const float IW = 1040, IH = 600;           // rozmer mistnosti
    float IOX => (W - IW) / 2f;                 // posun na obrazovce
    float IOY => (H - IH) / 2f + 20;

    static string PlaceName(Place p) => p switch
    {
        Place.Bank => "BANKA",
        Place.Hospital => "NEMOCNICE",
        Place.Safehouse => "UKRYT",
        Place.GunShop => "ZBROJNICE",
        _ => "VECERKA",
    };

    void EnterPlace(Place p, Vector2 worldDoor)
    {
        place = p;
        worldReturn = pPos;
        scr = Scr.Interior;
        ipPos = new Vector2(IW / 2, IH - 70);
        ipFace = -MathF.PI / 2;
        robbing = false; robT = 0;
        shop = false;
        BuildStations(p);
        Sfx.Play(Snd.Click);
        Say("Vstoupil jsi: " + PlaceName(p) + "  (ESC = odejit)");
    }

    void ExitPlace()
    {
        scr = Scr.Play;
        pPos = worldReturn;
        world.Resolve(ref pPos, 9);
        place = Place.None;
        shop = false; robbing = false;
        Sfx.Play(Snd.Click);
    }

    void BuildStations(Place p)
    {
        stations.Clear();
        var steel = new Color(120, 124, 132, 255);
        var wood = new Color(120, 86, 54, 255);
        switch (p)
        {
            case Place.Bank:
                stations.Add(new Station { Pos = new(IW / 2, 150), Label = "SEJF - podrz [E] a vykradni", Action = -1, Col = new Color(210, 180, 70, 255) });
                stations.Add(new Station { Pos = new(IW * 0.22f, 250), Label = "PREPAZKA - [E] vlozit penize", Action = 1, Col = steel });
                stations.Add(new Station { Pos = new(IW * 0.78f, 250), Label = "BANKOMAT - [E] vybrat penize", Action = 2, Col = steel });
                break;
            case Place.Hospital:
                stations.Add(new Station { Pos = new(IW / 2, 160), Label = $"LUZKO - [E] plne vyleceni (${HospitalFee})", Action = 3, Col = new Color(230, 235, 240, 255) });
                stations.Add(new Station { Pos = new(IW * 0.76f, 260), Label = "DAROVAT KREV - [E] +$150 za -25 HP", Action = 4, Col = new Color(200, 80, 80, 255) });
                break;
            case Place.Safehouse:
                stations.Add(new Station { Pos = new(IW / 2, 160), Label = "POSTEL - [E] ulozit a smazat hvezdy", Action = 5, Col = new Color(90, 120, 170, 255) });
                stations.Add(new Station { Pos = new(IW * 0.22f, 250), Label = "TREZOR - [E] ulozit penize", Action = 1, Col = steel });
                stations.Add(new Station { Pos = new(IW * 0.78f, 250), Label = "SKRIN - [E] prevleknout", Action = 6, Col = wood });
                break;
            case Place.GunShop:
                stations.Add(new Station { Pos = new(IW / 2, 170), Label = "PULT - [E] obchod se zbranemi", Action = 7, Col = wood });
                break;
            default: // Store
                stations.Add(new Station { Pos = new(IW * 0.3f, 180), Label = "LEDNICE - [E] zdravi +40 ($60)", Action = 8, Col = new Color(90, 160, 200, 255) });
                stations.Add(new Station { Pos = new(IW * 0.7f, 180), Label = "REGAL - [E] brneni +50 ($800)", Action = 9, Col = steel });
                break;
        }
    }

    void UpdateInterior(float dt)
    {
        if (msgT > 0) msgT -= dt;

        // obchod uvnitr zbrojnice
        if (shop)
        {
            if (Raylib.IsKeyPressed(Keys.B) || Raylib.IsKeyPressed(Keys.Escape)) shop = false;
            else { UpdateShop(); return; }
        }

        // pohyb po mistnosti
        Vector2 mv = Vector2.Zero;
        if (Raylib.IsKeyDown(Keys.W) || Raylib.IsKeyDown(Keys.Up)) mv.Y -= 1;
        if (Raylib.IsKeyDown(Keys.S) || Raylib.IsKeyDown(Keys.Down)) mv.Y += 1;
        if (Raylib.IsKeyDown(Keys.A) || Raylib.IsKeyDown(Keys.Left)) mv.X -= 1;
        if (Raylib.IsKeyDown(Keys.D) || Raylib.IsKeyDown(Keys.Right)) mv.X += 1;
        if (mv != Vector2.Zero)
        {
            mv = Vector2.Normalize(mv);
            ipPos += mv * 180f * dt;
            ipWalk += 180f * dt * 0.09f;
            ipFace = MathF.Atan2(mv.Y, mv.X);
        }
        ipPos.X = Math.Clamp(ipPos.X, 36, IW - 36);
        ipPos.Y = Math.Clamp(ipPos.Y, 36, IH - 36);

        // sejf v bance - drzet E
        var vault = place == Place.Bank ? stations[0] : null;
        if (vault != null && bankCd <= 0 && Vector2.Distance(ipPos, vault.Pos) < 80 && Raylib.IsKeyDown(Keys.E))
        {
            if (!robbing) { robbing = true; robT = 0; Sfx.Play(Snd.Siren, 1f, 0.5f); }
            robT += dt;
            if (robT >= RobTime)
            {
                int loot = rnd.Next(5000, 8001);
                money += loot; totalChaos += loot;
                heat = MathF.Max(heat, 150f);     // 4 hvezdy po odchodu
                bankCd = 150f;
                robbing = false;
                Sfx.Play(Snd.Bank);
                SaveGame();
                Say($"Sejf vykraden! +${loot} - venku cekaji poldove, utikej!");
            }
            return;
        }
        if (robbing && (vault == null || Vector2.Distance(ipPos, vault.Pos) >= 80 || !Raylib.IsKeyDown(Keys.E)))
            robbing = false;

        // odchod z budovy (dvere dole uprostred nebo ESC)
        bool atDoor = ipPos.Y > IH - 60 && MathF.Abs(ipPos.X - IW / 2) < 80;
        if (Raylib.IsKeyPressed(Keys.Escape) || Raylib.IsKeyPressed(Keys.P)) { ExitPlace(); return; }
        if (atDoor && Raylib.IsKeyPressed(Keys.E)) { ExitPlace(); return; }

        // interakce se stanici
        if (Raylib.IsKeyPressed(Keys.E) && !atDoor)
        {
            Station? best = null; float bd = 80;
            foreach (var s in stations)
            {
                if (s.Action < 0) continue;
                float d = Vector2.Distance(ipPos, s.Pos);
                if (d < bd) { bd = d; best = s; }
            }
            if (best != null) DoStation(best.Action);
        }
    }

    void DoStation(int action)
    {
        switch (action)
        {
            case 1:
                if (money <= 0) { Say("Nemas co ulozit."); break; }
                savings += money; Say($"Ulozeno ${money}. Zustatek: ${savings}"); money = 0; SaveGame();
                break;
            case 2:
                if (savings <= 0) { Say("Ucet je prazdny."); break; }
                money += savings; Say($"Vybrano ${savings}."); savings = 0; SaveGame();
                break;
            case 3:
                if (pHp >= 100) Say("Jsi plne zdravy.");
                else if (money >= HospitalFee) { money -= HospitalFee; pHp = 100; Sfx.Play(Snd.Pickup); Say("Plne vyleceno."); }
                else Say($"Nemas ${HospitalFee}.");
                break;
            case 4:
                if (pHp > 30) { pHp -= 25; money += 150; Sfx.Play(Snd.Coin); Say("Darovals krev: +$150, -25 HP."); }
                else Say("Prilis slaby na darovani.");
                break;
            case 5:
                heat = 0; combo = 0; SaveGame(); Sfx.Play(Snd.Pickup);
                Say($"Ulozeno, hvezdy smazany. Rekord ${bestChaos}.");
                break;
            case 6:
                playerCol = new Color((byte)rnd.Next(60, 230), (byte)rnd.Next(60, 230), (byte)rnd.Next(60, 230), (byte)255);
                Sfx.Play(Snd.Click); Say("Prevlekl ses do noveho.");
                break;
            case 7:
                shop = true; Sfx.Play(Snd.Click);
                break;
            case 8:
                if (pHp >= 100) Say("Zdravi je plne.");
                else if (money >= 60) { money -= 60; pHp = MathF.Min(100, pHp + 40); Sfx.Play(Snd.Pickup); Say("Zdravi +40."); }
                else Say("Nemas $60.");
                break;
            case 9:
                if (pArmor >= 100) Say("Brneni je plne.");
                else if (money >= ArmorPrice) { money -= ArmorPrice; pArmor = MathF.Min(100, pArmor + 50); Sfx.Play(Snd.Pickup); Say("Brneni +50."); }
                else Say($"Nemas ${ArmorPrice}.");
                break;
        }
    }

    // ------------------------------------------------------------ kresleni interieru

    Vector2 IScreen(Vector2 local) => new(IOX + local.X, IOY + local.Y);

    void DrawInterior()
    {
        Raylib.ClearBackground(new Color(18, 18, 24, 255));

        float ox = IOX, oy = IOY;
        Color floor = place switch
        {
            Place.Bank => new Color(60, 58, 50, 255),
            Place.Hospital => new Color(220, 228, 232, 255),
            Place.Safehouse => new Color(78, 66, 58, 255),
            Place.GunShop => new Color(54, 48, 44, 255),
            _ => new Color(70, 78, 82, 255),
        };

        // podlaha + dlazdice
        Raylib.DrawRectangle((int)ox, (int)oy, (int)IW, (int)IH, floor);
        for (float x = 0; x < IW; x += 64)
            Raylib.DrawLine((int)(ox + x), (int)oy, (int)(ox + x), (int)(oy + IH), Raylib.Fade(Pal.Black, 0.10f));
        for (float y = 0; y < IH; y += 64)
            Raylib.DrawLine((int)ox, (int)(oy + y), (int)(ox + IW), (int)(oy + y), Raylib.Fade(Pal.Black, 0.10f));

        // steny
        var wall = new Color(40, 40, 48, 255);
        Raylib.DrawRectangle((int)ox - 16, (int)oy - 16, (int)IW + 32, 16, wall);
        Raylib.DrawRectangle((int)ox - 16, (int)(oy + IH), (int)IW + 32, 16, wall);
        Raylib.DrawRectangle((int)ox - 16, (int)oy - 16, 16, (int)IH + 32, wall);
        Raylib.DrawRectangle((int)(ox + IW), (int)oy - 16, 16, (int)IH + 32, wall);
        Raylib.DrawRectangleLinesEx(new Rectangle(ox, oy, IW, IH), 2, Raylib.Fade(Pal.Black, 0.3f));

        // dvere dole uprostred
        var door = IScreen(new Vector2(IW / 2, IH));
        Raylib.DrawRectangle((int)door.X - 60, (int)door.Y - 6, 120, 20, new Color(150, 110, 60, 255));
        Txt("VYCHOD [E]", (int)door.X - 52, (int)door.Y - 4, 16, Pal.White);

        // stanice (nabytek) + popisek a zvyrazneni kdyz jsi blizko
        foreach (var s in stations)
        {
            var sp = IScreen(s.Pos);
            bool near = Vector2.Distance(ipPos, s.Pos) < 80;
            Raylib.DrawRectangle((int)sp.X - 44, (int)sp.Y - 28 + 6, 88, 56, Raylib.Fade(Pal.Black, 0.3f));
            Raylib.DrawRectangleRounded(new Rectangle(sp.X - 44, sp.Y - 28, 88, 56), 0.2f, 6, s.Col);
            Raylib.DrawRectangleLinesEx(new Rectangle(sp.X - 44, sp.Y - 28, 88, 56), 2, Raylib.Fade(Pal.Black, 0.5f));
            DrawStationIcon(s, sp);

            if (near)
            {
                int tw = Raylib.MeasureText(s.Label, 18);
                Raylib.DrawRectangle((int)sp.X - tw / 2 - 8, (int)sp.Y - 62, tw + 16, 26, Raylib.Fade(Pal.Black, 0.7f));
                Txt(s.Label, (int)sp.X - tw / 2, (int)sp.Y - 58, 18, Pal.Yellow);
            }
        }

        DrawInteriorPlayer();

        // vykradani sejfu - progress
        if (robbing)
        {
            Raylib.DrawRectangleRounded(new Rectangle(W / 2 - 180, 70, 360, 50), 0.3f, 8, Raylib.Fade(Pal.Black, 0.6f));
            Txt("VYKRADAM SEJF...", W / 2 - 100, 74, 22, Pal.Yellow);
            Bar(W / 2 - 160, 100, 320, 14, robT / RobTime, Pal.Yellow);
        }

        // horni pruh s nazvem a penezi
        Raylib.DrawRectangle(0, 0, W, 40, Raylib.Fade(Pal.Black, 0.55f));
        Txt(PlaceName(place), 20, 8, 26, Pal.Yellow);
        Txt($"Hotovost: ${money}    Na ucte: ${savings}    HP {(int)pHp}  AP {(int)pArmor}", 300, 11, 20, Pal.White);
        Txt("WASD = chuze    [E] = pouzit / vychod dole    ESC = odejit", 20, H - 28, 16, new Color(200, 200, 205, 255));

        if (msgT > 0)
        {
            int tw = Raylib.MeasureText(msg, 24);
            float a = Math.Clamp(msgT / 0.5f, 0f, 1f);
            Raylib.DrawRectangleRounded(new Rectangle(W / 2 - tw / 2 - 16, H - 92, tw + 32, 40), 0.3f, 8, Raylib.Fade(Pal.Black, 0.6f * a));
            Raylib.DrawText(msg, W / 2 - tw / 2, H - 84, 24, Raylib.Fade(Pal.White, a));
        }

        if (shop) DrawShop();
    }

    void DrawStationIcon(Station s, Vector2 sp)
    {
        switch (s.Action)
        {
            case -1: // sejf
                Raylib.DrawCircle((int)sp.X, (int)sp.Y, 14, new Color(40, 40, 44, 255));
                Raylib.DrawCircleLines((int)sp.X, (int)sp.Y, 14, new Color(230, 210, 120, 255));
                Raylib.DrawCircle((int)sp.X + 6, (int)sp.Y, 3, new Color(230, 210, 120, 255));
                break;
            case 3: // luzko
                Raylib.DrawRectangle((int)sp.X - 34, (int)sp.Y - 10, 24, 20, Pal.White);
                Raylib.DrawRectangle((int)sp.X - 36, (int)sp.Y - 14, 72, 8, new Color(200, 70, 70, 255));
                break;
            case 7: // zbrane na stene
                for (int i = -1; i <= 1; i++)
                    Raylib.DrawRectangle((int)sp.X - 24 + (i + 1) * 20, (int)sp.Y - 4, 16, 4, new Color(30, 30, 34, 255));
                break;
            default:
                Raylib.DrawText("$", (int)sp.X - 6, (int)sp.Y - 10, 24, Raylib.Fade(Pal.Black, 0.6f));
                break;
        }
    }

    void DrawInteriorPlayer()
    {
        var p = IScreen(ipPos);
        var fwd = DirOf(ipFace);
        var side = new Vector2(-fwd.Y, fwd.X);
        float swing = MathF.Sin(ipWalk) * 3.6f;
        var skin = new Color(238, 198, 158, 255);

        Raylib.DrawCircleV(p + new Vector2(2, 4), 10f, Raylib.Fade(Pal.Black, 0.3f));
        Raylib.DrawCircleV(p + side * 3.4f + fwd * swing, 3.2f, new Color(45, 45, 60, 255));
        Raylib.DrawCircleV(p - side * 3.4f - fwd * swing, 3.2f, new Color(45, 45, 60, 255));
        Raylib.DrawCircleV(p, 8.6f, playerCol);
        Raylib.DrawCircleLines((int)p.X, (int)p.Y, 8.6f, Raylib.Fade(Pal.Black, 0.6f));
        Raylib.DrawCircleV(p + side * 7 - fwd * swing * 0.6f, 2.6f, skin);
        Raylib.DrawCircleV(p - side * 7 + fwd * swing * 0.6f, 2.6f, skin);
        Raylib.DrawCircleV(p + fwd * 1.5f, 4.9f, skin);
    }
}
