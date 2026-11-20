using System.Numerics;
using Raylib_cs;

namespace Z04;

// Interiéry budov - samostatný režim, do kterého se vejde klávesou E a dá se tam něco dělat.
// Každá místnost je plně vyrenderovaný PNG podklad (assets/07_svet/interiery/room_*.png);
// interaktivní stanice jsou neviditelné body umístěné nad namalovaným nábytkem.
public partial class Game
{
    public enum Place { None, Bank, Hospital, Safehouse, GunShop, Store, Gas, Villa }

    Place place;
    Vector2 ipPos, worldReturn;     // pozice hrace v mistnosti (lokalni, v nativnich px PNG) a navratova pozice ve svete
    float ipWalk, ipFace = -MathF.PI / 2;
    int savings;                    // ulozene penize v bance (bezpecne pri smrti)
    float villaCd, gasCd;           // cooldowny loupeze (bankCd je v Game.cs)
    Color playerCol = new(210, 150, 40, 255);

    // Stanice: Action >= 0 = jednorazova akce (DoStation), Action < 0 = loupez (drzet E).
    class Station
    {
        public Vector2 Pos; public string Label = ""; public int Action;
        public int LootMin, LootMax; public float RobHeat, RobTime, RobCdSet;
    }
    readonly List<Station> stations = new();

    // Rozmer aktualni mistnosti v nativnich px PNG a pozice vychodu.
    float RNW = 1024, RNH = 768;
    Vector2 exitPos;
    string roomTex = "";

    const float WallM = 26f;        // okraj mistnosti (stena), kam hrac nedojde

    static string PlaceName(Place p) => p switch
    {
        Place.Bank => "BANKA",
        Place.Hospital => "NEMOCNICE",
        Place.Safehouse => "UKRYT",
        Place.GunShop => "ZBROJNICE",
        Place.Store => "VECERKA",
        Place.Gas => "BENZINKA",
        Place.Villa => "VILA",
        _ => "",
    };

    void EnterPlace(Place p, Vector2 worldDoor)
    {
        place = p;
        worldReturn = pPos;
        scr = Scr.Interior;
        robbing = false; robT = 0;
        shop = false;
        BuildStations(p);
        // start u vychodu, obraceny do mistnosti
        ipPos = new Vector2(exitPos.X, exitPos.Y - 70);
        ipPos.X = Math.Clamp(ipPos.X, WallM, RNW - WallM);
        ipPos.Y = Math.Clamp(ipPos.Y, WallM, RNH - WallM);
        ipFace = -MathF.PI / 2;
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

    // Loupezny cooldown podle typu stanice (persistuje i mimo interier - tika v UpdateBank).
    float GetRobCd(int action) => action == -1 ? bankCd : action == -2 ? villaCd : gasCd;
    void SetRobCd(int action, float v) { if (action == -1) bankCd = v; else if (action == -2) villaCd = v; else gasCd = v; }

    void BuildStations(Place p)
    {
        stations.Clear();
        void Act(float x, float y, string label, int action) => stations.Add(new Station { Pos = new(x, y), Label = label, Action = action });
        void Rob(float x, float y, string label, int action, int lo, int hi, float heat, float time, float cd) =>
            stations.Add(new Station { Pos = new(x, y), Label = label, Action = action, LootMin = lo, LootMax = hi, RobHeat = heat, RobTime = time, RobCdSet = cd });

        switch (p)
        {
            case Place.Bank:
                RNW = 1024; RNH = 768; roomTex = "room_bank"; exitPos = new(512, 724);
                Rob(866, 150, "SEJF - podrz [E] a vykradni", -1, 5000, 8000, 150f, 12f, 150f);
                Act(400, 300, "PREPAZKA - [E] vlozit penize", 1);
                Act(220, 705, "BANKOMAT - [E] vybrat penize", 2);
                break;
            case Place.Hospital:
                RNW = 1024; RNH = 768; roomTex = "room_hospital"; exitPos = new(160, 735);
                Act(360, 512, $"ORDINACE - [E] plne vyleceni (${HospitalFee})", 3);
                Act(420, 640, "DAROVAT KREV - [E] +$150 za -25 HP", 4);
                break;
            case Place.Safehouse:
                RNW = 896; RNH = 640; roomTex = "room_safehouse"; exitPos = new(112, 610);
                Act(705, 150, "POSTEL - [E] ulozit a smazat hvezdy", 5);
                Act(145, 430, "TREZOR - [E] ulozit penize", 1);
                Act(852, 90, "SKRIN - [E] prevleknout", 6);
                break;
            case Place.GunShop:
                RNW = 768; RNH = 512; roomTex = "room_gunshop"; exitPos = new(145, 480);
                Act(185, 205, "PULT - [E] obchod se zbranemi", 7);
                break;
            case Place.Store:
                RNW = 768; RNH = 512; roomTex = "room_store"; exitPos = new(648, 480);
                Act(40, 220, "LEDNICE - [E] zdravi +40 ($60)", 8);
                Act(180, 160, "REGAL - [E] brneni +50 ($800)", 9);
                break;
            case Place.Gas:
                RNW = 768; RNH = 512; roomTex = "room_gas"; exitPos = new(128, 480);
                Act(40, 300, "LEDNICE - [E] napoj, zdravi +30 ($40)", 10);
                Act(500, 445, "STANEK - [E] hotdog, zdravi +55 ($70)", 11);
                Rob(300, 350, "KASA - podrz [E] a vyber", -3, 400, 1100, 70f, 4f, 60f);
                break;
            default: // Villa
                RNW = 1280; RNH = 896; roomTex = "room_villa"; exitPos = new(350, 850);
                Rob(750, 80, "PRACOVNA - podrz [E] a vyloupni vilu", -2, 3000, 6000, 120f, 9f, 120f);
                Act(200, 60, "SATNA - [E] prevleknout", 6);
                Act(275, 315, "BAR - [E] obcerstveni, zdravi +60 ($50)", 12);
                break;
        }
    }

    const float ITopBar = 44f;      // horni pruh

    // Prepocet: velikost a umisteni mistnosti na obrazovce.
    float RScale => MathF.Min((W - 48f) / RNW, (H - ITopBar - 70f) / RNH);
    float ROX => (W - RNW * RScale) / 2f;
    float ROY => ITopBar + (H - ITopBar - 46f - RNH * RScale) / 2f;
    Vector2 IScreen(Vector2 local) => new(ROX + local.X * RScale, ROY + local.Y * RScale);

    Station? NearestRob()
    {
        Station? best = null; float bd = 95;
        foreach (var s in stations)
        {
            if (s.Action >= 0) continue;
            float d = Vector2.Distance(ipPos, s.Pos);
            if (d < bd) { bd = d; best = s; }
        }
        return best;
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
            ipPos += mv * 200f * dt;
            ipWalk += 200f * dt * 0.09f;
            ipFace = MathF.Atan2(mv.Y, mv.X);
        }
        ipPos.X = Math.Clamp(ipPos.X, WallM, RNW - WallM);
        ipPos.Y = Math.Clamp(ipPos.Y, WallM, RNH - WallM);

        // loupez - drzet E u sejfu/kasy
        var rob = NearestRob();
        if (rob != null && GetRobCd(rob.Action) <= 0 && Raylib.IsKeyDown(Keys.E))
        {
            if (!robbing) { robbing = true; robT = 0; Sfx.Play(Snd.Siren, 1f, 0.5f); }
            robT += dt;
            if (robT >= rob.RobTime)
            {
                int loot = rnd.Next(rob.LootMin, rob.LootMax + 1);
                money += loot; totalChaos += loot;
                heat = MathF.Max(heat, rob.RobHeat);
                SetRobCd(rob.Action, rob.RobCdSet);
                robbing = false;
                Sfx.Play(Snd.Bank);
                SaveGame();
                Say($"Vyloupeno! +${loot} - venku cekaji poldove, utikej!");
            }
            return;
        }
        if (robbing && rob == null) robbing = false;
        if (robbing && !Raylib.IsKeyDown(Keys.E)) robbing = false;

        // odchod z budovy (u vychodu nebo ESC)
        bool atDoor = Vector2.Distance(ipPos, exitPos) < 72;
        if (Raylib.IsKeyPressed(Keys.Escape) || Raylib.IsKeyPressed(Keys.P)) { ExitPlace(); return; }
        if (atDoor && Raylib.IsKeyPressed(Keys.E)) { ExitPlace(); return; }

        // interakce se stanici (jen ne-loupezne)
        if (Raylib.IsKeyPressed(Keys.E) && !atDoor)
        {
            Station? best = null; float bd = 95;
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
            case 10:
                if (pHp >= 100) Say("Zdravi je plne.");
                else if (money >= 40) { money -= 40; pHp = MathF.Min(100, pHp + 30); Sfx.Play(Snd.Pickup); Say("Zdravi +30."); }
                else Say("Nemas $40.");
                break;
            case 11:
                if (pHp >= 100) Say("Zdravi je plne.");
                else if (money >= 70) { money -= 70; pHp = MathF.Min(100, pHp + 55); Sfx.Play(Snd.Pickup); Say("Hotdog: zdravi +55."); }
                else Say("Nemas $70.");
                break;
            case 12:
                if (pHp >= 100) Say("Zdravi je plne.");
                else if (money >= 50) { money -= 50; pHp = MathF.Min(100, pHp + 60); Sfx.Play(Snd.Pickup); Say("Obcerstveni: zdravi +60."); }
                else Say("Nemas $50.");
                break;
        }
    }

    // ------------------------------------------------------------ kresleni interieru

    void DrawInterior()
    {
        Raylib.ClearBackground(new Color(14, 14, 20, 255));

        float ox = ROX, oy = ROY, sc = RScale;
        var dst = new Rectangle(ox, oy, RNW * sc, RNH * sc);
        string tex = $"07_svet/interiery/{roomTex}.png";

        if (Assets.Ready && Assets.Have(tex))
        {
            // stin pod mistnosti + samotny render
            Raylib.DrawRectangle((int)ox - 6, (int)oy - 6, (int)(RNW * sc) + 12, (int)(RNH * sc) + 12, Raylib.Fade(Pal.Black, 0.5f));
            Assets.DrawStretch(tex, dst, Pal.White);
        }
        else
        {
            // zaloha: plna barva kdyz PNG chybi
            Raylib.DrawRectangleRec(dst, new Color(60, 60, 70, 255));
            Raylib.DrawRectangleLinesEx(dst, 3, Raylib.Fade(Pal.Black, 0.4f));
        }

        // vychod - zvyrazneni kdyz jsi blizko
        var ep = IScreen(exitPos);
        bool nearExit = Vector2.Distance(ipPos, exitPos) < 72;
        float pulse = 0.5f + 0.5f * MathF.Sin(animT * 6f);
        Raylib.DrawCircleLines((int)ep.X, (int)ep.Y, 30 * sc, Raylib.Fade(Pal.Yellow, nearExit ? 0.9f : 0.35f + pulse * 0.2f));
        if (nearExit)
        {
            const string et = "VYCHOD [E]";
            int tw = Raylib.MeasureText(et, 18);
            Raylib.DrawRectangle((int)ep.X - tw / 2 - 8, (int)ep.Y - 58, tw + 16, 26, Raylib.Fade(Pal.Black, 0.7f));
            Txt(et, (int)ep.X - tw / 2, (int)ep.Y - 54, 18, Pal.Yellow);
        }

        // stanice - znacka + popisek kdyz jsi blizko
        foreach (var s in stations)
        {
            var sp = IScreen(s.Pos);
            bool near = Vector2.Distance(ipPos, s.Pos) < 95;
            bool isRob = s.Action < 0;
            bool onCd = isRob && GetRobCd(s.Action) > 0;
            Color mark = isRob ? (onCd ? new Color(130, 130, 135, 255) : new Color(240, 200, 70, 255))
                               : new Color(120, 200, 255, 255);

            float r = (near ? 13 : 10) + pulse * 2.5f;
            Raylib.DrawCircleV(sp, r + 3, Raylib.Fade(Pal.Black, 0.35f));
            Raylib.DrawCircleLines((int)sp.X, (int)sp.Y, r, Raylib.Fade(mark, near ? 1f : 0.7f));
            Raylib.DrawCircleV(sp, r * 0.45f, Raylib.Fade(mark, near ? 0.9f : 0.55f));
            Txt(isRob ? "!" : "E", (int)sp.X - 4, (int)sp.Y - 8, 16, Raylib.Fade(Pal.White, near ? 1f : 0.7f));

            if (near)
            {
                string lbl = onCd ? $"{s.Label}  (pockej {Math.Ceiling(GetRobCd(s.Action))}s)" : s.Label;
                int tw = Raylib.MeasureText(lbl, 18);
                Raylib.DrawRectangle((int)sp.X - tw / 2 - 8, (int)sp.Y - 46, tw + 16, 26, Raylib.Fade(Pal.Black, 0.75f));
                Txt(lbl, (int)sp.X - tw / 2, (int)sp.Y - 42, 18, onCd ? new Color(220, 220, 225, 255) : Pal.Yellow);
            }
        }

        DrawInteriorPlayer();

        // loupez - progress
        if (robbing)
        {
            Raylib.DrawRectangleRounded(new Rectangle(W / 2 - 180, 70, 360, 50), 0.3f, 8, Raylib.Fade(Pal.Black, 0.6f));
            Txt("LOUPEZ PROBIHA...", W / 2 - 100, 74, 22, Pal.Yellow);
            var rr = NearestRob();
            Bar(W / 2 - 160, 100, 320, 14, rr != null ? robT / rr.RobTime : 0f, Pal.Yellow);
        }

        // horni pruh s nazvem a penezi
        Raylib.DrawRectangle(0, 0, W, 40, Raylib.Fade(Pal.Black, 0.6f));
        Txt(PlaceName(place), 20, 8, 26, Pal.Yellow);
        Txt($"Hotovost: ${money}    Na ucte: ${savings}    HP {(int)pHp}  AP {(int)pArmor}", 300, 11, 20, Pal.White);
        Txt("WASD = chuze    [E] = pouzit    u vychodu [E] / ESC = odejit", 20, H - 28, 16, new Color(200, 200, 205, 255));

        if (msgT > 0)
        {
            int tw = Raylib.MeasureText(msg, 24);
            float a = Math.Clamp(msgT / 0.5f, 0f, 1f);
            Raylib.DrawRectangleRounded(new Rectangle(W / 2 - tw / 2 - 16, H - 92, tw + 32, 40), 0.3f, 8, Raylib.Fade(Pal.Black, 0.6f * a));
            Raylib.DrawText(msg, W / 2 - tw / 2, H - 84, 24, Raylib.Fade(Pal.White, a));
        }

        if (shop) DrawShop();
    }

    void DrawInteriorPlayer()
    {
        var p = IScreen(ipPos);
        float sc = MathF.Max(0.7f, RScale * 1.1f);
        var fwd = DirOf(ipFace);
        var side = new Vector2(-fwd.Y, fwd.X);
        float swing = MathF.Sin(ipWalk) * 3.6f * sc;
        var skin = new Color(238, 198, 158, 255);

        Raylib.DrawCircleV(p + new Vector2(2, 4), 10f * sc, Raylib.Fade(Pal.Black, 0.3f));
        Raylib.DrawCircleV(p + side * 3.4f * sc + fwd * swing, 3.2f * sc, new Color(45, 45, 60, 255));
        Raylib.DrawCircleV(p - side * 3.4f * sc - fwd * swing, 3.2f * sc, new Color(45, 45, 60, 255));
        Raylib.DrawCircleV(p, 8.6f * sc, playerCol);
        Raylib.DrawCircleLines((int)p.X, (int)p.Y, 8.6f * sc, Raylib.Fade(Pal.Black, 0.6f));
        Raylib.DrawCircleV(p + side * 7 * sc - fwd * swing * 0.6f, 2.6f * sc, skin);
        Raylib.DrawCircleV(p - side * 7 * sc + fwd * swing * 0.6f, 2.6f * sc, skin);
        Raylib.DrawCircleV(p + fwd * 1.5f * sc, 4.9f * sc, skin);
    }
}
