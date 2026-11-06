using System.Numerics;
using Raylib_cs;

namespace Z04;

public class Game
{
    const float RadToDeg = 180f / MathF.PI;
    static readonly Vector2[] Dirs = { new(1, 0), new(0, 1), new(-1, 0), new(0, -1) };

    // kolik policistu / aut / vojaku / tanku chceme podle hvezdicek (index 0..5)
    static readonly int[] WantCops = { 0, 2, 3, 3, 2, 1 };
    static readonly int[] WantCars = { 0, 0, 1, 2, 2, 2 };
    static readonly int[] WantSoldiers = { 0, 0, 0, 0, 3, 5 };
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

    // hrac
    Vector2 pPos;
    float pAngle, pHp = 100, fireT, deadT, lastHurtT = 99, carHitCd;
    Vehicle? car;
    int money, weaponIdx;
    readonly bool[] owned = new bool[Weapons.All.Length];
    bool shop;

    // chaos / stav hry
    float heat, comboT, spawnT, shake, animT, msgT, civT;
    int combo, carsDestroyed, kills, totalChaos;
    string msg = "";
    Quest? quest;
    Camera2D cam;
    float vx0, vy0, vx1, vy1;

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

    // ------------------------------------------------------------ start

    public Game(int w, int h)
    {
        W = w; H = h;
        owned[0] = true;
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
            people.Add(new Person(kind, pos, col) { Angle = Rnd(0, 2 * MathF.PI) });
            return;
        }
    }

    // ------------------------------------------------------------ update

    public void Update(float dt)
    {
        animT += dt;
        if (msgT > 0) msgT -= dt;

        if (Raylib.IsKeyPressed(Keys.B) && deadT <= 0) shop = !shop;
        if (shop) { UpdateShop(); return; }

        UpdatePlayer(dt);
        UpdateVehicles(dt);
        UpdatePeople(dt);
        UpdateBullets(dt);
        UpdateWanted(dt);
        UpdateQuest(dt);
        UpdateEffects(dt);
        Cleanup();
        UpdateCamera(dt);
    }

    void UpdateShop()
    {
        for (int i = 0; i < Weapons.All.Length; i++)
        {
            if (!Raylib.IsKeyPressed(Keys.Num(i))) continue;
            var w = Weapons.All[i];
            if (owned[i]) { weaponIdx = i; Say("Vybrano: " + w.Name); }
            else if (money >= w.Price)
            {
                money -= w.Price; owned[i] = true; weaponIdx = i;
                Say("Koupeno: " + w.Name);
            }
            else Say("Nedostatek penez na " + w.Name);
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

        if (Raylib.IsKeyPressed(Keys.E))
        {
            if (car != null) ExitCar(); else TryEnterCar();
        }
        if (car != null) return;   // v aute se rizeni resi v UpdateVehicles

        if (Raylib.IsKeyPressed(Keys.Q))
        {
            do { weaponIdx = (weaponIdx + 1) % Weapons.All.Length; } while (!owned[weaponIdx]);
            Say("Zbran: " + Weapons.All[weaponIdx].Name);
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
        world.Resolve(ref pPos, 9);

        // mireni a strelba
        var mw = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), cam);
        pAngle = MathF.Atan2(mw.Y - pPos.Y, mw.X - pPos.X);
        if (Raylib.IsMouseButtonDown(Keys.MouseLeft) && fireT <= 0)
        {
            var w = Weapons.All[weaponIdx];
            fireT = w.Cooldown;
            Shoot(pPos + DirOf(pAngle) * 14, pAngle, w.Damage, w.Spread, w.Speed, w.Range, w.Explosive, true, null, w.Pellets);
            AlertCivilians(pPos, 320, 3f);
            if (w.Explosive) shake = MathF.Max(shake, 5);
        }
    }

    void Respawn()
    {
        pHp = 100; heat = 0; car = null; combo = 0;
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
        pHp -= dmg;
        lastHurtT = 0;
        shake = MathF.Max(shake, 4);
        if (pHp <= 0)
        {
            pHp = 0;
            deadT = 3.5f;
            if (car != null) { car.PlayerDriven = false; car = null; }
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
        DriveVehicle(v, thr, st, Raylib.IsKeyDown(Keys.Space), dt);

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

        Vector2 to = pPos - v.Pos;
        float dist = to.Length();
        float desired = MathF.Atan2(to.Y, to.X);
        v.FireT -= dt;

        if (v.Kind == VKind.Tank)
        {
            v.TurretAngle = desired;
            if (dist < 600 && v.FireT <= 0)
            {
                v.FireT = 2.6f;
                Shoot(v.Pos + DirOf(desired) * 46, desired + Rnd(-0.05f, 0.05f), 0, 0.03f, 520, 900, true, false, v);
            }
        }
        else if (dist < 330 && v.FireT <= 0)
        {
            v.FireT = 0.45f;
            Shoot(v.Pos + DirOf(desired) * 24, desired, 7, 0.14f, 700, 520, false, false, v);
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

            if (v.PlayerDriven) UpdatePlayerCar(v, dt);
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
                        p.FireT = 0.14f;
                        Shoot(p.Pos + dir * 12, ang, 7, 0.14f, 800, 550, false, false, null);
                    }
                    else
                    {
                        p.FireT = 0.8f;
                        Shoot(p.Pos + dir * 12, ang, 9, 0.10f, 700, 500, false, false, null);
                    }
                }
            }
            else vel = Wander(p, dt, 40f);

            p.Pos += vel * dt;
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
        kills++;
        for (int i = 0; i < 5; i++)
            AddParticle(p.Pos, new Vector2(Rnd(-60, 60), Rnd(-60, 60)), 0.5f, 4, 2);

        if (!p.HitByPlayer) return;
        switch (p.Kind)
        {
            case PKind.Civil: heat += 14; AddMoney(50, p.Pos); break;
            case PKind.Cop: heat += 45; AddMoney(150, p.Pos); QuestProgress(QType.KillCops); break;
            default: heat += 55; AddMoney(300, p.Pos); QuestProgress(QType.KillCops); break;
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
                        DamageVehicle(v, b.Damage * 0.7f, false);
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

        float pd = Vector2.Distance(pPos, pos);
        if (deadT <= 0 && pd < radius) HurtPlayer(damage * (1f - pd / radius) * 0.6f);

        AlertCivilians(pos, 450, 4f);
        shake = MathF.Max(shake, MathF.Max(0, 16 - Vector2.Distance(pos, pPos) / 40f));
    }

    // ---------- hledanost, policie, armada

    void UpdateWanted(float dt)
    {
        heat = Math.Clamp(heat, 0, 200);

        if (heat > 0)
        {
            float nearest = 99999;
            foreach (var p in people)
                if (!p.Dead && p.Kind != PKind.Civil) nearest = MathF.Min(nearest, Vector2.Distance(p.Pos, pPos));
            foreach (var v in vehicles)
                if (v.State == VState.Alive && v.Kind != VKind.Civil && v.Ai) nearest = MathF.Min(nearest, Vector2.Distance(v.Pos, pPos));
            heat -= (nearest > 700 ? 7f : 0.6f) * dt;
            if (heat < 0) heat = 0;
        }

        comboT -= dt;
        if (comboT <= 0) combo = 0;

        spawnT -= dt;
        if (spawnT <= 0) { spawnT = 1.2f; SpawnPolice(); }

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
            if (v.State == VState.Wreck) return v.WreckAge > 40f && d > 800;
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

        world.Draw(vx0, vy0, vx1, vy1);

        foreach (var p in people)
            if (p.Dead && InView(p.Pos)) Raylib.DrawCircleV(p.Pos, 11, Raylib.Fade(Pal.Blood, MathF.Max(0.25f, 1f - p.DeadT / 25f)));
        if (deadT > 0) Raylib.DrawCircleV(pPos, 13, Pal.Blood);

        foreach (var v in vehicles)
            if (v.State == VState.Wreck && InView(v.Pos)) DrawVehicle(v);
        foreach (var v in vehicles)
            if (v.State != VState.Wreck && InView(v.Pos)) DrawVehicle(v);

        foreach (var p in people)
            if (!p.Dead && InView(p.Pos)) DrawPerson(p);

        if (car == null && deadT <= 0)
        {
            Raylib.DrawCircleV(pPos, 9, new Color(245, 200, 60, 255));
            Raylib.DrawCircleLines((int)pPos.X, (int)pPos.Y, 9, Pal.Black);
            Raylib.DrawLineEx(pPos, pPos + DirOf(pAngle) * 18, 4, Pal.Dark);
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

        foreach (var e in explosions)
        {
            float t = e.T / Explosion.Dur;
            float r = e.Radius * MathF.Min(1f, t * 3f);
            Raylib.DrawCircleV(e.Pos, r, Raylib.Fade(Pal.Fire, (1f - t) * 0.85f));
            Raylib.DrawCircleV(e.Pos, r * 0.6f, Raylib.Fade(Pal.Yellow, (1f - t) * 0.9f));
            Raylib.DrawCircleV(e.Pos, r * 0.25f, Raylib.Fade(Pal.White, (1f - t)));
        }

        foreach (var p in particles)
        {
            if (!InView(p.Pos)) continue;
            float t = p.Life / p.MaxLife;
            if (p.Kind == 0) Raylib.DrawCircleV(p.Pos, p.Size * (0.4f + t * 0.6f), Raylib.Fade(Pal.Fire, t));
            else if (p.Kind == 1) Raylib.DrawCircleV(p.Pos, p.Size * (1.8f - t * 0.8f), Raylib.Fade(Pal.Smoke, t * 0.6f));
            else Raylib.DrawCircleV(p.Pos, p.Size * 0.5f, Raylib.Fade(Pal.Yellow, t));
        }

        if (quest != null && quest.Type == QType.Checkpoint)
        {
            float pulse = 1f + MathF.Sin(animT * 5f) * 0.12f;
            Raylib.DrawCircleV(quest.Target, 70 * pulse, Raylib.Fade(Pal.Yellow, 0.35f));
            Raylib.DrawCircleLines((int)quest.Target.X, (int)quest.Target.Y, 70 * pulse, Pal.Yellow);
        }

        foreach (var p in popups)
            Txt(p.Text, (int)p.Pos.X - 30, (int)(p.Pos.Y - 20 - (1.6f - p.Life) * 40), 22, Pal.Yellow);

        Raylib.EndMode2D();

        DrawHud();
        if (shop) DrawShop();
    }

    void DrawVehicle(Vehicle v)
    {
        float deg = v.Angle * RadToDeg;
        var fwd = v.Forward;
        var right = new Vector2(-fwd.Y, fwd.X);
        var origin = new Vector2(v.Length / 2, v.Width / 2);
        bool wreck = v.State == VState.Wreck;

        Raylib.DrawRectanglePro(new Rectangle(v.Pos.X + 3, v.Pos.Y + 3, v.Length, v.Width), origin, deg, Raylib.Fade(Pal.Black, 0.3f));

        if (v.Kind == VKind.Tank)
        {
            Color body = wreck ? Pal.Wreck : v.Body;
            foreach (float side in new[] { -1f, 1f })
            {
                var tc = v.Pos + right * side * (v.Width / 2 - 4);
                Raylib.DrawRectanglePro(new Rectangle(tc.X, tc.Y, v.Length, 10), new Vector2(v.Length / 2, 5), deg, Pal.Dark);
            }
            Raylib.DrawRectanglePro(new Rectangle(v.Pos.X, v.Pos.Y, v.Length - 6, v.Width - 10), new Vector2((v.Length - 6) / 2, (v.Width - 10) / 2), deg, body);
            Raylib.DrawRectanglePro(new Rectangle(v.Pos.X, v.Pos.Y, 44, 7), new Vector2(0, 3.5f), v.TurretAngle * RadToDeg, Pal.Dark);
            Raylib.DrawCircleV(v.Pos, 13, wreck ? Pal.Dark : new Color(64, 84, 46, 255));
            if (v.State == VState.Burning) Raylib.DrawCircleV(v.Pos, 20, Raylib.Fade(Pal.Black, 0.4f));
            return;
        }

        Raylib.DrawRectanglePro(new Rectangle(v.Pos.X, v.Pos.Y, v.Length, v.Width), origin, deg, wreck ? Pal.Wreck : v.Body);
        if (wreck)
        {
            Raylib.DrawRectanglePro(new Rectangle(v.Pos.X, v.Pos.Y, v.Length * 0.6f, v.Width * 0.6f),
                new Vector2(v.Length * 0.3f, v.Width * 0.3f), deg, Raylib.Fade(Pal.Black, 0.6f));
            return;
        }

        var wc = v.Pos + fwd * (v.Length * 0.14f);
        Raylib.DrawRectanglePro(new Rectangle(wc.X, wc.Y, v.Length * 0.26f, v.Width * 0.78f),
            new Vector2(v.Length * 0.13f, v.Width * 0.39f), deg, Pal.Glass);
        var rc = v.Pos - fwd * (v.Length * 0.27f);
        Raylib.DrawRectanglePro(new Rectangle(rc.X, rc.Y, v.Length * 0.16f, v.Width * 0.7f),
            new Vector2(v.Length * 0.08f, v.Width * 0.35f), deg, Pal.Glass);

        var hl = v.Pos + fwd * (v.Length / 2 - 2);
        Raylib.DrawCircleV(hl + right * (v.Width / 2 - 4), 2.5f, Pal.Yellow);
        Raylib.DrawCircleV(hl - right * (v.Width / 2 - 4), 2.5f, Pal.Yellow);

        if (v.Kind == VKind.Police)
        {
            Raylib.DrawRectanglePro(new Rectangle(v.Pos.X, v.Pos.Y, v.Length * 0.9f, 4), new Vector2(v.Length * 0.45f, 2), deg, Pal.Blue);
            bool b = ((int)(animT * 8)) % 2 == 0;
            Raylib.DrawCircleV(v.Pos + right * 5, 4.5f, b ? Pal.Red : Pal.Blue);
            Raylib.DrawCircleV(v.Pos - right * 5, 4.5f, b ? Pal.Blue : Pal.Red);
        }

        if (v.State == VState.Burning)
            Raylib.DrawRectanglePro(new Rectangle(v.Pos.X, v.Pos.Y, v.Length, v.Width), origin, deg, Raylib.Fade(Pal.Black, 0.45f));
    }

    void DrawPerson(Person p)
    {
        Raylib.DrawCircleV(p.Pos, 8.5f, p.Col);
        Raylib.DrawCircleLines((int)p.Pos.X, (int)p.Pos.Y, 8.5f, Raylib.Fade(Pal.Black, 0.6f));
        if (p.Kind != PKind.Civil)
            Raylib.DrawLineEx(p.Pos, p.Pos + DirOf(p.Angle) * 16, 3, Pal.Dark);
    }

    void DrawHud()
    {
        Txt($"${money}", 22, 16, 38, Pal.White);

        // hvezdicky
        int stars = Stars;
        bool blink = ((int)(animT * 4)) % 2 == 0;
        for (int i = 0; i < 5; i++)
        {
            Color c = i < stars ? (blink ? Pal.Yellow : new Color(255, 160, 30, 255)) : new Color(50, 50, 55, 255);
            Raylib.DrawCircle(34 + i * 28, 78, 10, c);
            Raylib.DrawCircleLines(34 + i * 28, 78, 10, Pal.Black);
        }

        if (combo > 1) Txt($"CHAOS COMBO x{Mult():0.0}", 22, 96, 22, Pal.Fire);

        // zdravi hrace
        Raylib.DrawRectangle(22, 130, 204, 16, Pal.Dark);
        Raylib.DrawRectangle(24, 132, (int)(2 * pHp), 12, Pal.Red);
        Txt("ZDRAVI", 232, 130, 16, Pal.White);

        if (car != null)
        {
            Raylib.DrawRectangle(22, 152, 204, 16, Pal.Dark);
            Raylib.DrawRectangle(24, 154, (int)(200 * car.Hp / car.MaxHp), 12, new Color(70, 170, 230, 255));
            Txt(car.State == VState.Burning ? "AUTO HORI! VYSTUP (E)" : "AUTO", 232, 152, 16,
                car.State == VState.Burning ? Pal.Red : Pal.White);
        }

        string wn = car != null && car.Kind == VKind.Tank ? "Tankovy kanon" : Weapons.All[weaponIdx].Name;
        Txt("Zbran: " + wn, 22, 176, 20, Pal.White);
        Txt($"Znicena auta: {carsDestroyed}   Obeti: {kills}   Celkem chaos: ${totalChaos}", 22, 202, 16, new Color(210, 210, 215, 255));

        // quest
        if (quest != null)
        {
            string qt = QuestText(quest);
            string rw = $"Odmena: ${quest.Reward}";
            int qw = Math.Max(Raylib.MeasureText(qt, 22), Raylib.MeasureText(rw, 18)) + 28;
            Raylib.DrawRectangle(W - qw - 16, 16, qw, 70, Raylib.Fade(Pal.Black, 0.55f));
            Raylib.DrawText("SIDEQUEST (N = jiny)", W - qw - 4, 22, 14, Pal.Yellow);
            Raylib.DrawText(qt, W - qw - 4, 40, 22, Pal.White);
            Raylib.DrawText(rw, W - qw - 4, 66, 18, new Color(120, 230, 120, 255));
        }

        if (msgT > 0)
        {
            int tw = Raylib.MeasureText(msg, 26);
            Txt(msg, W / 2 - tw / 2, 100, 26, Pal.White);
        }

        if (deadT > 0)
        {
            int tw = Raylib.MeasureText("WASTED", 96);
            Raylib.DrawRectangle(0, H / 2 - 80, W, 160, Raylib.Fade(Pal.Black, 0.5f));
            Txt("WASTED", W / 2 - tw / 2, H / 2 - 48, 96, Pal.Red);
        }

        Txt("WASD jizda/chuze  E nastup/vystup  LMB strelba  Q zbran  B obchod  Shift beh  Space rucni brzda",
            22, H - 28, 16, new Color(200, 200, 205, 255));

        DrawMinimap();
    }

    void DrawMinimap()
    {
        int size = 180, mx = W - size - 14, my = H - size - 40;
        float s = size / World.Size;

        Raylib.DrawRectangle(mx - 3, my - 3, size + 6, size + 6, Pal.Black);
        Raylib.DrawRectangle(mx, my, size, size, Pal.MapBg);
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
        }

        Raylib.DrawCircle(mx + (int)(pPos.X * s), my + (int)(pPos.Y * s), 4, Pal.White);
    }

    void DrawShop()
    {
        Raylib.DrawRectangle(0, 0, W, H, Raylib.Fade(Pal.Black, 0.6f));
        int px = W / 2 - 300, py = H / 2 - 190;
        Raylib.DrawRectangle(px, py, 600, 380, new Color(24, 24, 30, 255));
        Raylib.DrawRectangleLines(px, py, 600, 380, Pal.Yellow);
        Txt("OBCHOD SE ZBRANEMI", px + 24, py + 18, 30, Pal.Yellow);
        Txt($"Penize: ${money}", px + 24, py + 56, 22, Pal.White);

        for (int i = 0; i < Weapons.All.Length; i++)
        {
            var w = Weapons.All[i];
            string status = owned[i] ? (i == weaponIdx ? "[VYBRANO]" : "[MAS - stiskni pro vyber]") : $"${w.Price}";
            Color c = owned[i] ? new Color(120, 230, 120, 255) : (money >= w.Price ? Pal.White : new Color(150, 150, 155, 255));
            Txt($"{i + 1}.  {w.Name}", px + 24, py + 100 + i * 46, 26, c);
            Txt(status, px + 280, py + 104 + i * 46, 22, c);
        }
        Txt("Stiskni cislo 1-5 = koupit / vybrat.   B = zavrit", px + 24, py + 340, 18, Pal.Gray);
    }
}
