using System.Numerics;
using Raylib_cs;

namespace Z04;

/// <summary>Klavesy jako ciselne kody (GLFW) - nezavisle na verzi Raylib-cs.</summary>
public static class Keys
{
    public static readonly KeyboardKey W = (KeyboardKey)87;
    public static readonly KeyboardKey A = (KeyboardKey)65;
    public static readonly KeyboardKey S = (KeyboardKey)83;
    public static readonly KeyboardKey D = (KeyboardKey)68;
    public static readonly KeyboardKey E = (KeyboardKey)69;
    public static readonly KeyboardKey F = (KeyboardKey)70;
    public static readonly KeyboardKey B = (KeyboardKey)66;
    public static readonly KeyboardKey N = (KeyboardKey)78;
    public static readonly KeyboardKey Q = (KeyboardKey)81;
    public static readonly KeyboardKey R = (KeyboardKey)82;
    public static readonly KeyboardKey M = (KeyboardKey)77;
    public static readonly KeyboardKey P = (KeyboardKey)80;
    public static readonly KeyboardKey Enter = (KeyboardKey)257;
    public static readonly KeyboardKey Escape = (KeyboardKey)256;
    public static readonly KeyboardKey Space = (KeyboardKey)32;
    public static readonly KeyboardKey Shift = (KeyboardKey)340;
    public static readonly KeyboardKey Up = (KeyboardKey)265;
    public static readonly KeyboardKey Down = (KeyboardKey)264;
    public static readonly KeyboardKey Left = (KeyboardKey)263;
    public static readonly KeyboardKey Right = (KeyboardKey)262;
    public static readonly MouseButton MouseLeft = (MouseButton)0;

    /// <summary>Klavesa 1..9 (i = 0 -> '1').</summary>
    public static KeyboardKey Num(int i) => (KeyboardKey)(49 + i);
}

public static class Pal
{
    public static readonly Color Black = new(0, 0, 0, 255);
    public static readonly Color White = new(255, 255, 255, 255);
    public static readonly Color Grass = new(52, 104, 54, 255);
    public static readonly Color Park = new(38, 118, 52, 255);
    public static readonly Color Tree = new(24, 86, 38, 255);
    public static readonly Color Road = new(60, 60, 65, 255);
    public static readonly Color Line = new(225, 205, 90, 255);
    public static readonly Color Sidewalk = new(150, 150, 146, 255);
    public static readonly Color Glass = new(35, 55, 80, 255);
    public static readonly Color Wreck = new(38, 36, 36, 255);
    public static readonly Color Police = new(235, 235, 245, 255);
    public static readonly Color Army = new(82, 104, 60, 255);
    public static readonly Color Blood = new(120, 10, 10, 255);
    public static readonly Color Yellow = new(250, 220, 40, 255);
    public static readonly Color Red = new(230, 40, 40, 255);
    public static readonly Color Blue = new(50, 90, 230, 255);
    public static readonly Color Dark = new(30, 30, 34, 255);
    public static readonly Color Gray = new(110, 110, 116, 255);
    public static readonly Color Fire = new(255, 150, 30, 255);
    public static readonly Color Smoke = new(70, 70, 70, 255);
    public static readonly Color MapBg = new(70, 70, 74, 255);

    public static readonly Color[] Roofs =
    {
        new(122, 92, 82, 255), new(98, 108, 124, 255), new(136, 126, 100, 255),
        new(90, 100, 92, 255), new(140, 100, 100, 255), new(96, 86, 120, 255),
    };

    public static readonly Color[] Cars =
    {
        new(200, 50, 50, 255), new(50, 90, 200, 255), new(220, 200, 60, 255), new(60, 160, 90, 255),
        new(225, 225, 225, 255), new(45, 45, 50, 255), new(230, 130, 40, 255), new(150, 60, 170, 255),
    };
}

public record Weapon(string Name, int Price, float Cooldown, float Damage, int Pellets,
                     float Spread, float Speed, float Range, bool Explosive, int Mag, float ReloadTime);

public static class Weapons
{
    // Name, Price, Cooldown, Damage, Pellets, Spread, Speed, Range, Explosive, Mag (zasobnik), ReloadTime (s)
    // Zaloha naboju je neomezena - po vystrileni zasobniku se prebiji (R nebo automaticky).
    public static readonly Weapon[] All =
    {
        new("Pistole",    0,     0.35f, 25, 1, 0.03f, 900,  650,  false, 12,  1.1f),
        new("SMG",        1500,  0.08f, 13, 1, 0.09f, 950,  600,  false, 30,  1.4f),
        new("Brokovnice", 3500,  0.80f, 14, 8, 0.28f, 800,  330,  false, 6,   2.0f),
        new("Minigun",    9000,  0.035f, 9, 1, 0.12f, 1000, 650,  false, 120, 3.2f),
        new("Granat",     2000,  1.00f, 0,  1, 0.02f, 430,  250,  true,  3,   1.6f),
        new("Raketomet",  15000, 1.10f, 0,  1, 0.01f, 650,  1400, true,  1,   2.2f),
    };
}

/// <summary>Ladici konstanty obtiznosti (pouzite v Game.cs).</summary>
public static class Tune
{
    public const float CopSpawnDelay = 4f;      // s mezi doplnovanim policie
    public const float CarFireCd = 0.9f;        // s mezi vystrely policejniho auta
    public const float CopFireCd = 1.2f;        // s mezi vystrely policisty
    public const float SoldierFireCd = 0.24f;   // s mezi vystrely vojaka
    public const float CopDamageMul = 0.5f;     // nasobek poskozeni aut od strelby
    public const float HeatDecayNear = 1.2f;    // ubytek hledanosti/s kdyz jsou policajti blizko
    public const float HeatDecayFar = 8f;       // ... kdyz jsou daleko (utek)
}

public enum VKind { Civil, Police, Tank }
public enum VModel { Sedan, Van, Bus, Bike, Ambulance }
public enum VState { Alive, Burning, Wreck }
public enum PKind { Civil, Cop, Soldier }
public enum QType { DestroyCars, KillCops, Checkpoint, Survive, Courier, StealCar, Assassinate, Rampage }

public class Vehicle
{
    public Vector2 Pos;
    public float Angle;          // radiany, 0 = vychod, y roste dolu
    public float Speed;
    public float Hp, MaxHp;
    public VKind Kind;
    public VModel Model = VModel.Sedan;
    public VState State = VState.Alive;
    public Color Body;

    public bool PlayerDriven;    // ridi hrac
    public bool Ai;              // ridi AI (civil provoz / policie / armada)
    public bool HitByPlayer;     // pro odmeny za znicene auto
    public bool Blockade;        // policejni zatarasc - stoji na miste a strili

    public float BurnTime, WreckAge, SmokeT, FireT, TurretAngle;
    public int Dir;              // smer civilniho AI (0 V, 1 J, 2 Z, 3 S)
    public int LastNode = -1;
    public float StuckT, ReverseT, ReverseSteer;

    public Vehicle(VKind kind, Vector2 pos, float angle, Color body)
    {
        Kind = kind; Pos = pos; Angle = angle; Body = body; TurretAngle = angle;
        MaxHp = Hp = kind switch { VKind.Tank => 600f, VKind.Police => 110f, _ => 100f };
    }

    public float Length => Kind == VKind.Tank ? 64f : Kind == VKind.Police ? 46f : Model switch
    { VModel.Van => 58f, VModel.Bus => 96f, VModel.Bike => 32f, VModel.Ambulance => 58f, _ => 44f };
    public float Width => Kind == VKind.Tank ? 44f : Kind == VKind.Police ? 25f : Model switch
    { VModel.Van => 28f, VModel.Bus => 34f, VModel.Bike => 14f, VModel.Ambulance => 28f, _ => 24f };
    public float Radius => Kind == VKind.Tank ? 27f : Length * 0.43f;
    public float MaxSpeed => Kind switch
    {
        VKind.Tank => 150f,
        VKind.Police => PlayerDriven ? 380f : 270f,
        _ => Model switch { VModel.Bus => 230f, VModel.Van => 320f, VModel.Bike => 440f, VModel.Ambulance => 350f, _ => 380f }
    };
    public float Accel => Kind switch
    {
        VKind.Tank => 170f,
        VKind.Police => 320f,
        _ => Model switch { VModel.Bus => 220f, VModel.Van => 320f, VModel.Bike => 520f, VModel.Ambulance => 360f, _ => 420f }
    };
    public float TurnRate => Kind switch
    {
        VKind.Tank => 1.4f,
        VKind.Police => 2.6f,
        _ => Model switch { VModel.Bus => 1.9f, VModel.Van => 2.3f, VModel.Bike => 3.4f, VModel.Ambulance => 2.4f, _ => 2.7f }
    };
    public Vector2 Forward => new(MathF.Cos(Angle), MathF.Sin(Angle));
}

public class Person
{
    public Vector2 Pos;
    public float Hp;
    public PKind Kind;
    public Color Col;
    public float Angle, WalkT, FireT, FleeT, DeadT;
    public float AnimWalk, Phase;       // animacni faze chuze (kracejici nohy/ruce)
    public float DeathAng;              // smer, kam postava padla
    public string CivDir = "02_postavy/civilista_a/civ_a";  // varianta spritu civilisty
    public bool Moving, Dead, HitByPlayer;
    public bool Vip;                    // cil mise (zlata postava)
    public Vector2 FleeFrom;

    public Person(PKind kind, Vector2 pos, Color col)
    {
        Kind = kind; Pos = pos; Col = col;
        Hp = kind switch { PKind.Soldier => 100f, PKind.Cop => 40f, _ => 40f };
    }
}

/// <summary>Policejni vrtulnik - leta nad mestem a strili na hrace (az 5 hvezd).</summary>
public class Heli
{
    public Vector2 Pos;
    public float Hp = 170f, MaxHp = 170f;
    public float FireT, Rotor, Bob;
    public bool HitByPlayer;
    public float Radius => 30f;
}

public class Bullet
{
    public Vector2 Pos, Vel;
    public float Damage, Life;
    public bool Player, Explosive, Dead;
    public Vehicle? Ignore;
}

public class Explosion
{
    public const float Dur = 0.6f;
    public Vector2 Pos;
    public float Radius, T;
}

public class Particle
{
    public Vector2 Pos, Vel;
    public float Life, MaxLife, Size;
    public int Kind;             // 0 ohen, 1 koure, 2 jiskra
}

public class Popup
{
    public Vector2 Pos;
    public string Text = "";
    public float Life;
}

public class Quest
{
    public QType Type;
    public float Prog, Time;
    public int Goal, Reward;
    public int Stage;                 // faze mise (kuryr / kradez auta)
    public Vector2 Target, Target2;   // cilove body
    public Person? Vip;               // cil atentatu
    public Vehicle? Car;              // auto ke kradezi
}
