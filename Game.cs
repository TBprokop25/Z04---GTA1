using System.Numerics;
using Raylib_cs;

namespace Z04;

public class Game
{
    const float RadToDeg = 180f / MathF.PI;
    static readonly Vector2[] Dirs = { new(1, 0), new(0, 1), new(-1, 0), new(0, -1) };

    // kolik policistu / aut / vojaku / tanku chceme podle hvezdicek (index 0..5)
    static readonly int[] WantCops = { 0, 1, 2, 2, 2, 1 };
    static readonly int[] WantCars = { 0, 0, 1, 1, 2, 2 };
    static readonly int[] WantSoldiers = { 0, 0, 0, 0, 2, 4 };
    static readonly int[] WantTanks = { 0, 0, 0, 0, 0, 1 };

    readonly int W, H;
    readonly World world = new(1337);
    readonly Random rnd = new();

    readonly List<Vehicle> vehicles = new();
    readonly List<Person> people = new();
    readonly List<Bullet> bullets = new();
    readonly List<Explosion> explosions = new();
    readonly List<Particle> particles = new();
    readonly List<Popup> popups = new();
    Heli? heli;
    float blockadeCd;

    // hrac
    Vector2 pPos;
    float pAngle, pHp = 100, pArmor, fireT, deadT, lastHurtT = 99, carHitCd;
    const int ArmorPrice = 800;
    Vehicle? car;
    int money, weaponIdx;
    readonly bool[] owned = new bool[Weapons.All.Length];
    bool shop;
    bool holstered = true;   // zbran schovana (nebehas s ni porad v ruce)

    // chaos / stav hry
    float heat, comboT, spawnT, shake, animT, msgT, civT, sirenT;
    int combo, carsDestroyed, kills, totalChaos;
    string msg = "";
    Quest? quest;
    Camera2D cam;
    float vx0, vy0, vx1, vy1;

    // pickupy, lakovna, ohorele fleky po vybuchu
    class Pickup { public Vector2 Pos; public int Kind; public float Life = 22f; }   // 0 zdravi, 1 penize
    class Scorch { public Vector2 Pos; public float R, Age; }
    class Muzzle { public Vector2 Pos; public float Ang, T; }   // zablesk z hlavne
    class Mark { public Vector2 Pos; public float Age; }        // stopa po pneumatikach
    readonly List<Pickup> pickups = new();
    readonly List<Scorch> scorches = new();
    readonly List<Muzzle> muzzles = new();
    readonly List<Mark> tireMarks = new();
    float pWalk;   // akumulovana faze chuze hrace (pro animaci nohou)
    Vector2 spray => world.GarageDoor;
    const int SprayPrice = 300;
    float sprayCd, sprayT;
    bool robbing, mapOpen;
    float robT, bankCd;
    const float RobTime = 12f;
    int prevStars;
    float alertT;
    string alertText = "";

    // sluzby na mape
    const int HospitalFee = 200;
    float serviceCd;              // cooldown interakci u budov
    int bestChaos;               // nejlepsi skore (ulozene)
    static string SavePath => Path.Combine(AppContext.BaseDirectory, "chaoscity_save.txt");

    // den / noc
    float dayTime = 0.28f;       // 0..1 (0 = pulnoc, 0.5 = poledne)
    float Night => Math.Clamp((MathF.Cos(dayTime * MathF.Tau) + 0.35f) / 1.1f, 0f, 1f);

    // UI: obrazovky
    enum Scr { Menu, Play, Pause, Help }
    Scr scr = Scr.Menu, helpBack = Scr.Menu;
    int sel;
    public bool Quit, WantRestart;
    static readonly string[] MenuItems = { "Hrat", "Ovladani", "Konec" };
    static readonly string[] PauseItems = { "Pokracovat", "Ovladani", "Hlavni menu" };

    int Stars => heat < 15 ? 0 : heat < 55 ? 1 : heat < 95 ? 2 : heat < 130 ? 3 : heat < 165 ? 4 : 5;
    float Mult() => MathF.Min(1f + MathF.Max(0, combo - 1) * 0.25f, 5f);

    // ------------------------------------------------------------ pomocne

    static float AngleDiff(float target, float cur)
    {
        float d = (target - cur) % (2 * MathF.PI);
        if (d > MathF.PI) d -= 2 * MathF.PI;
        if (d < -MathF.PI) d += 2 * MathF.PI;
        return d;
    }

    static Vector2 DirOf(float a) => new(MathF.Cos(a), MathF.Sin(a));
    float Rnd() => (float)rnd.NextDouble();
    float Rnd(float a, float b) => a + Rnd() * (b - a);
    void Say(string s) { msg = s; msgT = 3.5f; }

    void LoadGame()
    {
        try
        {
            if (!File.Exists(SavePath)) return;
            var parts = File.ReadAllText(SavePath).Split(';');
            if (parts.Length >= 1 && int.TryParse(parts[0], out int bc)) bestChaos = bc;
            if (parts.Length >= 2 && int.TryParse(parts[1], out int mn)) money = mn;
        }
        catch { /* poskozeny save ignorujeme */ }
    }

    void SaveGame()
    {
        try
        {
            bestChaos = Math.Max(bestChaos, totalChaos);
            File.WriteAllText(SavePath, $"{bestChaos};{money}");
        }
        catch { }
    }

    // ------------------------------------------------------------ start

    public Game(int w, int h)
    {
        W = w; H = h;
        owned[0] = true;
        LoadGame();
        pPos = World.Node(4, 4);

        cam = new Camera2D();
        cam.Offset = new Vector2(W / 2f, H / 2f);
        cam.Target = pPos;
        cam.Zoom = 1f;

        for (int i = 0; i < 28; i++) vehicles.Add(SpawnCivil(true, 500));
        for (int i = 0; i < 14; i++) vehicles.Add(SpawnCivil(false, 250));
        for (int i = 0; i < 70; i++) SpawnPerson(PKind.Civil, 120, 1800);

        NewQuest();
        Say("Vitej v Chaos City! Ukradni auto (E), znic co sezenes a vydelej.");
    }

    Vehicle SpawnCivil(bool ai, float minDist)
    {
        for (int attempt = 0; ; attempt++)
        {
            bool horiz = rnd.Next(2) == 0;
            int k = rnd.Next(World.Grid + 1);
            float along = Rnd(150, World.Size - 150);
            int dir = horiz ? (rnd.Next(2) == 0 ? 0 : 2) : (rnd.Next(2) == 0 ? 1 : 3);
            Vector2 d = Dirs[dir];
            Vector2 right = new(-d.Y, d.X);
            float off = ai ? 20f : 54f;                 // jizdni pruh vs. krajnice (parkujici auta)
            float c = k * World.Cell + World.Road / 2;
            Vector2 pos = (horiz ? new Vector2(along, c) : new Vector2(c, along)) + right * off;
            if (attempt < 30 && Vector2.Distance(pos, pPos) < minDist) continue;

            var v = new Vehicle(VKind.Civil, pos, dir * MathF.PI / 2, Pal.Cars[rnd.Next(Pal.Cars.Length)]);
            // vyber modelu: vetsinou sedan, obcas dodavka/autobus/motorka/sanitka
            double mr = rnd.NextDouble();
            v.Model = mr < 0.62 ? VModel.Sedan : mr < 0.78 ? VModel.Van : mr < 0.88 ? VModel.Bike
                    : mr < 0.96 ? VModel.Bus : VModel.Ambulance;
            if (v.Model == VModel.Ambulance) { v.Body = new Color(240, 240, 245, 255); v.MaxHp = v.Hp = 130f; }
            else if (v.Model == VModel.Bus) { v.MaxHp = v.Hp = 200f; }
            v.Dir = dir;
            v.Ai = ai;
            if (ai) v.Speed = 150;
            return v;
        }
    }

    void SpawnPoliceVehicle(VKind kind)
    {
        for (int i = 0; i < 30; i++)
        {
            Vector2 pos = pPos + DirOf(Rnd(0, 2 * MathF.PI)) * Rnd(650, 950);
            if (!world.OnRoad(pos)) continue;
            var v = new Vehicle(kind, pos, Rnd(0, 2 * MathF.PI), kind == VKind.Police ? Pal.Police : Pal.Army);
            v.Ai = true;
            vehicles.Add(v);
            return;
        }
    }

    void SpawnPerson(PKind kind, float minDist, float maxDist)
    {
        for (int i = 0; i < 30; i++)
        {
            Vector2 pos = pPos + DirOf(Rnd(0, 2 * MathF.PI)) * Rnd(minDist, maxDist);
            if (pos.X < 20 || pos.Y < 20 || pos.X > World.Size - 20 || pos.Y > World.Size - 20) continue;
            if (world.PointInBuilding(pos)) continue;

            Color col = kind switch
            {
                PKind.Cop => new Color(40, 60, 200, 255),
                PKind.Soldier => new Color(80, 100, 50, 255),
                _ => new Color((byte)rnd.Next(70, 230), (byte)rnd.Next(70, 230), (byte)rnd.Next(70, 230), (byte)255),
            };
            float ph = Rnd(0, 2 * MathF.PI);
            people.Add(new Person(kind, pos, col) { Angle = ph, Phase = ph, AnimWalk = ph });
            return;
        }
    }

    // ------------------------------------------------------------ update

    public void Update(float dt)
    {
        animT += dt;
        dayTime = (dayTime + dt / 150f) % 1f;   // cely den/noc cyklus za 150 s

        bool driving = scr == Scr.Play && !mapOpen && !shop && car != null && car.State == VState.Alive;
        Sfx.Engine(driving, driving ? MathF.Abs(car!.Speed) / car.MaxSpeed : 0f);

        if (scr != Scr.Play) { UpdateMenu(); return; }
        if (mapOpen)
        {
            if (Raylib.IsKeyPressed(Keys.M) || Raylib.IsKeyPressed(Keys.Escape) || Raylib.IsKeyPressed(Keys.P)) mapOpen = false;
            return;
        }
        if (Raylib.IsKeyPressed(Keys.M) && !shop) { mapOpen = true; return; }

        if (Raylib.IsKeyPressed(Keys.P) || Raylib.IsKeyPressed(Keys.Escape))
        {
            if (shop) shop = false;
            else { scr = Scr.Pause; sel = 0; return; }
        }
        if (msgT > 0) msgT -= dt;

        if (Raylib.IsKeyPressed(Keys.B) && deadT <= 0) shop = !shop;
        if (shop) { UpdateShop(); return; }

        UpdatePlayer(dt);
        UpdateVehicles(dt);
        UpdateHeli(dt);
        UpdatePeople(dt);
        UpdateBullets(dt);
        UpdateWanted(dt);
        UpdateQuest(dt);
        UpdatePickups(dt);
        UpdateBank(dt);
        UpdateEffects(dt);
        Cleanup();
        UpdateCamera(dt);
    }

    Rectangle ShopRow(int i) => new(W / 2 - 284, H / 2 - 190 + 92 + i * 46, 568, 42);

    void UpdateShop()
    {
        var mp = Raylib.GetMousePosition();
        bool click = Raylib.IsMouseButtonPressed(Keys.MouseLeft);
        for (int i = 0; i < Weapons.All.Length; i++)
        {
            bool pressed = Raylib.IsKeyPressed(Keys.Num(i)) ||
                           (click && Raylib.CheckCollisionPointRec(mp, ShopRow(i)));
            if (!pressed) continue;
            var w = Weapons.All[i];
            if (owned[i]) { weaponIdx = i; Say("Vybrano: " + w.Name); }
            else if (money >= w.Price)
            {
                money -= w.Price; owned[i] = true; weaponIdx = i;
                Say("Koupeno: " + w.Name);
            }
            else Say("Nedostatek penez na " + w.Name);
        }

        // brneni (posledni radek)
        int ai = Weapons.All.Length;
        if (Raylib.IsKeyPressed(Keys.Num(ai)) || (click && Raylib.CheckCollisionPointRec(mp, ShopRow(ai))))
        {
            if (pArmor >= 100) Say("Brneni je plne.");
            else if (money >= ArmorPrice) { money -= ArmorPrice; pArmor = MathF.Min(100, pArmor + 50); Say("Koupeno brneni (+50)."); }
            else Say($"Nedostatek penez na brneni (${ArmorPrice}).");
        }
    }

    // ---------- hrac

    void UpdatePlayer(float dt)
    {
        if (carHitCd > 0) carHitCd -= dt;
        lastHurtT += dt;
        fireT -= dt;

        if (deadT > 0)
        {
            deadT -= dt;
            if (deadT <= 0) Respawn();
            return;
        }

        if (pHp < 100 && lastHurtT > 6f) pHp = MathF.Min(100, pHp + 4 * dt);
        if (Raylib.IsKeyPressed(Keys.N)) { NewQuest(); Say("Novy quest."); }

        if (serviceCd > 0) serviceCd -= dt;

        if (Raylib.IsKeyPressed(Keys.E))
        {
            if (car != null) ExitCar();
            else if (CanRobBank()) StartRobbery();
            else if (NearDoor(world.HospitalDoor)) UseHospital();
            else if (NearDoor(world.SafehouseDoor)) UseSafehouse();
            else TryEnterCar();
        }
        if (car != null) return;   // v aute se rizeni resi v UpdateVehicles

        if (Raylib.IsKeyPressed(Keys.Q))
        {
            do { weaponIdx = (weaponIdx + 1) % Weapons.All.Length; } while (!owned[weaponIdx]);
            holstered = false;
            Say("Zbran: " + Weapons.All[weaponIdx].Name);
        }

        if (Raylib.IsKeyPressed(Keys.F))
        {
            holstered = !holstered;
            Sfx.Play(Snd.Click);
            Say(holstered ? "Zbran schovana" : "Zbran pripravena");
        }

        // pohyb pesky
        Vector2 mv = Vector2.Zero;
        if (Raylib.IsKeyDown(Keys.W) || Raylib.IsKeyDown(Keys.Up)) mv.Y -= 1;
        if (Raylib.IsKeyDown(Keys.S) || Raylib.IsKeyDown(Keys.Down)) mv.Y += 1;
        if (Raylib.IsKeyDown(Keys.A) || Raylib.IsKeyDown(Keys.Left)) mv.X -= 1;
        if (Raylib.IsKeyDown(Keys.D) || Raylib.IsKeyDown(Keys.Right)) mv.X += 1;
        if (mv != Vector2.Zero) mv = Vector2.Normalize(mv);
        float spd = Raylib.IsKeyDown(Keys.Shift) ? 220f : 150f;
        pPos += mv * spd * dt;
        pWalk += mv.Length() * spd * dt * 0.09f;   // animace nohou podle ujete vzdalenosti
        world.Resolve(ref pPos, 9);

        // mireni a strelba
        var mw = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), cam);
        pAngle = MathF.Atan2(mw.Y - pPos.Y, mw.X - pPos.X);

        // schovana zbran: prvni klik ji jen vytahne (nebehas s ni porad)
        if (holstered)
        {
            if (Raylib.IsMouseButtonPressed(Keys.MouseLeft)) { holstered = false; Say("Zbran pripravena"); }
            return;
        }

        if (Raylib.IsMouseButtonDown(Keys.MouseLeft) && fireT <= 0)
        {
            var w = Weapons.All[weaponIdx];
            fireT = w.Cooldown;
            Shoot(pPos + DirOf(pAngle) * 14, pAngle, w.Damage, w.Spread, w.Speed, w.Range, w.Explosive, true, null, w.Pellets);
            AlertCivilians(pPos, 320, 3f);
            if (w.Explosive) shake = MathF.Max(shake, 5);
            Snd sfx = w.Name == "Brokovnice" ? Snd.Shotgun
                    : w.Name is "SMG" or "Minigun" ? Snd.Smg
                    : w.Explosive ? Snd.Nitro : Snd.Shot;
            Sfx.Play(sfx, Rnd(0.95f, 1.08f));
        }
    }

    void Respawn()
    {
        pHp = 100; pArmor = 0; heat = 0; car = null; combo = 0; sprayT = 0; robbing = false; heli = null; holstered = true;
        pPos = World.Node(4, 4);
        int fee = (int)(money * 0.2f);
        money -= fee;
        Say($"Nemocnice te vylecila. Poplatek: ${fee}");
    }

    void TryEnterCar()
    {
        Vehicle? best = null;
        float bd = 70;
        foreach (var v in vehicles)
        {
            if (v.State != VState.Alive || v.PlayerDriven) continue;
            float d = Vector2.Distance(v.Pos, pPos) - v.Radius;
            if (d < bd) { bd = d; best = v; }
        }
        if (best == null) { Say("Zadne auto v dosahu."); return; }

        if (best.Ai) heat += 8f;      // kradez auta ridici
        best.Ai = false;
        best.PlayerDriven = true;
        best.HitByPlayer = false;
        car = best;
    }

    void ExitCar()
    {
        var v = car!;
        v.PlayerDriven = false;
        car = null;
        var left = new Vector2(MathF.Sin(v.Angle), -MathF.Cos(v.Angle));
        pPos = v.Pos + left * (v.Radius + 14);
        world.Resolve(ref pPos, 9);
    }

    void HurtPlayer(float dmg)
    {
        if (deadT > 0) return;
        if (pArmor > 0)                      // brneni pohlti vetsinu zasahu
        {
            float absorb = MathF.Min(pArmor, dmg * 0.7f);
            pArmor -= absorb;
            dmg -= absorb;
        }
        pHp -= dmg;
        lastHurtT = 0;
        shake = MathF.Max(shake, 4);
        Sfx.Play(Snd.Hurt, Rnd(0.9f, 1.1f));
        if (pHp <= 0)
        {
            pHp = 0;
            deadT = 3.5f;
            if (car != null) { car.PlayerDriven = false; car = null; }
            Sfx.Play(Snd.Explosion, 0.7f);
            SaveGame();
        }
    }

    // ---------- vozidla

    void DriveVehicle(Vehicle v, float throttle, float steer, bool brake, float dt)
    {
        if (throttle > 0) v.Speed += (v.Speed < 0 ? 2.2f : 1f) * v.Accel * throttle * dt;
        else if (throttle < 0) v.Speed += (v.Speed > 0 ? 2.2f : 1f) * v.Accel * throttle * dt;
        else
        {
            float f = 220f * dt;
            v.Speed = MathF.Abs(v.Speed) <= f ? 0 : v.Speed - MathF.Sign(v.Speed) * f;
        }
        if (brake)
        {
            float f = 760f * dt;
            v.Speed = MathF.Abs(v.Speed) <= f ? 0 : v.Speed - MathF.Sign(v.Speed) * f;
        }
        v.Speed = Math.Clamp(v.Speed, -v.MaxSpeed * 0.4f, v.MaxSpeed);

        float turn = v.TurnRate * Math.Clamp(MathF.Abs(v.Speed) / 120f, 0f, 1f) * (v.Speed >= 0 ? 1f : -1f);
        v.Angle += steer * turn * dt;
    }

    void MoveVehicle(Vehicle v, float dt)
    {
        v.Pos += v.Forward * v.Speed * dt;
        var push = world.Resolve(ref v.Pos, v.Radius);
        if (push.LengthSquared() > 0.25f)
        {
            float impact = MathF.Abs(v.Speed);
            if (impact > 140)
            {
                DamageVehicle(v, (impact - 140) * 0.15f, false);
                if (v.PlayerDriven) shake = MathF.Max(shake, MathF.Min(10, impact / 40));
            }
            v.Speed *= 0.5f;
        }
    }

    void UpdatePlayerCar(Vehicle v, float dt)
    {
        float thr = 0, st = 0;
        if (Raylib.IsKeyDown(Keys.W) || Raylib.IsKeyDown(Keys.Up)) thr += 1;
        if (Raylib.IsKeyDown(Keys.S) || Raylib.IsKeyDown(Keys.Down)) thr -= 1;
        if (Raylib.IsKeyDown(Keys.D) || Raylib.IsKeyDown(Keys.Right)) st += 1;
        if (Raylib.IsKeyDown(Keys.A) || Raylib.IsKeyDown(Keys.Left)) st -= 1;
        bool brake = Raylib.IsKeyDown(Keys.Space);
        DriveVehicle(v, thr, st, brake, dt);

        // nitro: Shift pri plynu dopredu = kratkodobe prekroceni maximalky + plameny z vyfuku
        if (Raylib.IsKeyPressed(Keys.Shift) && v.Kind != VKind.Tank) Sfx.Play(Snd.Nitro, 1.2f, 0.7f);
        if (Raylib.IsKeyDown(Keys.Shift) && thr > 0 && v.Kind != VKind.Tank && v.State == VState.Alive)
        {
            v.Speed = MathF.Min(v.Speed + v.Accel * 1.6f * dt, v.MaxSpeed * 1.4f);
            var ex = v.Pos - v.Forward * (v.Length * 0.5f);
            AddParticle(ex + new Vector2(Rnd(-4, 4), Rnd(-4, 4)), -v.Forward * Rnd(40, 110), 0.3f, Rnd(5, 9), 0);
        }

        // stopy po pneumatikach pri smyku (rucni brzda nebo ostra zatacka v rychlosti)
        if ((brake || MathF.Abs(st) > 0.5f) && MathF.Abs(v.Speed) > 150 && v.Kind != VKind.Tank)
        {
            var fw = v.Forward;
            var rg = new Vector2(-fw.Y, fw.X);
            var rear = v.Pos - fw * (v.Length * 0.3f);
            tireMarks.Add(new Mark { Pos = rear + rg * (v.Width * 0.38f) });
            tireMarks.Add(new Mark { Pos = rear - rg * (v.Width * 0.38f) });
            while (tireMarks.Count > 400) tireMarks.RemoveAt(0);
        }

        if (v.Kind == VKind.Tank)
        {
            var mw = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), cam);
            v.TurretAngle = MathF.Atan2(mw.Y - v.Pos.Y, mw.X - v.Pos.X);
            if (Raylib.IsMouseButtonDown(Keys.MouseLeft) && fireT <= 0)
            {
                fireT = 1.0f;
                Shoot(v.Pos + DirOf(v.TurretAngle) * 46, v.TurretAngle, 0, 0.02f, 700, 1200, true, true, v);
                shake = MathF.Max(shake, 6);
            }
        }
    }

    void UpdateCivilAi(Vehicle v, float dt)
    {
        var d = Dirs[v.Dir];
        int ni = Math.Clamp((int)MathF.Round((v.Pos.X - World.Road / 2) / World.Cell), 0, World.Grid);
        int nj = Math.Clamp((int)MathF.Round((v.Pos.Y - World.Road / 2) / World.Cell), 0, World.Grid);
        var node = World.Node(ni, nj);
        int id = World.NodeId(ni, nj);

        // na krizovatce nahodne vyber novy smer
        if (id != v.LastNode && MathF.Abs(Vector2.Dot(v.Pos - node, d)) < 10 && Vector2.Distance(v.Pos, node) < 45)
        {
            v.LastNode = id;
            var options = new List<int>();
            for (int nd = 0; nd < 4; nd++)
            {
                if (nd == (v.Dir + 2) % 4) continue;
                int ti = ni + (int)Dirs[nd].X, tj = nj + (int)Dirs[nd].Y;
                if (ti < 0 || tj < 0 || ti > World.Grid || tj > World.Grid) continue;
                options.Add(nd);
                if (nd == v.Dir) { options.Add(nd); options.Add(nd); }   // radeji rovne
            }
            v.Dir = options.Count > 0 ? options[rnd.Next(options.Count)] : (v.Dir + 2) % 4;
        }

        float target = v.Dir * MathF.PI / 2;
        float diff = AngleDiff(target, v.Angle);
        v.Angle += Math.Clamp(diff, -3.2f * dt, 3.2f * dt);

        if (MathF.Abs(diff) < 0.35f)    // drz se v pravem jizdnim pruhu
        {
            var dd = Dirs[v.Dir];
            var right = new Vector2(-dd.Y, dd.X);
            if (dd.X != 0) v.Pos.Y += (node.Y + right.Y * 20 - v.Pos.Y) * MathF.Min(1, 4 * dt);
            else v.Pos.X += (node.X + right.X * 20 - v.Pos.X) * MathF.Min(1, 4 * dt);
        }

        // brzdi pred prekazkou
        float want = 170f;
        var fwd = v.Forward;
        foreach (var o in vehicles)
        {
            if (o == v) continue;
            var rel = o.Pos - v.Pos;
            float ahead = Vector2.Dot(rel, fwd);
            float side = MathF.Abs(rel.X * fwd.Y - rel.Y * fwd.X);
            if (ahead > 0 && ahead < 85 + v.Speed * 0.2f && side < 30) { want = 0; break; }
        }
        if (want == 0) v.StuckT += dt; else v.StuckT = 0;

        if (v.Speed < want) v.Speed = MathF.Min(want, v.Speed + 260 * dt);
        else v.Speed = MathF.Max(want, v.Speed - 700 * dt);

        if (v.StuckT > 3.5f) { v.Dir = (v.Dir + 2) % 4; v.StuckT = 0; v.LastNode = -1; }   // otoc se
    }

    void UpdateChaseAi(Vehicle v, float dt)
    {
        if (Stars == 0 || deadT > 0) { DriveVehicle(v, 0, 0, true, dt); return; }

        if (v.Blockade)   // zatarasc stoji na miste a odstreluje
        {
            DriveVehicle(v, 0, 0, true, dt);
            var d2 = pPos - v.Pos;
            float dist2 = d2.Length();
            v.FireT -= dt;
            if (dist2 < 360 && v.FireT <= 0)
            {
                v.FireT = Tune.CarFireCd;
                float a2 = MathF.Atan2(d2.Y, d2.X);
                Shoot(v.Pos + DirOf(a2) * 24, a2, 6, 0.18f, 700, 480, false, false, v);
            }
            return;
        }

        Vector2 to = pPos - v.Pos;
        float dist = to.Length();
        float desired = MathF.Atan2(to.Y, to.X);
        v.FireT -= dt;

        if (v.Kind == VKind.Tank)
        {
            v.TurretAngle = desired;
            if (dist < 600 && v.FireT <= 0)
            {
                v.FireT = 3.4f;
                Shoot(v.Pos + DirOf(desired) * 46, desired + Rnd(-0.05f, 0.05f), 0, 0.03f, 520, 900, true, false, v);
            }
        }
        else if (dist < 300 && v.FireT <= 0)
        {
            v.FireT = Tune.CarFireCd;
            Shoot(v.Pos + DirOf(desired) * 24, desired, 6, 0.18f, 700, 480, false, false, v);
        }

        if (v.ReverseT > 0)
        {
            v.ReverseT -= dt;
            DriveVehicle(v, -1, v.ReverseSteer, false, dt);
            return;
        }

        float diff = AngleDiff(desired, v.Angle);
        float thr = MathF.Abs(diff) > 1.2f ? 0.4f : 1f;
        if (v.Kind == VKind.Tank && dist < 350) thr = 0;
        DriveVehicle(v, thr, Math.Clamp(diff * 2f, -1f, 1f), false, dt);

        if (MathF.Abs(v.Speed) < 25 && dist > 90 && v.Kind != VKind.Tank) v.StuckT += dt; else v.StuckT = 0;
        if (v.StuckT > 0.8f)
        {
            v.StuckT = 0;
            v.ReverseT = 1.1f;
            v.ReverseSteer = rnd.Next(2) == 0 ? 1f : -1f;
        }
    }

    void UpdateVehicles(float dt)
    {
        foreach (var v in vehicles)
        {
            if (v.State == VState.Wreck) { v.WreckAge += dt; continue; }

            if (v.State == VState.Burning)
            {
                v.BurnTime -= dt;
                v.SmokeT -= dt;
                if (v.SmokeT <= 0)
                {
                    v.SmokeT = 0.04f;
                    AddParticle(v.Pos + new Vector2(Rnd(-10, 10), Rnd(-10, 10)), new Vector2(Rnd(-25, 25), Rnd(-25, 25)), 0.35f, 7, 0);
                    AddParticle(v.Pos + new Vector2(Rnd(-8, 8), Rnd(-8, 8)), new Vector2(Rnd(-30, 30), Rnd(-30, 30)), 0.9f, 9, 1);
                }
                if (v.PlayerDriven) UpdatePlayerCar(v, dt);
                else v.Speed *= MathF.Pow(0.35f, dt);
                MoveVehicle(v, dt);

                if (v.BurnTime <= 0)
                {
                    Explode(v.Pos, 130, 140, v.HitByPlayer);
                    v.State = VState.Wreck;
                    v.Speed = 0;
                    if (v.PlayerDriven) { v.PlayerDriven = false; if (car == v) car = null; }
                }
                continue;
            }

            if (v.PlayerDriven) { if (sprayT > 0) DriveVehicle(v, 0, 0, true, dt); else UpdatePlayerCar(v, dt); }
            else if (v.Ai && v.Kind == VKind.Civil) UpdateCivilAi(v, dt);
            else if (v.Ai) UpdateChaseAi(v, dt);
            else DriveVehicle(v, 0, 0, false, dt);

            MoveVehicle(v, dt);

            if (v.Hp < v.MaxHp * 0.35f)    // poskozene auto koure
            {
                v.SmokeT -= dt;
                if (v.SmokeT <= 0)
                {
                    v.SmokeT = 0.12f;
                    AddParticle(v.Pos, new Vector2(Rnd(-15, 15), Rnd(-15, 15)), 0.8f, 7, 1);
                }
            }

            // prejizdeni chodcu
            if (MathF.Abs(v.Speed) > 110)
            {
                foreach (var p in people)
                {
                    if (p.Dead) continue;
                    if (Vector2.Distance(p.Pos, v.Pos) < v.Radius + 8)
                        DamagePerson(p, 200, v.PlayerDriven);
                }
                if (car == null && deadT <= 0 && carHitCd <= 0 && Vector2.Distance(pPos, v.Pos) < v.Radius + 9)
                {
                    HurtPlayer(MathF.Abs(v.Speed) * 0.2f);
                    carHitCd = 0.6f;
                }
            }
        }

        // srazky aut mezi sebou (vraky jsou staticke prekazky)
        for (int i = 0; i < vehicles.Count; i++)
        {
            for (int j = i + 1; j < vehicles.Count; j++)
            {
                var a = vehicles[i];
                var b = vehicles[j];
                bool aStatic = a.State == VState.Wreck, bStatic = b.State == VState.Wreck;
                if (aStatic && bStatic) continue;

                var delta = b.Pos - a.Pos;
                float minD = a.Radius + b.Radius;
                if (MathF.Abs(delta.X) > minD || MathF.Abs(delta.Y) > minD) continue;
                float dist = delta.Length();
                if (dist >= minD) continue;
                if (dist < 0.01f) { delta = new Vector2(1, 0); dist = 1; }

                var n = delta / dist;
                float overlap = minD - dist;
                if (aStatic) b.Pos += n * overlap;
                else if (bStatic) a.Pos -= n * overlap;
                else { a.Pos -= n * overlap * 0.5f; b.Pos += n * overlap * 0.5f; }

                float rel = Vector2.Dot(a.Forward * a.Speed - b.Forward * b.Speed, n);
                if (rel <= 0) continue;

                if (!aStatic) a.Speed -= Vector2.Dot(n, a.Forward) * rel * 0.7f;
                if (!bStatic) b.Speed += Vector2.Dot(n, b.Forward) * rel * 0.7f;

                if (rel > 70)
                {
                    float dmg = (rel - 70) * 0.16f;
                    DamageVehicle(b, dmg * (a.Kind == VKind.Tank ? 2.5f : 1f), a.PlayerDriven);
                    DamageVehicle(a, dmg * (b.Kind == VKind.Tank ? 2.5f : 1f), b.PlayerDriven);
                    if (a.PlayerDriven || b.PlayerDriven) shake = MathF.Max(shake, MathF.Min(9, rel / 40));
                }
            }
        }

        if (car != null) pPos = car.Pos;
    }

    void DamageVehicle(Vehicle v, float dmg, bool byPlayer)
    {
        if (v.State != VState.Alive || dmg <= 0) return;
        if (byPlayer && !v.PlayerDriven) v.HitByPlayer = true;
        v.Hp -= dmg;

        if (v.Hp <= 0)
        {
            v.Hp = 0;
            v.State = VState.Burning;
            v.BurnTime = Rnd(1.8f, 3.0f);
            v.Ai = false;

            if (v.HitByPlayer && !v.PlayerDriven)
            {
                carsDestroyed++;
                int reward = 200; float h = 12;
                if (v.Kind == VKind.Police) { reward = 400; h = 40; }
                else if (v.Kind == VKind.Tank) { reward = 2500; h = 80; }
                heat += h;
                DropLoot(v.Pos, v.Kind == VKind.Civil ? 0.3f : 0.8f);
                AddMoney(reward, v.Pos);
                QuestProgress(QType.DestroyCars);
            }
        }
        else if (byPlayer && !v.PlayerDriven && v.Kind != VKind.Civil)
        {
            heat += dmg * 0.2f;    // utok na policii / armadu
        }
    }

    // ---------- lide

    Vector2 Wander(Person p, float dt, float speed)
    {
        p.WalkT -= dt;
        if (p.WalkT <= 0)
        {
            p.WalkT = Rnd(1, 4);
            p.Angle = Rnd(0, 2 * MathF.PI);
            p.Moving = rnd.Next(3) != 0;
        }
        return p.Moving ? DirOf(p.Angle) * speed : Vector2.Zero;
    }

    void UpdatePeople(float dt)
    {
        int stars = Stars;
        foreach (var p in people)
        {
            if (p.Dead) { p.DeadT += dt; continue; }

            Vector2 vel;
            p.FireT -= dt;

            if (p.Kind == PKind.Civil)
            {
                if (p.FleeT > 0)
                {
                    p.FleeT -= dt;
                    var away = p.Pos - p.FleeFrom;
                    if (away.LengthSquared() < 1) away = new Vector2(1, 0);
                    vel = Vector2.Normalize(away) * 140f;
                }
                else vel = Wander(p, dt, 45f);
            }
            else if (stars > 0 && deadT <= 0)
            {
                var to = pPos - p.Pos;
                float d = to.Length();
                float ang = MathF.Atan2(to.Y, to.X);
                p.Angle = ang;
                bool sold = p.Kind == PKind.Soldier;
                float keep = sold ? 260f : 200f;
                float range = sold ? 450f : 380f;
                var dir = DirOf(ang);

                vel = Vector2.Zero;
                if (d > keep) vel = dir * (sold ? 115f : 100f);
                else if (d < keep * 0.5f) vel = -dir * 60f;

                if (d < range && p.FireT <= 0)
                {
                    if (sold)
                    {
                        p.FireT = Tune.SoldierFireCd;
                        Shoot(p.Pos + dir * 12, ang, 5, 0.20f, 800, 520, false, false, null);
                    }
                    else
                    {
                        p.FireT = Tune.CopFireCd;
                        Shoot(p.Pos + dir * 12, ang, 7, 0.16f, 700, 450, false, false, null);
                    }
                }
            }
            else vel = Wander(p, dt, 40f);

            // civiliste se drzi chodniku - kdyz by vkrocili na silnici, otoci se zpet
            if (p.Kind == PKind.Civil && p.FleeT <= 0 && vel.LengthSquared() > 1)
            {
                if (world.OnRoad(p.Pos + Vector2.Normalize(vel) * 16))
                {
                    p.Angle = MathF.Atan2(-vel.Y, -vel.X);
                    p.WalkT = Rnd(0.6f, 1.6f);
                    vel = Vector2.Zero;
                }
            }

            p.Pos += vel * dt;
            p.AnimWalk += vel.Length() * dt * 0.09f;
            world.Resolve(ref p.Pos, 9);
            if (p.Kind == PKind.Civil && vel.LengthSquared() > 1) p.Angle = MathF.Atan2(vel.Y, vel.X);
        }
    }

    void AlertCivilians(Vector2 from, float radius, float time)
    {
        foreach (var p in people)
        {
            if (p.Dead || p.Kind != PKind.Civil) continue;
            if (Vector2.Distance(p.Pos, from) < radius) { p.FleeT = time; p.FleeFrom = from; }
        }
    }

    void DamagePerson(Person p, float dmg, bool byPlayer)
    {
        if (p.Dead) return;
        if (byPlayer) p.HitByPlayer = true;
        p.Hp -= dmg;
        if (p.Kind != PKind.Civil && byPlayer) heat += dmg * 0.25f;

        if (p.Hp > 0)
        {
            if (p.Kind == PKind.Civil) { p.FleeT = 4f; p.FleeFrom = pPos; }
            return;
        }

        p.Dead = true;
        p.DeadT = 0;
        p.DeathAng = p.Angle + Rnd(-0.6f, 0.6f);
        kills++;
        for (int i = 0; i < 5; i++)
            AddParticle(p.Pos, new Vector2(Rnd(-60, 60), Rnd(-60, 60)), 0.5f, 4, 2);

        if (!p.HitByPlayer) return;
        switch (p.Kind)
        {
            case PKind.Civil: heat += 10; AddMoney(50, p.Pos); break;
            case PKind.Cop: heat += 45; DropLoot(p.Pos, 0.6f); AddMoney(150, p.Pos); QuestProgress(QType.KillCops); break;
            default: heat += 55; DropLoot(p.Pos, 0.8f); AddMoney(300, p.Pos); QuestProgress(QType.KillCops); break;
        }
    }

    // ---------- strelba a vybuchy

    void Shoot(Vector2 origin, float angle, float damage, float spread, float speed, float range,
               bool explosive, bool player, Vehicle? ignore, int pellets = 1)
    {
        for (int i = 0; i < pellets; i++)
        {
            float a = angle + Rnd(-spread, spread);
            float s = speed * Rnd(0.92f, 1.08f);
            bullets.Add(new Bullet
            {
                Pos = origin,
                Vel = DirOf(a) * s,
                Damage = damage,
                Life = range / s,
                Player = player,
                Explosive = explosive,
                Ignore = ignore,
            });
        }
        AddParticle(origin, DirOf(angle) * 40, 0.08f, 6, 2);
        if (muzzles.Count < 60) muzzles.Add(new Muzzle { Pos = origin, Ang = angle, T = 0.06f });
    }

    void UpdateBullets(float dt)
    {
        foreach (var b in bullets)
        {
            if (b.Dead) continue;
            b.Pos += b.Vel * dt;
            b.Life -= dt;
            if (b.Explosive) AddParticle(b.Pos, Vector2.Zero, 0.5f, 5, 1);

            bool hit = false;

            if (b.Life <= 0 || b.Pos.X < 0 || b.Pos.Y < 0 || b.Pos.X > World.Size || b.Pos.Y > World.Size ||
                world.PointInBuilding(b.Pos))
                hit = true;

            if (!hit)
            {
                foreach (var v in vehicles)
                {
                    if (v == b.Ignore) continue;
                    if (Vector2.Distance(v.Pos, b.Pos) > v.Radius * 0.95f) continue;

                    if (v.State == VState.Wreck) { hit = true; break; }
                    if (b.Player)
                    {
                        if (v.PlayerDriven) continue;
                        float dm = v.Kind == VKind.Tank ? b.Damage * 0.35f : b.Damage;
                        DamageVehicle(v, dm, true);
                    }
                    else
                    {
                        if (!(v.PlayerDriven || v.Kind == VKind.Civil)) continue;   // zadna friendly fire
                        DamageVehicle(v, b.Damage * Tune.CopDamageMul, false);
                    }
                    hit = true;
                    break;
                }
            }

            if (!hit && b.Player)
            {
                foreach (var p in people)
                {
                    if (p.Dead || Vector2.Distance(p.Pos, b.Pos) > 10) continue;
                    DamagePerson(p, b.Damage, true);
                    hit = true;
                    break;
                }
            }

            if (!hit && b.Player && heli != null && Vector2.Distance(heli.Pos, b.Pos) < heli.Radius)
            {
                DamageHeli(b.Damage, true);   // vybusne strely navic vybuchnou v bloku 'hit' nize
                hit = true;
            }

            if (!hit && !b.Player && car == null && deadT <= 0 && Vector2.Distance(pPos, b.Pos) < 10)
            {
                HurtPlayer(b.Damage);
                hit = true;
            }

            if (hit)
            {
                b.Dead = true;
                if (b.Explosive) Explode(b.Pos, 120, 150, b.Player);
                else AddParticle(b.Pos, Vector2.Zero, 0.12f, 4, 2);
            }
        }
        bullets.RemoveAll(b => b.Dead);
    }

    void Explode(Vector2 pos, float radius, float damage, bool byPlayer)
    {
        explosions.Add(new Explosion { Pos = pos, Radius = radius });
        scorches.Add(new Scorch { Pos = pos, R = radius * 0.55f });
        float evol = Math.Clamp(1f - Vector2.Distance(pos, pPos) / 1400f, 0.1f, 1f);
        Sfx.Play(Snd.Explosion, Rnd(0.9f, 1.1f), evol);
        if (scorches.Count > 40) scorches.RemoveAt(0);

        for (int i = 0; i < 28; i++)
            AddParticle(pos, DirOf(Rnd(0, 2 * MathF.PI)) * Rnd(40, 240), Rnd(0.4f, 0.9f), Rnd(6, 12), 0);
        for (int i = 0; i < 12; i++)
            AddParticle(pos, DirOf(Rnd(0, 2 * MathF.PI)) * Rnd(10, 90), Rnd(0.8f, 1.6f), Rnd(10, 16), 1);

        foreach (var v in vehicles)
        {
            float reach = radius + v.Radius;
            float dist = Vector2.Distance(v.Pos, pos);
            if (dist >= reach || v.State != VState.Alive) continue;
            float f = 1f - dist / reach;
            DamageVehicle(v, damage * f * 1.1f * (v.Kind == VKind.Tank ? 0.7f : 1f), byPlayer);
        }

        foreach (var p in people)
        {
            if (p.Dead) continue;
            float dist = Vector2.Distance(p.Pos, pos);
            if (dist >= radius) continue;
            DamagePerson(p, damage * (1f - dist / radius) * 2f, byPlayer);
        }

        if (heli != null)
        {
            float hd = Vector2.Distance(heli.Pos, pos);
            if (hd < radius + heli.Radius) DamageHeli(damage * (1f - hd / (radius + heli.Radius)), byPlayer);
        }

        float pd = Vector2.Distance(pPos, pos);
        if (deadT <= 0 && pd < radius) HurtPlayer(damage * (1f - pd / radius) * 0.6f);

        AlertCivilians(pos, 450, 4f);
        shake = MathF.Max(shake, MathF.Max(0, 16 - Vector2.Distance(pos, pPos) / 40f));
    }

    // ---------- hledanost, policie, armada

    void UpdateWanted(float dt)
    {
        heat = Math.Clamp(heat, 0, 200);

        int st = Stars;
        if (st > prevStars && (prevStars == 0 || (st >= 4 && prevStars < 4)))
        {
            alertT = 3.5f;
            alertText = st >= 4 ? "POZOR! NASAZENA ARMADA!" : "POZOR! POLICIE JDE ZA TEBOU!";
            SpawnPolice();
            spawnT = Tune.CopSpawnDelay;
        }
        prevStars = st;
        if (alertT > 0) alertT -= dt;

        if (heat > 0)
        {
            float nearest = 99999;
            foreach (var p in people)
                if (!p.Dead && p.Kind != PKind.Civil) nearest = MathF.Min(nearest, Vector2.Distance(p.Pos, pPos));
            foreach (var v in vehicles)
                if (v.State == VState.Alive && v.Kind != VKind.Civil && v.Ai) nearest = MathF.Min(nearest, Vector2.Distance(v.Pos, pPos));
            heat -= (nearest > 700 ? Tune.HeatDecayFar : Tune.HeatDecayNear) * dt;
            if (heat < 0) heat = 0;
        }

        comboT -= dt;
        if (comboT <= 0) combo = 0;

        sirenT -= dt;
        if (Stars > 0 && sirenT <= 0) { sirenT = Rnd(2.0f, 3.2f); Sfx.Play(Snd.Siren, Rnd(0.95f, 1.05f), 0.45f); }

        spawnT -= dt;
        if (spawnT <= 0) { spawnT = Tune.CopSpawnDelay; SpawnPolice(); }

        // policejni zatarasy pri vyssi hledanosti
        if (blockadeCd > 0) blockadeCd -= dt;
        if (Stars >= 3 && blockadeCd <= 0) { blockadeCd = Rnd(14f, 22f); SpawnRoadblock(); }

        // doplnovani civilniho provozu a chodcu
        civT -= dt;
        if (civT <= 0)
        {
            civT = 0.6f;
            int civ = 0, civCars = 0;
            foreach (var p in people) if (!p.Dead && p.Kind == PKind.Civil) civ++;
            foreach (var v in vehicles) if (v.Ai && v.Kind == VKind.Civil && v.State == VState.Alive) civCars++;
            if (civ < 70) SpawnPerson(PKind.Civil, 700, 1400);
            if (civCars < 26) vehicles.Add(SpawnCivil(true, 800));
        }
    }

    void SpawnPolice()
    {
        int s = Stars;
        if (s == 0) return;
        int cops = 0, sold = 0, cars = 0, tanks = 0;
        foreach (var p in people)
        {
            if (p.Dead) continue;
            if (p.Kind == PKind.Cop) cops++;
            else if (p.Kind == PKind.Soldier) sold++;
        }
        foreach (var v in vehicles)
        {
            if (v.State != VState.Alive || !v.Ai) continue;
            if (v.Kind == VKind.Police) cars++;
            else if (v.Kind == VKind.Tank) tanks++;
        }
        if (cops < WantCops[s]) SpawnPerson(PKind.Cop, 450, 800);
        if (sold < WantSoldiers[s]) SpawnPerson(PKind.Soldier, 450, 800);
        if (cars < WantCars[s]) SpawnPoliceVehicle(VKind.Police);
        if (tanks < WantTanks[s]) SpawnPoliceVehicle(VKind.Tank);
        if (s >= 5 && heli == null) heli = new Heli { Pos = pPos + DirOf(Rnd(0, 2 * MathF.PI)) * 760 };
    }

    void SpawnRoadblock()
    {
        var fdir = car != null && MathF.Abs(car.Speed) > 30 ? car.Forward : DirOf(pAngle);
        var target = pPos + fdir * 650;
        int ni = Math.Clamp((int)MathF.Round((target.X - World.Road / 2) / World.Cell), 0, World.Grid);
        int nj = Math.Clamp((int)MathF.Round((target.Y - World.Road / 2) / World.Cell), 0, World.Grid);
        var node = World.Node(ni, nj);
        if (Vector2.Distance(node, pPos) < 320) return;

        var across = new Vector2(-fdir.Y, fdir.X);
        for (int k = -1; k <= 1; k++)
        {
            var v = new Vehicle(VKind.Police, node + across * (k * 36), MathF.Atan2(across.Y, across.X), Pal.Police)
            { Ai = true, Blockade = true };
            vehicles.Add(v);
        }
        Say("Policejni zataras pred tebou!");
    }

    void UpdateHeli(float dt)
    {
        var h = heli;
        if (h == null) return;
        h.Rotor += dt * 28f;
        h.Bob += dt * 3.2f;

        var to = pPos - h.Pos;
        float dist = to.Length();
        var dir = dist > 1 ? to / dist : new Vector2(1, 0);

        if (Stars < 4)   // ztratil zajem - odleti a zmizi
        {
            h.Pos -= dir * 240f * dt;
            if (dist > 1700) heli = null;
            return;
        }

        // drzi odstup ~320 a bocne krouzi kolem hrace
        float radial = dist > 330 ? 220f : dist < 250 ? -140f : 0f;
        var perp = new Vector2(-dir.Y, dir.X);
        h.Pos += dir * radial * dt + perp * 120f * dt;

        h.FireT -= dt;
        if (deadT <= 0 && dist < 540 && h.FireT <= 0)
        {
            h.FireT = 0.55f;
            float a = MathF.Atan2(to.Y, to.X) + Rnd(-0.07f, 0.07f);
            Shoot(h.Pos + DirOf(a) * 20, a, 8, 0.05f, 900, 600, false, false, null);
        }
    }

    void DamageHeli(float dmg, bool byPlayer)
    {
        var h = heli;
        if (h == null || dmg <= 0) return;
        if (byPlayer) h.HitByPlayer = true;
        h.Hp -= dmg;
        if (h.Hp <= 0)
        {
            Explode(h.Pos, 160, 170, h.HitByPlayer);
            if (h.HitByPlayer)
            {
                carsDestroyed++;
                heat += 30;
                DropLoot(h.Pos, 1f); DropLoot(h.Pos, 1f);
                AddMoney(3500, h.Pos);
            }
            heli = null;
        }
    }

    void Cleanup()
    {
        int s = Stars;
        people.RemoveAll(p =>
        {
            float d = Vector2.Distance(p.Pos, pPos);
            if (p.Dead) return p.DeadT > 25f;
            if (p.Kind == PKind.Civil) return d > 2200;
            return d > 1800 || (s == 0 && d > 700);
        });
        vehicles.RemoveAll(v =>
        {
            if (v.PlayerDriven) return false;
            float d = Vector2.Distance(v.Pos, pPos);
            if (v.State == VState.Wreck) return v.WreckAge > 18f;
            return v.Kind != VKind.Civil && v.Ai && v.State == VState.Alive && (d > 1800 || (s == 0 && d > 700));
        });
    }

    // ---------- penize a questy

    void AddMoney(int baseAmt, Vector2 pos)
    {
        combo++;
        comboT = 4f;
        float m = Mult();
        int amt = (int)(baseAmt * m);
        money += amt;
        totalChaos += amt;
        popups.Add(new Popup { Pos = pos, Text = m > 1.01f ? $"+${amt} x{m:0.0}" : $"+${amt}", Life = 1.6f });
    }

    void NewQuest()
    {
        var q = new Quest();
        switch (rnd.Next(4))
        {
            case 0:
                q.Type = QType.DestroyCars; q.Goal = rnd.Next(4, 9); q.Reward = q.Goal * 400;
                break;
            case 1:
                q.Type = QType.KillCops; q.Goal = rnd.Next(3, 6); q.Reward = q.Goal * 700;
                break;
            case 2:
            {
                q.Type = QType.Checkpoint; q.Goal = 1;
                for (int i = 0; i < 40; i++)
                {
                    q.Target = World.Node(rnd.Next(World.Grid + 1), rnd.Next(World.Grid + 1));
                    float dd = Vector2.Distance(q.Target, pPos);
                    if (dd > 700 && dd < 2500) break;
                }
                float dist = Vector2.Distance(q.Target, pPos);
                q.Time = dist / 130f + 18f;
                q.Reward = (int)(1200 + dist * 0.8f);
                break;
            }
            default:
                q.Type = QType.Survive; q.Goal = 25; q.Reward = 4000;
                break;
        }
        quest = q;
    }

    void QuestProgress(QType t)
    {
        if (quest == null || quest.Type != t) return;
        quest.Prog++;
        if (quest.Prog >= quest.Goal) CompleteQuest();
    }

    void CompleteQuest()
    {
        if (quest == null) return;
        money += quest.Reward;
        totalChaos += quest.Reward;
        popups.Add(new Popup { Pos = pPos, Text = $"QUEST +${quest.Reward}", Life = 2.2f });
        Say($"Quest splnen! +${quest.Reward}");
        Sfx.Play(Snd.Quest);
        SaveGame();
        NewQuest();
    }

    void UpdateQuest(float dt)
    {
        var q = quest;
        if (q == null || deadT > 0) return;

        if (q.Type == QType.Checkpoint)
        {
            q.Time -= dt;
            if (car != null && Vector2.Distance(car.Pos, q.Target) < 80) CompleteQuest();
            else if (q.Time <= 0) { Say("Quest nesplnen - vyprsel cas."); NewQuest(); }
        }
        else if (q.Type == QType.Survive)
        {
            if (Stars >= 3)
            {
                q.Prog += dt;
                if (q.Prog >= q.Goal) CompleteQuest();
            }
            else q.Prog = 0;
        }
    }

    string QuestText(Quest q) => q.Type switch
    {
        QType.DestroyCars => $"Znic auta: {(int)q.Prog}/{q.Goal}",
        QType.KillCops => $"Zabij policisty/vojaky: {(int)q.Prog}/{q.Goal}",
        QType.Checkpoint => $"Dojed autem na zluty bod: {Math.Max(0, (int)q.Time)}s",
        _ => $"Preziji na 3+ hvezdach: {(int)q.Prog}/{q.Goal}s",
    };

    // ---------- pickupy a lakovna

    void DropLoot(Vector2 pos, float chance)
    {
        if (pickups.Count > 30 || Rnd() > chance) return;
        float r = Rnd();
        int kind = r < 0.42f ? 0 : r < 0.82f ? 1 : 2;   // 0 zdravi, 1 penize, 2 brneni
        pickups.Add(new Pickup { Pos = pos + new Vector2(Rnd(-12, 12), Rnd(-12, 12)), Kind = kind });
    }

    void UpdatePickups(float dt)
    {
        if (sprayCd > 0) sprayCd -= dt;
        if (deadT > 0) return;

        foreach (var k in pickups)
        {
            k.Life -= dt;
            if (Vector2.Distance(k.Pos, pPos) > 26) continue;
            k.Life = 0;
            if (k.Kind == 0)
            {
                pHp = MathF.Min(100, pHp + 35);
                if (car != null) car.Hp = MathF.Min(car.MaxHp, car.Hp + 30);
                popups.Add(new Popup { Pos = pPos, Text = "+ZDRAVI", Life = 1.2f });
                Sfx.Play(Snd.Pickup);
            }
            else if (k.Kind == 2)
            {
                pArmor = MathF.Min(100, pArmor + 50);
                popups.Add(new Popup { Pos = pPos, Text = "+BRNENI", Life = 1.2f });
                Sfx.Play(Snd.Pickup);
            }
            else
            {
                money += 100; totalChaos += 100;
                popups.Add(new Popup { Pos = pPos, Text = "+$100", Life = 1.2f });
                Sfx.Play(Snd.Coin);
            }
        }
        pickups.RemoveAll(k => k.Life <= 0);

        // lakovna: zajed autem k budove, chvili se lakuje a opravuje
        if (sprayT > 0)
        {
            if (car == null || car.State != VState.Alive || Vector2.Distance(car.Pos, spray) > 100)
            {
                sprayT = 0; money += SprayPrice;
                Say($"Lakovani prerusene - vraceno ${SprayPrice}");
            }
            else
            {
                sprayT -= dt;
                if (sprayT <= 0)
                {
                    heat = 0;
                    car.Hp = car.MaxHp;
                    if (car.Kind == VKind.Civil) car.Body = Pal.Cars[rnd.Next(Pal.Cars.Length)];
                    Say("Lakovna: auto opraveno, hvezdicky smazany");
                }
            }
        }
        else if (car != null && car.State == VState.Alive && sprayCd <= 0 &&
                 Vector2.Distance(car.Pos, spray) < 70 && (heat > 0 || car.Hp < car.MaxHp - 1))
        {
            sprayCd = 3f;
            if (money >= SprayPrice) { money -= SprayPrice; sprayT = 1.6f; }
            else Say($"Lakovna potrebuje ${SprayPrice}.");
        }
    }

    // ---------- banka

    bool NearDoor(Vector2 door) => car == null && deadT <= 0 && Vector2.Distance(pPos, door) < 90;

    void UseHospital()
    {
        if (pHp >= 100) { Say("Jsi plne zdravy."); return; }
        if (money < HospitalFee) { Say($"Nemocnice stoji ${HospitalFee}."); return; }
        money -= HospitalFee;
        pHp = 100;
        popups.Add(new Popup { Pos = pPos, Text = "+PLNE ZDRAVI", Life = 1.4f });
        Say($"Nemocnice te vylecila za ${HospitalFee}.");
    }

    void UseSafehouse()
    {
        heat = 0; combo = 0;
        SaveGame();
        popups.Add(new Popup { Pos = pPos, Text = "ULOZENO", Life = 1.6f });
        Say($"Ukryt: hvezdicky smazany, hra ulozena (rekord ${bestChaos}).");
    }

    bool CanRobBank() =>
        !robbing && bankCd <= 0 && deadT <= 0 && Vector2.Distance(pPos, world.BankDoor) < 90;

    void StartRobbery()
    {
        robbing = true;
        robT = 0;
        heat = MathF.Max(heat, 100f);
        AlertCivilians(world.BankDoor, 500, 6f);
        Say($"Vykradas banku! Zustan u dveri {RobTime:0}s.");
    }

    void UpdateBank(float dt)
    {
        if (bankCd > 0) bankCd -= dt;
        if (!robbing) return;

        float d = Vector2.Distance(pPos, world.BankDoor);
        if (deadT > 0 || car != null || d > 130)
        {
            robbing = false;
            bankCd = 15f;
            Say("Loupez se nezdaril!");
            return;
        }

        heat = MathF.Max(heat, 100f);    // alarm drzi 3 hvezdy
        robT += dt;
        if (robT >= RobTime)
        {
            robbing = false;
            bankCd = 120f;
            int loot = rnd.Next(4000, 6001);
            money += loot; totalChaos += loot;
            popups.Add(new Popup { Pos = pPos, Text = $"BANKA +${loot}", Life = 2.5f });
            Say($"Banka vykradena! +${loot} - utikej!");
            Sfx.Play(Snd.Bank);
            SaveGame();
            heat = MathF.Max(heat, 110f);
        }
    }

    // ---------- efekty, kamera

    void AddParticle(Vector2 pos, Vector2 vel, float life, float size, int kind)
    {
        if (particles.Count > 700) return;
        particles.Add(new Particle { Pos = pos, Vel = vel, Life = life, MaxLife = life, Size = size, Kind = kind });
    }

    void UpdateEffects(float dt)
    {
        foreach (var p in particles)
        {
            p.Pos += p.Vel * dt;
            p.Vel *= MathF.Max(0, 1f - 2f * dt);
            p.Life -= dt;
        }
        particles.RemoveAll(p => p.Life <= 0);

        foreach (var sc in scorches) sc.Age += dt;
        scorches.RemoveAll(sc => sc.Age > 10f);

        foreach (var m in muzzles) m.T -= dt;
        muzzles.RemoveAll(m => m.T <= 0);

        foreach (var tm in tireMarks) tm.Age += dt;
        tireMarks.RemoveAll(tm => tm.Age > 8f);

        foreach (var e in explosions) e.T += dt;
        explosions.RemoveAll(e => e.T > Explosion.Dur);

        foreach (var p in popups) p.Life -= dt;
        popups.RemoveAll(p => p.Life <= 0);

        shake = MathF.Max(0, shake - 30f * dt);
    }

    void UpdateCamera(float dt)
    {
        float tz = 1f;
        if (car != null) tz = 0.95f - MathF.Min(MathF.Abs(car.Speed) / car.MaxSpeed, 1f) * 0.3f;
        cam.Zoom += (tz - cam.Zoom) * MathF.Min(1f, 2f * dt);
        cam.Offset = new Vector2(W / 2f, H / 2f);
        cam.Target = pPos + new Vector2(Rnd(-0.5f, 0.5f) * shake, Rnd(-0.5f, 0.5f) * shake);
        cam.Rotation = 0;
    }

    // ------------------------------------------------------------ menu

    string[] CurItems => scr == Scr.Menu ? MenuItems : PauseItems;
    Rectangle ItemRect(int i) => new(W / 2 - 170, (scr == Scr.Menu ? 330 : 280) + i * 68, 340, 54);

    void UpdateMenu()
    {
        if (scr == Scr.Help)
        {
            if (Raylib.IsKeyPressed(Keys.Escape) || Raylib.IsKeyPressed(Keys.Enter) || Raylib.IsKeyPressed(Keys.Space) ||
                Raylib.IsKeyPressed(Keys.P) || Raylib.IsMouseButtonPressed(Keys.MouseLeft))
                scr = helpBack;
            return;
        }

        if (scr == Scr.Menu)    // kamera pomalu krouzi nad mestem
        {
            cam.Offset = new Vector2(W / 2f, H / 2f);
            cam.Target = pPos + new Vector2(MathF.Cos(animT * 0.15f) * 420, MathF.Sin(animT * 0.2f) * 260);
            cam.Zoom = 0.85f;
        }
        else if (Raylib.IsKeyPressed(Keys.Escape) || Raylib.IsKeyPressed(Keys.P)) { scr = Scr.Play; return; }

        int n = CurItems.Length;
        if (Raylib.IsKeyPressed(Keys.Down) || Raylib.IsKeyPressed(Keys.S)) sel = (sel + 1) % n;
        if (Raylib.IsKeyPressed(Keys.Up) || Raylib.IsKeyPressed(Keys.W)) sel = (sel + n - 1) % n;

        var mp = Raylib.GetMousePosition();
        bool click = Raylib.IsMouseButtonPressed(Keys.MouseLeft);
        bool go = Raylib.IsKeyPressed(Keys.Enter) || Raylib.IsKeyPressed(Keys.Space);
        for (int i = 0; i < n; i++)
            if (Raylib.CheckCollisionPointRec(mp, ItemRect(i))) { sel = i; if (click) go = true; }
        if (go) Activate(sel);
    }

    void Activate(int i)
    {
        Sfx.Play(Snd.Click);
        if (i == 0) { scr = Scr.Play; fireT = 0.3f; return; }
        if (i == 1) { helpBack = scr; scr = Scr.Help; return; }
        if (scr == Scr.Menu) Quit = true; else WantRestart = true;
    }

    // ------------------------------------------------------------ kresleni

    bool InView(Vector2 p) => p.X > vx0 && p.X < vx1 && p.Y > vy0 && p.Y < vy1;

    static void Txt(string s, int x, int y, int size, Color c)
    {
        Raylib.DrawText(s, x + 2, y + 2, size, Raylib.Fade(Pal.Black, 0.7f));
        Raylib.DrawText(s, x, y, size, c);
    }

    public void Draw()
    {
        Raylib.ClearBackground(Pal.Grass);
        Raylib.BeginMode2D(cam);

        float hw = W / 2f / cam.Zoom + 120, hh = H / 2f / cam.Zoom + 120;
        vx0 = cam.Target.X - hw; vx1 = cam.Target.X + hw;
        vy0 = cam.Target.Y - hh; vy1 = cam.Target.Y + hh;

        world.Night = Night;
        world.Draw(vx0, vy0, vx1, vy1);

        // nocni ztmaveni zeme a budov + pouliclni lampy
        float night = Night;
        if (night > 0.04f)
        {
            Raylib.DrawRectangle((int)vx0, (int)vy0, (int)(vx1 - vx0), (int)(vy1 - vy0),
                Raylib.Fade(new Color(8, 12, 38, 255), night * 0.62f));

            var lamp = new Color(255, 220, 150, 255);
            int i0 = Math.Max(0, (int)(vx0 / World.Cell)), i1 = Math.Min(World.Grid, (int)(vx1 / World.Cell) + 1);
            int j0 = Math.Max(0, (int)(vy0 / World.Cell)), j1 = Math.Min(World.Grid, (int)(vy1 / World.Cell) + 1);
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    var n = World.Node(i, j);
                    Raylib.DrawCircleV(n, 70, Raylib.Fade(lamp, night * 0.16f));
                    Raylib.DrawCircleV(n, 6, Raylib.Fade(lamp, night * 0.9f));
                }
        }

        foreach (var tm in tireMarks)
        {
            if (!InView(tm.Pos)) continue;
            Raylib.DrawCircleV(tm.Pos, 3.2f, Raylib.Fade(Pal.Black, 0.30f * (1f - tm.Age / 8f)));
        }

        foreach (var p in people)
            if (p.Dead && InView(p.Pos)) DrawCorpse(p);
        if (deadT > 0) Raylib.DrawCircleV(pPos, 13, Pal.Blood);

        foreach (var sc in scorches)
        {
            if (!InView(sc.Pos)) continue;
            float sa = Math.Clamp((10f - sc.Age) / 3f, 0f, 1f);
            Raylib.DrawCircleV(sc.Pos, sc.R, Raylib.Fade(Pal.Black, 0.30f * sa));
            Raylib.DrawCircleV(sc.Pos, sc.R * 0.6f, Raylib.Fade(Pal.Black, 0.25f * sa));
        }

        // lakovna
        if (InView(spray))
        {
            var pink = new Color(255, 90, 180, 255);
            float pu = 1f + MathF.Sin(animT * 3f) * 0.05f;
            Raylib.DrawCircleV(spray, 62, Raylib.Fade(pink, 0.30f));
            Raylib.DrawCircleLines((int)spray.X, (int)spray.Y, 62 * pu, pink);
            int tw = Raylib.MeasureText("LAKOVNA", 22);
            Raylib.DrawText("LAKOVNA", (int)spray.X - tw / 2, (int)spray.Y - 22, 22, Pal.White);
            string sub = $"${SprayPrice} = bez hvezdicek";
            int sw = Raylib.MeasureText(sub, 14);
            Raylib.DrawText(sub, (int)spray.X - sw / 2, (int)spray.Y + 6, 14, Pal.White);
        }

        var bd = world.BankDoor;
        if (InView(bd))
        {
            bool ready = bankCd <= 0 && !robbing;
            Color bc = ready ? new Color(240, 200, 60, 255) : new Color(130, 130, 130, 255);
            float bp = 1f + MathF.Sin(animT * 4f) * 0.06f;
            Raylib.DrawCircleV(bd, 55, Raylib.Fade(bc, 0.25f));
            Raylib.DrawCircleLines((int)bd.X, (int)bd.Y, 55 * bp, bc);
            Raylib.DrawText("$", (int)bd.X - 7, (int)bd.Y - 16, 34, bc);
        }

        foreach (var v in vehicles)
            if (v.State == VState.Wreck && InView(v.Pos)) DrawVehicle(v);
        foreach (var v in vehicles)
            if (v.State != VState.Wreck && InView(v.Pos)) DrawVehicle(v);

        foreach (var k in pickups)
        {
            if (!InView(k.Pos) || (k.Life < 4f && ((int)(animT * 8)) % 2 == 0)) continue;
            float bob = 1f + MathF.Sin(animT * 6f + k.Pos.X) * 0.1f;
            int kx = (int)k.Pos.X, ky = (int)k.Pos.Y;
            Raylib.DrawCircleV(k.Pos + new Vector2(2, 3), 11 * bob, Raylib.Fade(Pal.Black, 0.3f));
            if (k.Kind == 0)
            {
                Raylib.DrawCircleV(k.Pos, 11 * bob, Pal.White);
                Raylib.DrawRectangle(kx - 6, ky - 2, 12, 4, Pal.Red);
                Raylib.DrawRectangle(kx - 2, ky - 6, 4, 12, Pal.Red);
            }
            else if (k.Kind == 2)
            {
                Raylib.DrawCircleV(k.Pos, 11 * bob, new Color(70, 120, 230, 255));
                Raylib.DrawCircleLines(kx, ky, 11 * bob, Pal.White);
                Raylib.DrawText("+", kx - 5, ky - 9, 20, Pal.White);
            }
            else
            {
                Raylib.DrawCircleV(k.Pos, 11 * bob, new Color(60, 170, 80, 255));
                Raylib.DrawCircleLines(kx, ky, 11 * bob, Pal.Black);
                Raylib.DrawText("$", kx - 4, ky - 8, 18, Pal.White);
            }
        }

        foreach (var p in people)
            if (!p.Dead && InView(p.Pos)) DrawPerson(p);

        if (car == null && deadT <= 0)
        {
            var fwd = DirOf(pAngle);
            var side = new Vector2(-fwd.Y, fwd.X);
            float swing = MathF.Sin(pWalk) * 3.6f;
            var jacket = new Color(210, 150, 40, 255);
            var skin = new Color(238, 198, 158, 255);

            Raylib.DrawCircleV(pPos + new Vector2(2, 3), 9f, Raylib.Fade(Pal.Black, 0.32f));

            // nohy
            var legMov = side * 3.4f + fwd * swing;
            Raylib.DrawCircleV(pPos + legMov, 3.1f, new Color(45, 45, 60, 255));
            Raylib.DrawCircleV(pPos - legMov, 3.1f, new Color(45, 45, 60, 255));

            // telo
            Raylib.DrawCircleV(pPos, 8.2f, jacket);
            Raylib.DrawCircleLines((int)pPos.X, (int)pPos.Y, 8.2f, Raylib.Fade(Pal.Black, 0.6f));

            if (holstered)
            {
                // ruce u tela (kyvaji se pri chuzi), zbran schovana na zadech
                Raylib.DrawCircleV(pPos + side * 7 - fwd * swing * 0.6f, 2.6f, skin);
                Raylib.DrawCircleV(pPos - side * 7 + fwd * swing * 0.6f, 2.6f, skin);
                if (weaponIdx != 0)
                    Raylib.DrawLineEx(pPos - fwd * 2 - side * 5, pPos - fwd * 2 + side * 5, 3, Pal.Dark);
            }
            else
            {
                // ruce drzi zbran ve smeru mireni (lehky zpetny raz hned po vystrelu)
                float kick = fireT > 0 && fireT > Weapons.All[weaponIdx].Cooldown - 0.06f ? -3f : 0f;
                var gunBase = pPos + fwd * (7 + kick);
                Raylib.DrawCircleV(gunBase + side * 3, 2.6f, skin);
                Raylib.DrawCircleV(gunBase - side * 3, 2.6f, skin);
                Raylib.DrawLineEx(gunBase, pPos + fwd * (18 + kick), 4, Pal.Dark);
            }

            // hlava
            Raylib.DrawCircleV(pPos + fwd * 1.5f, 4.8f, skin);
        }

        foreach (var b in bullets)
        {
            if (!InView(b.Pos)) continue;
            if (b.Explosive) Raylib.DrawCircleV(b.Pos, 5, Pal.Fire);
            else
            {
                var n = Vector2.Normalize(b.Vel);
                Raylib.DrawLineEx(b.Pos - n * 9, b.Pos, 2.5f, b.Player ? Pal.Yellow : new Color(255, 120, 120, 255));
            }
        }

        foreach (var m in muzzles)
        {
            if (!InView(m.Pos)) continue;
            float mt = m.T / 0.06f;
            var fdir = DirOf(m.Ang);
            var fside = new Vector2(-fdir.Y, fdir.X);
            var tip = m.Pos + fdir * (10 + mt * 6);
            // hvezdicovity zableskovy kuzel
            Raylib.DrawTriangle(m.Pos + fside * 4f, m.Pos - fside * 4f, tip, Raylib.Fade(Pal.Yellow, mt));
            Raylib.DrawCircleV(m.Pos, 4f * mt, Raylib.Fade(Pal.White, mt));
            Raylib.DrawCircleV(tip, 2.5f * mt, Raylib.Fade(Pal.Fire, mt));
        }

        foreach (var e in explosions)
        {
            float t = e.T / Explosion.Dur;
            float r = e.Radius * MathF.Min(1f, t * 3f);
            Raylib.DrawCircleV(e.Pos, r, Raylib.Fade(Pal.Fire, (1f - t) * 0.85f));
            Raylib.DrawCircleV(e.Pos, r * 0.6f, Raylib.Fade(Pal.Yellow, (1f - t) * 0.9f));
            Raylib.DrawCircleV(e.Pos, r * 0.25f, Raylib.Fade(Pal.White, (1f - t)));
            // rozpinajici se razova vlna
            float ring = e.Radius * (0.4f + t * 1.4f);
            Raylib.DrawCircleLines((int)e.Pos.X, (int)e.Pos.Y, ring, Raylib.Fade(Pal.White, (1f - t) * 0.5f));
        }

        foreach (var p in particles)
        {
            if (!InView(p.Pos)) continue;
            float t = p.Life / p.MaxLife;
            if (p.Kind == 0) Raylib.DrawCircleV(p.Pos, p.Size * (0.4f + t * 0.6f), Raylib.Fade(Pal.Fire, t));
            else if (p.Kind == 1) Raylib.DrawCircleV(p.Pos, p.Size * (1.8f - t * 0.8f), Raylib.Fade(Pal.Smoke, t * 0.6f));
            else Raylib.DrawCircleV(p.Pos, p.Size * 0.5f, Raylib.Fade(Pal.Yellow, t));
        }

        if (heli != null && InView(heli.Pos)) DrawHeli(heli);

        if (quest != null && quest.Type == QType.Checkpoint)
        {
            float pulse = 1f + MathF.Sin(animT * 5f) * 0.12f;
            Raylib.DrawCircleV(quest.Target, 70 * pulse, Raylib.Fade(Pal.Yellow, 0.35f));
            Raylib.DrawCircleLines((int)quest.Target.X, (int)quest.Target.Y, 70 * pulse, Pal.Yellow);
        }

        foreach (var p in popups)
            Txt(p.Text, (int)p.Pos.X - 30, (int)(p.Pos.Y - 20 - (1.6f - p.Life) * 40), 22, Pal.Yellow);

        Raylib.EndMode2D();

        DrawVignette();
        if (scr != Scr.Menu) DrawHud();
        if (shop && scr == Scr.Play) DrawShop();
        if (mapOpen && scr == Scr.Play) DrawBigMap();
        if (scr != Scr.Play) DrawMenu();
    }

    void DrawVehicle(Vehicle v)
    {
        float deg = v.Angle * RadToDeg;
        var fwd = v.Forward;
        var right = new Vector2(-fwd.Y, fwd.X);
        var origin = new Vector2(v.Length / 2, v.Width / 2);
        bool wreck = v.State == VState.Wreck;
        float wa = wreck ? Math.Clamp((18f - v.WreckAge) / 4f, 0f, 1f) : 1f;
        Color F(Color c) => wreck ? Raylib.Fade(c, wa) : c;

        Raylib.DrawRectanglePro(new Rectangle(v.Pos.X + 3, v.Pos.Y + 3, v.Length, v.Width), origin, deg, Raylib.Fade(Pal.Black, 0.3f * wa));

        // kola (cerne obdelniky trcici zpod karoserie) - jen auta, ne tank
        if (v.Kind != VKind.Tank)
        {
            var tyre = F(new Color(24, 24, 28, 255));
            float wl = v.Length * 0.22f, ww = 6f;
            foreach (float fx in new[] { 0.3f, -0.26f })
                foreach (float sy in new[] { 1f, -1f })
                {
                    var wp = v.Pos + fwd * (v.Length * fx) + right * (sy * (v.Width / 2 + 1));
                    Raylib.DrawRectanglePro(new Rectangle(wp.X, wp.Y, wl, ww), new Vector2(wl / 2, ww / 2), deg, tyre);
                }
        }

        if (v.Kind == VKind.Tank)
        {
            Color body = wreck ? F(Pal.Wreck) : v.Body;
            foreach (float side in new[] { -1f, 1f })
            {
                var tc = v.Pos + right * side * (v.Width / 2 - 4);
                Raylib.DrawRectanglePro(new Rectangle(tc.X, tc.Y, v.Length, 10), new Vector2(v.Length / 2, 5), deg, F(Pal.Dark));
            }
            Raylib.DrawRectanglePro(new Rectangle(v.Pos.X, v.Pos.Y, v.Length - 6, v.Width - 10), new Vector2((v.Length - 6) / 2, (v.Width - 10) / 2), deg, body);
            Raylib.DrawRectanglePro(new Rectangle(v.Pos.X, v.Pos.Y, 44, 7), new Vector2(0, 3.5f), v.TurretAngle * RadToDeg, F(Pal.Dark));
            Raylib.DrawCircleV(v.Pos, 13, wreck ? F(Pal.Dark) : new Color(64, 84, 46, 255));
            if (v.State == VState.Burning) Raylib.DrawCircleV(v.Pos, 20, Raylib.Fade(Pal.Black, 0.4f));
            return;
        }

        Raylib.DrawRectanglePro(new Rectangle(v.Pos.X, v.Pos.Y, v.Length, v.Width), origin, deg, wreck ? F(Pal.Wreck) : v.Body);
        if (wreck)
        {
            Raylib.DrawRectanglePro(new Rectangle(v.Pos.X, v.Pos.Y, v.Length * 0.6f, v.Width * 0.6f),
                new Vector2(v.Length * 0.3f, v.Width * 0.3f), deg, Raylib.Fade(Pal.Black, 0.6f * wa));
            return;
        }

        var wc = v.Pos + fwd * (v.Length * 0.14f);
        Raylib.DrawRectanglePro(new Rectangle(wc.X, wc.Y, v.Length * 0.26f, v.Width * 0.78f),
            new Vector2(v.Length * 0.13f, v.Width * 0.39f), deg, Pal.Glass);
        var rc = v.Pos - fwd * (v.Length * 0.27f);
        Raylib.DrawRectanglePro(new Rectangle(rc.X, rc.Y, v.Length * 0.16f, v.Width * 0.7f),
            new Vector2(v.Length * 0.08f, v.Width * 0.35f), deg, Pal.Glass);

        var roof = v.Pos - fwd * (v.Length * 0.09f);
        Raylib.DrawRectanglePro(new Rectangle(roof.X, roof.Y, v.Length * 0.2f, v.Width * 0.7f),
            new Vector2(v.Length * 0.1f, v.Width * 0.35f), deg, Raylib.Fade(Pal.White, 0.14f));

        if (v.Kind != VKind.Tank)
        {
            float beam = 0.06f + Night * 0.5f;
            var nose = v.Pos + fwd * (v.Length / 2);
            var far = nose + fwd * (90 + Night * 70);
            Raylib.DrawTriangle(nose + right * 7, nose - right * 7, far + right * 34, Raylib.Fade(Pal.Yellow, beam * 0.5f));
            Raylib.DrawTriangle(nose + right * 7, far + right * 34, far - right * 34, Raylib.Fade(Pal.Yellow, beam * 0.5f));
            Raylib.DrawCircleV(nose + fwd * 16, 16, Raylib.Fade(Pal.Yellow, beam * 0.6f));
        }
        var tl = v.Pos - fwd * (v.Length / 2 - 2);
        Color tail = v.Speed < -5 ? Pal.White : new Color(190, 30, 30, 255);
        Raylib.DrawCircleV(tl + right * (v.Width / 2 - 4), 2.2f, tail);
        Raylib.DrawCircleV(tl - right * (v.Width / 2 - 4), 2.2f, tail);

        var hl = v.Pos + fwd * (v.Length / 2 - 2);
        Raylib.DrawCircleV(hl + right * (v.Width / 2 - 4), 2.5f, Pal.Yellow);
        Raylib.DrawCircleV(hl - right * (v.Width / 2 - 4), 2.5f, Pal.Yellow);

        if (v.Kind == VKind.Civil)
        {
            if (v.Model == VModel.Ambulance)
            {
                // cerveny kriz na strese + modre blikajici svetlo
                Raylib.DrawRectanglePro(new Rectangle(v.Pos.X, v.Pos.Y, 14, 4), new Vector2(7, 2), deg, Pal.Red);
                Raylib.DrawRectanglePro(new Rectangle(v.Pos.X, v.Pos.Y, 4, 14), new Vector2(2, 7), deg, Pal.Red);
                bool amb = ((int)(animT * 7)) % 2 == 0;
                Raylib.DrawCircleV(v.Pos + fwd * (v.Length * 0.32f), 4, amb ? Pal.Red : Pal.Blue);
            }
            else if (v.Model == VModel.Bus)
            {
                // radky bocnich oken
                for (float k = -0.32f; k <= 0.32f; k += 0.16f)
                {
                    var wp = v.Pos + fwd * (v.Length * k);
                    Raylib.DrawRectanglePro(new Rectangle(wp.X, wp.Y, v.Length * 0.1f, v.Width * 0.82f),
                        new Vector2(v.Length * 0.05f, v.Width * 0.41f), deg, Pal.Glass);
                }
            }
            else if (v.Model == VModel.Bike)
            {
                Raylib.DrawCircleV(v.Pos, 5.5f, new Color(40, 40, 50, 255));          // ridic
                Raylib.DrawCircleV(v.Pos + fwd * 2, 3.2f, new Color(230, 200, 160, 255)); // helma
            }
        }

        if (v.Kind == VKind.Police)
        {
            Raylib.DrawRectanglePro(new Rectangle(v.Pos.X, v.Pos.Y, v.Length * 0.9f, 4), new Vector2(v.Length * 0.45f, 2), deg, Pal.Blue);
            bool b = ((int)(animT * 8)) % 2 == 0;
            Raylib.DrawCircleV(v.Pos + right * 5, 14, Raylib.Fade(b ? Pal.Red : Pal.Blue, 0.18f));
            Raylib.DrawCircleV(v.Pos - right * 5, 14, Raylib.Fade(b ? Pal.Blue : Pal.Red, 0.18f));
            Raylib.DrawCircleV(v.Pos + right * 5, 4.5f, b ? Pal.Red : Pal.Blue);
            Raylib.DrawCircleV(v.Pos - right * 5, 4.5f, b ? Pal.Blue : Pal.Red);
        }

        if (v.State == VState.Burning)
            Raylib.DrawRectanglePro(new Rectangle(v.Pos.X, v.Pos.Y, v.Length, v.Width), origin, deg, Raylib.Fade(Pal.Black, 0.45f));
    }

    void DrawHeli(Heli h)
    {
        float bob = MathF.Sin(h.Bob) * 4f;
        var to = pPos - h.Pos;
        float deg = MathF.Atan2(to.Y, to.X) * RadToDeg;
        var body = h.Pos + new Vector2(0, bob);

        // stin na zemi (vrtulnik leti vysoko)
        Raylib.DrawCircleV(h.Pos + new Vector2(20, 38), 18, Raylib.Fade(Pal.Black, 0.26f));

        // ocasni nosnik + trup
        Raylib.DrawRectanglePro(new Rectangle(body.X, body.Y, 52, 8), new Vector2(40, 4), deg, new Color(36, 42, 50, 255));
        Raylib.DrawRectanglePro(new Rectangle(body.X, body.Y, 34, 22), new Vector2(17, 11), deg, new Color(48, 56, 66, 255));
        // kokpit
        var fwd = DirOf(deg / RadToDeg);
        Raylib.DrawCircleV(body + fwd * 9, 6, Pal.Glass);
        // policejni svetlo
        bool bl = ((int)(animT * 8)) % 2 == 0;
        Raylib.DrawCircleV(body, 3.5f, bl ? Pal.Red : Pal.Blue);

        // rotujici hlavni rotor
        for (int i = 0; i < 2; i++)
        {
            float a = h.Rotor + i * MathF.PI / 2;
            var d = DirOf(a);
            Raylib.DrawLineEx(body - d * 34, body + d * 34, 3, Raylib.Fade(new Color(20, 20, 24, 255), 0.85f));
        }
        Raylib.DrawCircleV(body, 3, new Color(20, 20, 24, 255));

        // HP lista kdyz poskozeny
        if (h.Hp < h.MaxHp)
        {
            Raylib.DrawRectangle((int)body.X - 22, (int)body.Y - 34, 44, 5, new Color(20, 20, 26, 255));
            Raylib.DrawRectangle((int)body.X - 21, (int)body.Y - 33, (int)(42 * Math.Clamp(h.Hp / h.MaxHp, 0, 1)), 3, Pal.Red);
        }
    }

    void DrawPerson(Person p)
    {
        var fwd = DirOf(p.Angle);
        var side = new Vector2(-fwd.Y, fwd.X);
        bool armed = p.Kind != PKind.Civil;
        float swing = MathF.Sin(p.AnimWalk) * 3.4f;   // vykyv nohou/rukou

        // stin
        Raylib.DrawCircleV(p.Pos + new Vector2(2, 3), 8.5f, Raylib.Fade(Pal.Black, 0.3f));

        // nohy (kracejici - predkrok/zakrok podle faze)
        Color pants = p.Kind == PKind.Soldier ? new Color(58, 70, 40, 255)
                    : p.Kind == PKind.Cop ? new Color(28, 38, 86, 255)
                    : new Color(45, 45, 60, 255);
        var legL = p.Pos + side * 3.2f + fwd * swing;
        var legR = p.Pos - side * 3.2f - fwd * swing;
        Raylib.DrawCircleV(legL, 2.9f, pants);
        Raylib.DrawCircleV(legR, 2.9f, pants);

        // telo
        Raylib.DrawCircleV(p.Pos, 7.6f, p.Col);
        Raylib.DrawCircleLines((int)p.Pos.X, (int)p.Pos.Y, 7.6f, Raylib.Fade(Pal.Black, 0.55f));

        Color skin = new Color(238, 198, 158, 255);
        if (armed)
        {
            // obe ruce drzi zbran pred telem
            var gunBase = p.Pos + fwd * 6;
            var handL = gunBase + side * 3;
            var handR = gunBase - side * 3;
            Raylib.DrawCircleV(handL, 2.4f, skin);
            Raylib.DrawCircleV(handR, 2.4f, skin);
            Color gun = p.Kind == PKind.Soldier ? new Color(35, 40, 30, 255) : new Color(30, 30, 34, 255);
            Raylib.DrawLineEx(gunBase, p.Pos + fwd * (p.Kind == PKind.Soldier ? 17 : 14), 3.2f, gun);
        }
        else
        {
            // ruce svisaji a kyvaji se opacne nez nohy
            Raylib.DrawCircleV(p.Pos + side * 6 - fwd * swing * 0.7f, 2.4f, p.Col);
            Raylib.DrawCircleV(p.Pos - side * 6 + fwd * swing * 0.7f, 2.4f, p.Col);
        }

        // hlava
        var hp = p.Pos + fwd * 1.5f;
        Raylib.DrawCircleV(hp, 4.6f, skin);

        // pokryvka hlavy
        if (p.Kind == PKind.Cop)
        {
            Raylib.DrawCircleV(hp, 4.6f, new Color(24, 34, 110, 255));
            Raylib.DrawCircleV(hp + fwd * 3, 2.2f, new Color(20, 28, 90, 255));   // ksilt
        }
        else if (p.Kind == PKind.Soldier)
        {
            Raylib.DrawCircleV(hp, 4.8f, new Color(52, 66, 34, 255));            // helma
            Raylib.DrawCircleLines((int)hp.X, (int)hp.Y, 4.8f, Raylib.Fade(Pal.Black, 0.5f));
        }
    }

    void DrawCorpse(Person p)
    {
        float fade = MathF.Max(0.25f, 1f - p.DeadT / 25f);
        var fwd = DirOf(p.DeathAng);
        var side = new Vector2(-fwd.Y, fwd.X);

        // rozlevajici se kaluz krve
        float pool = MathF.Min(14f, 7f + p.DeadT * 2.2f);
        Raylib.DrawCircleV(p.Pos, pool, Raylib.Fade(Pal.Blood, 0.5f * fade));
        Raylib.DrawCircleV(p.Pos + side * 4, pool * 0.6f, Raylib.Fade(Pal.Blood, 0.4f * fade));

        // lezici telo (protahle podel smeru padu)
        Color body = p.Kind == PKind.Cop ? new Color(40, 60, 170, 255)
                   : p.Kind == PKind.Soldier ? new Color(70, 88, 46, 255)
                   : p.Col;
        for (int i = -1; i <= 1; i++)
            Raylib.DrawCircleV(p.Pos + fwd * (i * 4.5f), i == 1 ? 3.6f : 5.2f, Raylib.Fade(body, fade));
    }

    void DrawVignette()
    {
        var dark = Raylib.Fade(Pal.Black, 0.38f);
        var clear = Raylib.Fade(Pal.Black, 0f);
        int e = 160;
        Raylib.DrawRectangleGradientV(0, 0, W, e, dark, clear);
        Raylib.DrawRectangleGradientV(0, H - e, W, e, clear, dark);
        Raylib.DrawRectangleGradientH(0, 0, e, H, dark, clear);
        Raylib.DrawRectangleGradientH(W - e, 0, e, H, clear, dark);

        if (Stars >= 2)    // blikani policejnich svetel na okrajich obrazovky
        {
            bool f = ((int)(animT * 4)) % 2 == 0;
            Raylib.DrawRectangleGradientH(0, 0, 120, H, Raylib.Fade(f ? Pal.Red : Pal.Blue, 0.18f), clear);
            Raylib.DrawRectangleGradientH(W - 120, 0, 120, H, clear, Raylib.Fade(f ? Pal.Blue : Pal.Red, 0.18f));
        }

        if (lastHurtT < 0.3f && deadT <= 0)
            Raylib.DrawRectangle(0, 0, W, H, Raylib.Fade(Pal.Red, 0.3f - lastHurtT));
    }

    static void Panel(Rectangle r, Color fill) => Raylib.DrawRectangleRounded(r, 0.12f, 8, fill);

    static void Bar(int x, int y, int w, int h, float frac, Color col)
    {
        frac = Math.Clamp(frac, 0f, 1f);
        Raylib.DrawRectangleRounded(new Rectangle(x, y, w, h), 0.5f, 6, new Color(20, 20, 26, 255));
        if (frac > 0.02f)
            Raylib.DrawRectangleRounded(new Rectangle(x + 2, y + 2, (w - 4) * frac, h - 4), 0.5f, 6, col);
    }

    void DrawHud()
    {
        var panelCol = Raylib.Fade(Pal.Black, 0.45f);
        var green = new Color(120, 230, 120, 255);

        // levy horni panel
        Panel(new Rectangle(12, 8, 340, 232), panelCol);
        Txt($"${money}", 24, 14, 40, green);

        int stars = Stars;
        bool blink = ((int)(animT * 4)) % 2 == 0;
        for (int i = 0; i < 5; i++)
        {
            Color c = i < stars ? (blink ? Pal.Yellow : new Color(255, 160, 30, 255)) : new Color(50, 50, 55, 255);
            Raylib.DrawCircle(34 + i * 28, 78, 10, c);
            Raylib.DrawCircleLines(34 + i * 28, 78, 10, Pal.Black);
        }

        if (combo > 1)
        {
            Txt($"CHAOS COMBO x{Mult():0.0}", 24, 94, 22, Pal.Fire);
            Raylib.DrawRectangle(24, 120, (int)(150 * Math.Clamp(comboT / 4f, 0f, 1f)), 3, Pal.Fire);
        }

        Txt("HP", 24, 128, 16, Pal.White);
        Color hpCol = pHp < 30 && blink ? Pal.White : (pHp < 30 ? Pal.Red : new Color(230, 70, 70, 255));
        Bar(56, 128, 240, 16, pHp / 100f, hpCol);
        Txt($"{(int)pHp}", 304, 126, 18, Pal.White);

        Txt("AP", 24, 150, 16, new Color(120, 170, 240, 255));
        Bar(56, 150, 240, 12, pArmor / 100f, new Color(70, 120, 230, 255));

        if (car != null)
        {
            Txt("AUTO", 24, 170, 16, Pal.White);
            Bar(70, 170, 226, 14, car.Hp / car.MaxHp, new Color(70, 170, 230, 255));
            if (car.State == VState.Burning && blink)
            {
                string w = "AUTO HORI! VYSTUP (E)";
                int ww = Raylib.MeasureText(w, 28);
                Txt(w, W / 2 - ww / 2, 240, 28, Pal.Red);
            }
        }

        Txt($"Auta: {carsDestroyed}   Obeti: {kills}   Chaos: ${totalChaos}", 24, 192, 16, new Color(210, 210, 215, 255));
        Txt($"Rekord: ${bestChaos}", 24, 212, 15, new Color(250, 215, 90, 255));

        // zbran vlevo dole
        bool onFootHolstered = car == null && holstered;
        string wn = car != null && car.Kind == VKind.Tank ? "Tankovy kanon"
                  : Weapons.All[weaponIdx].Name + (onFootHolstered ? "  (schovano - F)" : "");
        Panel(new Rectangle(12, H - 100, 340, 62), panelCol);
        Txt(wn, 24, H - 94, 26, onFootHolstered ? Pal.Gray : Pal.White);
        for (int i = 0; i < Weapons.All.Length; i++)
        {
            Color c = i == weaponIdx ? Pal.Yellow : owned[i] ? new Color(170, 170, 176, 255) : new Color(60, 60, 66, 255);
            Raylib.DrawRectangleRounded(new Rectangle(24 + i * 38, H - 60, 32, 10), 0.5f, 4, c);
        }

        // quest
        if (quest != null)
        {
            string qt = QuestText(quest);
            string rw = $"Odmena: ${quest.Reward}";
            int qw = Math.Max(Raylib.MeasureText(qt, 22), Raylib.MeasureText(rw, 18)) + 28;
            Panel(new Rectangle(W - qw - 16, 16, qw, 100), panelCol);
            Raylib.DrawText("SIDEQUEST (N = jiny)", W - qw - 4, 22, 14, Pal.Yellow);
            Raylib.DrawText(qt, W - qw - 4, 40, 22, Pal.White);
            Raylib.DrawText(rw, W - qw - 4, 66, 18, green);
            float frac = quest.Type == QType.Checkpoint ? 0f : quest.Prog / Math.Max(1, quest.Goal);
            if (quest.Type != QType.Checkpoint) Bar(W - qw - 4, 92, qw - 24, 12, frac, Pal.Yellow);
        }

        // varovani pred policii
        if (alertT > 0)
        {
            float aa = Math.Clamp(alertT / 0.6f, 0f, 1f);
            bool flash = ((int)(animT * 6)) % 2 == 0;
            int aw = Raylib.MeasureText(alertText, 32);
            Raylib.DrawRectangleRounded(new Rectangle(W / 2 - aw / 2 - 30, 16, aw + 60, 56), 0.3f, 8,
                Raylib.Fade(flash ? Pal.Red : Pal.Blue, 0.75f * aa));
            Raylib.DrawText(alertText, W / 2 - aw / 2, 30, 32, Raylib.Fade(Pal.White, aa));

            bool has = false; Vector2 np = default; float nd = float.MaxValue;
            foreach (var v in vehicles)
                if (v.State == VState.Alive && v.Kind != VKind.Civil && v.Ai)
                { float dd = Vector2.Distance(v.Pos, pPos); if (dd < nd) { nd = dd; np = v.Pos; has = true; } }
            foreach (var pe in people)
                if (!pe.Dead && pe.Kind != PKind.Civil)
                { float dd = Vector2.Distance(pe.Pos, pPos); if (dd < nd) { nd = dd; np = pe.Pos; has = true; } }
            if (has && nd > 40)
            {
                var dir = Vector2.Normalize(np - pPos);
                Raylib.DrawPoly(new Vector2(W / 2f, H / 2f) + dir * 130, 3, 16, MathF.Atan2(dir.Y, dir.X) * RadToDeg,
                    Raylib.Fade(Pal.Red, aa));
            }
        }

        // navigacni sipka k cili questu (checkpoint)
        if (quest != null && quest.Type == QType.Checkpoint)
        {
            var sp = Raylib.GetWorldToScreen2D(quest.Target, cam);
            bool off = sp.X < 60 || sp.X > W - 60 || sp.Y < 90 || sp.Y > H - 90;
            if (off)
            {
                var dir = Vector2.Normalize(quest.Target - pPos);
                var at = new Vector2(W / 2f, H / 2f) + dir * 150;
                Raylib.DrawPoly(at, 3, 20, MathF.Atan2(dir.Y, dir.X) * RadToDeg, Pal.Yellow);
                Raylib.DrawPolyLines(at, 3, 20, MathF.Atan2(dir.Y, dir.X) * RadToDeg, Pal.Black);
                int m = Math.Max(0, (int)(Vector2.Distance(pPos, quest.Target) / 10));
                Txt($"{m} m", (int)at.X - 20, (int)at.Y + 22, 16, Pal.Yellow);
            }
        }

        // loupez banky / lakovani / vyzva
        if (robbing)
        {
            Panel(new Rectangle(W / 2 - 170, 84, 340, 54), panelCol);
            Txt("LOUPEZ BANKY - zustan u dveri!", W / 2 - 160, 88, 18, Pal.Yellow);
            Bar(W / 2 - 160, 112, 320, 18, robT / RobTime, Pal.Yellow);
        }
        else if (sprayT > 0)
        {
            Panel(new Rectangle(W / 2 - 170, 84, 340, 54), panelCol);
            Txt("LAKUJI A OPRAVUJI...", W / 2 - 160, 88, 18, new Color(255, 120, 190, 255));
            Bar(W / 2 - 160, 112, 320, 18, 1f - sprayT / 1.6f, new Color(255, 120, 190, 255));
        }
        else if (car == null && deadT <= 0 && Vector2.Distance(pPos, world.BankDoor) < 90)
        {
            string bt = bankCd > 0 ? $"Banka je zavrena ({(int)bankCd}s)" : "[E] VYKRAST BANKU";
            int bw2 = Raylib.MeasureText(bt, 26);
            Txt(bt, W / 2 - bw2 / 2, H - 140, 26, bankCd > 0 ? Pal.Gray : Pal.Yellow);
        }
        else if (NearDoor(world.HospitalDoor))
        {
            string ht = $"[E] NEMOCNICE - plne zdravi (${HospitalFee})";
            int hw = Raylib.MeasureText(ht, 26);
            Txt(ht, W / 2 - hw / 2, H - 140, 26, new Color(250, 120, 120, 255));
        }
        else if (NearDoor(world.SafehouseDoor))
        {
            string st = "[E] UKRYT - smazat hvezdy a ulozit";
            int sw3 = Raylib.MeasureText(st, 26);
            Txt(st, W / 2 - sw3 / 2, H - 140, 26, new Color(120, 220, 150, 255));
        }

        // hlaska
        if (msgT > 0)
        {
            int tw = Raylib.MeasureText(msg, 24);
            float a = Math.Clamp(msgT / 0.5f, 0f, 1f);
            Raylib.DrawRectangleRounded(new Rectangle(W / 2 - tw / 2 - 16, 146, tw + 32, 40), 0.3f, 8, Raylib.Fade(Pal.Black, 0.55f * a));
            Raylib.DrawText(msg, W / 2 - tw / 2, 154, 24, Raylib.Fade(Pal.White, a));
        }

        if (deadT > 0)
        {
            int tw = Raylib.MeasureText("WASTED", 96);
            Raylib.DrawRectangle(0, H / 2 - 80, W, 160, Raylib.Fade(Pal.Black, 0.5f));
            Txt("WASTED", W / 2 - tw / 2, H / 2 - 48, 96, Pal.Red);
        }

        Txt("P/ESC pauza  M mapa  B obchod  E auto  Q zbran  F schovat/vytahnout", 22, H - 28, 16, new Color(200, 200, 205, 255));

        DrawMinimap();
    }

    // ---------- menu / napoveda

    void DrawMenu()
    {
        Raylib.DrawRectangle(0, 0, W, H, Raylib.Fade(Pal.Black, scr == Scr.Menu ? 0.45f : 0.6f));
        if (scr == Scr.Help) { DrawHelp(); return; }

        string title = scr == Scr.Menu ? "CHAOS CITY" : "PAUZA";
        int size = scr == Scr.Menu ? 104 : 72;
        int ty = scr == Scr.Menu ? 120 : 130;
        int tw = Raylib.MeasureText(title, size);
        Raylib.DrawText(title, W / 2 - tw / 2 + 5, ty + 5, size, Raylib.Fade(Pal.Black, 0.8f));
        Raylib.DrawText(title, W / 2 - tw / 2, ty, size, scr == Scr.Menu ? Pal.Yellow : Pal.White);

        string sub = scr == Scr.Menu
            ? "kradni, znic, vydelej - a utec policii"
            : $"Penize: ${money}   Znicena auta: {carsDestroyed}   Obeti: {kills}";
        int sw = Raylib.MeasureText(sub, scr == Scr.Menu ? 24 : 22);
        Txt(sub, W / 2 - sw / 2, ty + (scr == Scr.Menu ? 118 : 90), scr == Scr.Menu ? 24 : 22, new Color(220, 220, 225, 255));

        if (scr == Scr.Menu && bestChaos > 0)
        {
            string rec = $"Rekord: ${bestChaos}";
            int rw = Raylib.MeasureText(rec, 22);
            Txt(rec, W / 2 - rw / 2, ty + 150, 22, Pal.Yellow);
        }

        var items = CurItems;
        for (int i = 0; i < items.Length; i++)
        {
            var r = ItemRect(i);
            bool on = i == sel;
            Raylib.DrawRectangleRounded(new Rectangle(r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6), 0.3f, 8,
                on ? Pal.White : new Color(90, 90, 100, 255));
            Raylib.DrawRectangleRounded(r, 0.3f, 8, on ? Pal.Yellow : new Color(30, 30, 38, 240));
            int iw = Raylib.MeasureText(items[i], 28);
            Raylib.DrawText(items[i], (int)(r.X + r.Width / 2 - iw / 2), (int)(r.Y + 13), 28, on ? Pal.Dark : Pal.White);
        }

        string foot = "W/S nebo mys   Enter / klik = vyber";
        int fw = Raylib.MeasureText(foot, 16);
        Txt(foot, W / 2 - fw / 2, H - 40, 16, Pal.Gray);
    }

    void DrawHelp()
    {
        int pw = 760, ph = 640, px = W / 2 - pw / 2, py = H / 2 - ph / 2;
        Raylib.DrawRectangleRounded(new Rectangle(px - 3, py - 3, pw + 6, ph + 6), 0.04f, 8, Pal.Yellow);
        Raylib.DrawRectangleRounded(new Rectangle(px, py, pw, ph), 0.04f, 8, new Color(24, 24, 30, 255));
        Txt("OVLADANI", px + 28, py + 18, 34, Pal.Yellow);

        string[,] rows =
        {
            { "WASD / sipky", "chuze, jizda" },
            { "Shift", "beh (pesky) / nitro boost (v aute)" },
            { "Mys (LMB)", "strelba / kanon tanku" },
            { "E", "nastup/vystup z auta, pouziti budovy" },
            { "Space", "rucni brzda (smyk zanecha stopy)" },
            { "Q", "dalsi zbran" },
            { "F", "schovat / vytahnout zbran" },
            { "B", "obchod (zbrane 1-6, brneni 7)" },
            { "N", "novy sidequest" },
            { "M", "velka mapa" },
            { "E u banky", "vykrast banku (12 s)" },
            { "E u nemocnice", "plne vyleceni ($200)" },
            { "E u ukrytu", "smazat hvezdy + ulozit hru" },
            { "P / ESC", "pauza" },
        };
        for (int i = 0; i < rows.GetLength(0); i++)
        {
            Txt(rows[i, 0], px + 28, py + 66 + i * 28, 21, Pal.White);
            Txt(rows[i, 1], px + 260, py + 66 + i * 28, 21, new Color(200, 200, 205, 255));
        }

        Txt("TIPY", px + 28, py + 470, 22, Pal.Yellow);
        Txt("Nic rychle za sebou = chaos combo az x5. Z vraku padaji lekarnicky, penize i brneni.", px + 28, py + 498, 17, Pal.White);
        Txt("Mesto zije v cyklu den/noc - v noci sviti lampy, okna a reflektory aut.", px + 28, py + 520, 17, Pal.White);
        Txt("Na 3+ hvezdach stavi policie zatarasy, na 5 hvezdach prileti bojovy vrtulnik.", px + 28, py + 542, 17, Pal.White);
        Txt("Jezdi i dodavky, autobusy, motorky a sanitky. Granat (zbran 6) lob na dalku.", px + 28, py + 564, 17, Pal.White);
        Txt("Klikni nebo stiskni klavesu pro navrat", px + 28, py + 598, 16, Pal.Gray);
    }

    void DrawMinimap()
    {
        int size = 180, mx = W - size - 14, my = H - size - 40;
        float s = size / World.Size;

        Raylib.DrawRectangle(mx - 3, my - 3, size + 6, size + 6, Pal.Black);
        Raylib.DrawRectangle(mx, my, size, size, Pal.MapBg);
        foreach (var pk in world.Parks)
            Raylib.DrawRectangle(mx + (int)(pk.X * s), my + (int)(pk.Y * s),
                Math.Max(1, (int)(pk.Width * s)), Math.Max(1, (int)(pk.Height * s)), Pal.Park);
        foreach (var b in world.Buildings)
            Raylib.DrawRectangle(mx + (int)(b.X * s), my + (int)(b.Y * s),
                Math.Max(1, (int)(b.Width * s)), Math.Max(1, (int)(b.Height * s)), Pal.Gray);

        if (quest != null && quest.Type == QType.Checkpoint)
            Raylib.DrawCircle(mx + (int)(quest.Target.X * s), my + (int)(quest.Target.Y * s), 5, Pal.Yellow);

        if (Stars > 0)
        {
            foreach (var v in vehicles)
                if (v.State == VState.Alive && v.Kind != VKind.Civil && !v.PlayerDriven)
                    Raylib.DrawCircle(mx + (int)(v.Pos.X * s), my + (int)(v.Pos.Y * s), 3, v.Kind == VKind.Tank ? Pal.Red : Pal.Blue);
            foreach (var p in people)
                if (!p.Dead && p.Kind != PKind.Civil)
                    Raylib.DrawCircle(mx + (int)(p.Pos.X * s), my + (int)(p.Pos.Y * s), 2, Pal.Blue);
            if (heli != null)
                Raylib.DrawCircle(mx + (int)(heli.Pos.X * s), my + (int)(heli.Pos.Y * s), 3, Pal.Red);
        }

        float vw = W / cam.Zoom * s, vh = H / cam.Zoom * s;
        Raylib.BeginScissorMode(mx, my, size, size);
        Raylib.DrawRectangleLines(mx + (int)(cam.Target.X * s - vw / 2), my + (int)(cam.Target.Y * s - vh / 2),
            (int)vw, (int)vh, Raylib.Fade(Pal.White, 0.5f));
        Raylib.EndScissorMode();

        Raylib.DrawCircle(mx + (int)(spray.X * s), my + (int)(spray.Y * s), 4, new Color(255, 90, 180, 255));
        Raylib.DrawCircle(mx + (int)(world.BankDoor.X * s), my + (int)(world.BankDoor.Y * s), 4, new Color(240, 200, 60, 255));
        Raylib.DrawCircle(mx + (int)(world.HospitalDoor.X * s), my + (int)(world.HospitalDoor.Y * s), 4, new Color(250, 110, 110, 255));
        Raylib.DrawCircle(mx + (int)(world.SafehouseDoor.X * s), my + (int)(world.SafehouseDoor.Y * s), 4, new Color(110, 220, 150, 255));
        float pa = (car != null ? car.Angle : pAngle) * RadToDeg;
        Raylib.DrawPoly(new Vector2(mx + pPos.X * s, my + pPos.Y * s), 3, 6, pa, Pal.White);
        Txt("M = velka mapa", mx, my - 20, 14, Pal.White);
    }

    void DrawBigMap()
    {
        Raylib.DrawRectangle(0, 0, W, H, Raylib.Fade(Pal.Black, 0.72f));
        int size = 620, mx = W / 2 - size / 2, my = H / 2 - size / 2 + 8;
        float s = size / World.Size;
        float bs = World.Cell - World.Road;
        var gold = new Color(240, 200, 60, 255);
        var pink = new Color(255, 90, 180, 255);
        var hred = new Color(250, 110, 110, 255);
        var sgreen = new Color(110, 220, 150, 255);

        void R(Rectangle r, Color c) => Raylib.DrawRectangle(mx + (int)(r.X * s), my + (int)(r.Y * s),
            Math.Max(1, (int)(r.Width * s)), Math.Max(1, (int)(r.Height * s)), c);

        Raylib.DrawRectangleRounded(new Rectangle(mx - 6, my - 6, size + 12, size + 12), 0.02f, 6, Pal.Yellow);
        Raylib.DrawRectangle(mx, my, size, size, new Color(56, 56, 62, 255));

        for (int i = 0; i < World.Grid; i++)
            for (int j = 0; j < World.Grid; j++)
                R(new Rectangle(i * World.Cell + World.Road, j * World.Cell + World.Road, bs + 3, bs + 3), new Color(96, 96, 94, 255));
        foreach (var pk in world.Parks) R(pk, Pal.Park);
        foreach (var b in world.Buildings) R(b, new Color(128, 128, 134, 255));
        R(world.Bank, gold);
        R(world.Garage, pink);
        R(world.Hospital, hred);
        R(world.Safehouse, sgreen);

        // souradnice ulic
        for (int k = 0; k <= World.Grid; k++)
        {
            float c = k * World.Cell + World.Road / 2;
            Txt(((char)('A' + k)).ToString(), mx + (int)(c * s) - 5, my - 24, 16, Pal.White);
            Txt((k + 1).ToString(), mx - 18, my + (int)(c * s) - 8, 16, Pal.White);
        }

        string bl = "BANKA", gl = "LAKOVNA";
        Txt(bl, mx + (int)((world.Bank.X + world.Bank.Width / 2) * s) - Raylib.MeasureText(bl, 14) / 2,
            my + (int)((world.Bank.Y + world.Bank.Height / 2) * s) - 7, 14, Pal.Black);
        Txt(gl, mx + (int)((world.Garage.X + world.Garage.Width / 2) * s) - Raylib.MeasureText(gl, 14) / 2,
            my + (int)((world.Garage.Y + world.Garage.Height / 2) * s) - 7, 14, Pal.Black);

        if (quest != null && quest.Type == QType.Checkpoint)
        {
            Raylib.DrawCircle(mx + (int)(quest.Target.X * s), my + (int)(quest.Target.Y * s), 8, Pal.Yellow);
            Raylib.DrawCircleLines(mx + (int)(quest.Target.X * s), my + (int)(quest.Target.Y * s), 8, Pal.Black);
        }

        if (Stars > 0)
        {
            foreach (var v in vehicles)
                if (v.State == VState.Alive && v.Kind != VKind.Civil && !v.PlayerDriven)
                    Raylib.DrawCircle(mx + (int)(v.Pos.X * s), my + (int)(v.Pos.Y * s), 5, v.Kind == VKind.Tank ? Pal.Red : Pal.Blue);
            foreach (var p in people)
                if (!p.Dead && p.Kind != PKind.Civil)
                    Raylib.DrawCircle(mx + (int)(p.Pos.X * s), my + (int)(p.Pos.Y * s), 3, p.Kind == PKind.Soldier ? Pal.Red : Pal.Blue);
        }

        float pa = (car != null ? car.Angle : pAngle) * RadToDeg;
        Raylib.DrawPoly(new Vector2(mx + pPos.X * s, my + pPos.Y * s), 3, 10, pa, Pal.White);

        int ni = Math.Clamp((int)MathF.Round((pPos.X - World.Road / 2) / World.Cell), 0, World.Grid);
        int nj = Math.Clamp((int)MathF.Round((pPos.Y - World.Road / 2) / World.Cell), 0, World.Grid);
        Txt("MAPA MESTA", mx, my - 56, 28, Pal.Yellow);
        Txt($"Poloha: {(char)('A' + ni)}{nj + 1}", mx + 240, my - 50, 20, Pal.White);

        int lx = mx + size + 28, ly = my + 10;
        (string, Color)[] legend =
        {
            ("Ty", Pal.White), ("Banka (E = vykrast)", gold), ("Lakovna (zajed autem)", pink),
            ("Nemocnice (E = lecba)", hred), ("Ukryt (E = ulozit)", sgreen),
            ("Cil questu", Pal.Yellow), ("Policie", Pal.Blue), ("Armada / vrtulnik", Pal.Red),
        };
        for (int i = 0; i < legend.Length; i++)
        {
            Raylib.DrawRectangle(lx, ly + i * 30 + 3, 16, 16, legend[i].Item2);
            Txt(legend[i].Item1, lx + 26, ly + i * 30, 20, Pal.White);
        }
        Txt("M / ESC = zavrit", mx, my + size + 14, 16, Pal.Gray);
    }

    void DrawShop()
    {
        Raylib.DrawRectangle(0, 0, W, H, Raylib.Fade(Pal.Black, 0.6f));
        int px = W / 2 - 300, py = H / 2 - 220;
        Raylib.DrawRectangleRounded(new Rectangle(px - 2, py - 2, 604, 456), 0.04f, 8, Pal.Yellow);
        Raylib.DrawRectangleRounded(new Rectangle(px, py, 600, 452), 0.04f, 8, new Color(24, 24, 30, 255));
        Txt("OBCHOD SE ZBRANEMI", px + 24, py + 18, 30, Pal.Yellow);
        Txt($"Penize: ${money}", px + 24, py + 56, 22, new Color(120, 230, 120, 255));

        var mp = Raylib.GetMousePosition();
        for (int i = 0; i < Weapons.All.Length; i++)
        {
            var w = Weapons.All[i];
            var r = ShopRow(i);
            if (i == weaponIdx) Raylib.DrawRectangleRounded(r, 0.25f, 6, Raylib.Fade(new Color(120, 230, 120, 255), 0.18f));
            if (Raylib.CheckCollisionPointRec(mp, r)) Raylib.DrawRectangleRounded(r, 0.25f, 6, Raylib.Fade(Pal.White, 0.10f));

            string status = owned[i] ? (i == weaponIdx ? "[VYBRANO]" : "[MAS]") : $"${w.Price}";
            Color c = owned[i] ? new Color(120, 230, 120, 255) : (money >= w.Price ? Pal.White : new Color(150, 150, 155, 255));
            string dmg = w.Explosive ? "vybuch" : $"{w.Damage * w.Pellets:0} dmg";
            Txt($"{i + 1}.  {w.Name}", (int)r.X + 12, (int)r.Y + 8, 26, c);
            Txt($"{dmg}  {1f / w.Cooldown:0.#}/s", (int)r.X + 230, (int)r.Y + 14, 16, Pal.Gray);
            Txt(status, (int)r.X + 410, (int)r.Y + 10, 22, c);
        }

        // radek brneni
        int aidx = Weapons.All.Length;
        var ar = ShopRow(aidx);
        if (Raylib.CheckCollisionPointRec(mp, ar)) Raylib.DrawRectangleRounded(ar, 0.25f, 6, Raylib.Fade(Pal.White, 0.10f));
        Color ac = money >= ArmorPrice ? new Color(120, 170, 240, 255) : new Color(150, 150, 155, 255);
        Txt($"{aidx + 1}.  Brneni (+50)", (int)ar.X + 12, (int)ar.Y + 8, 26, ac);
        Txt($"aktualne {(int)pArmor}/100", (int)ar.X + 230, (int)ar.Y + 14, 16, Pal.Gray);
        Txt($"${ArmorPrice}", (int)ar.X + 410, (int)ar.Y + 10, 22, ac);

        Txt("Klik nebo cisla 1-7 = koupit / vybrat.   B / ESC = zavrit", px + 24, py + 418, 18, Pal.Gray);
    }
}
