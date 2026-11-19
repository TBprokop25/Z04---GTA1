using System.Numerics;
using Raylib_cs;

namespace Z04;

// Kresleni pres nactene textury (assets). Kdyz textura chybi, vraci false a Game spadne zpet na primitivy.
public partial class Game
{
    const float VehScale = 0.5f;   // assety jsou 2x -> kreslime na pul
    const float PedScale = 0.5f;   // postavy 64x64 @2x

    static readonly string[] SedanNames = { "red", "blue", "yellow", "green", "white", "black", "orange", "purple" };

    static readonly string[] CivDirs =
    {
        "02_postavy/civilista_a/civ_a", "02_postavy/civilista_b/civ_b",
        "02_postavy/civilista_c/civ_c", "02_postavy/civilistka_d/civ_d",
    };

    static int SedanColorIndex(Color c)
    {
        for (int i = 0; i < Pal.Cars.Length; i++)
            if (Pal.Cars[i].R == c.R && Pal.Cars[i].G == c.G && Pal.Cars[i].B == c.B) return i;
        return 0;
    }

    static string? VehiclePath(Vehicle v)
    {
        bool burn = v.State == VState.Burning, wreck = v.State == VState.Wreck;
        string suf = wreck ? "_wreck" : burn ? "_burn" : "";
        if (v.Kind == VKind.Police) return $"01_vozidla/policie/police{suf}.png";
        if (v.Kind == VKind.Tank) return $"01_vozidla/tank/tank_hull{suf}.png";
        return v.Model switch
        {
            VModel.Van => $"01_vozidla/dodavka/van{suf}.png",
            VModel.Bus => $"01_vozidla/autobus/bus{suf}.png",
            VModel.Bike => $"01_vozidla/motorka_s_jezdcem/moto{suf}.png",
            VModel.Ambulance => $"01_vozidla/sanitka/ambulance{suf}.png",
            _ => wreck ? "01_vozidla/sedan_v_plamenech_a_vrak/sedan_wreck.png"
               : burn ? $"01_vozidla/sedan_v_plamenech_a_vrak/sedan_{SedanNames[SedanColorIndex(v.Body)]}_burn.png"
               : $"01_vozidla/sedan/sedan_{SedanNames[SedanColorIndex(v.Body)]}.png",
        };
    }

    bool DrawVehicleSprite(Vehicle v)
    {
        var path = VehiclePath(v);
        if (path == null || !Assets.Have(path)) return false;

        float deg = v.Angle * RadToDeg;
        float wa = v.State == VState.Wreck ? Math.Clamp((18f - v.WreckAge) / 4f, 0f, 1f) : 1f;
        var tint = wa < 1f ? Raylib.Fade(Pal.White, wa) : Pal.White;

        Assets.DrawCentered(path, v.Pos, deg, VehScale, tint);

        if (v.Kind == VKind.Tank)
        {
            string tsuf = v.State == VState.Wreck ? "_wreck" : v.State == VState.Burning ? "_burn" : "";
            Assets.DrawPivot($"01_vozidla/tank/tank_turret{tsuf}.png", v.Pos, v.TurretAngle * RadToDeg, VehScale, new Vector2(42, 38), tint);
        }

        if (v.State == VState.Alive)
        {
            var fwd = v.Forward;
            var right = new Vector2(-fwd.Y, fwd.X);

            if (v.Kind != VKind.Tank && Night > 0.04f)   // nocni reflektory
            {
                float beam = Night * 0.5f;
                var nose = v.Pos + fwd * (v.Length / 2);
                var far = nose + fwd * (90 + Night * 70);
                Raylib.DrawTriangle(nose + right * 7, nose - right * 7, far + right * 34, Raylib.Fade(Pal.Yellow, beam * 0.5f));
                Raylib.DrawTriangle(nose + right * 7, far + right * 34, far - right * 34, Raylib.Fade(Pal.Yellow, beam * 0.5f));
            }

            if (v.Kind == VKind.Police || v.Model == VModel.Ambulance)   // blikajici majak
            {
                bool b = ((int)(animT * 8)) % 2 == 0;
                Raylib.DrawCircleV(v.Pos + right * 5, 7, Raylib.Fade(b ? Pal.Red : Pal.Blue, 0.5f));
                Raylib.DrawCircleV(v.Pos - right * 5, 7, Raylib.Fade(b ? Pal.Blue : Pal.Red, 0.5f));
            }
        }
        return true;
    }

    // ---------- postavy ----------

    static string PersonDir(Person p) => p switch
    {
        _ when p.Kind == PKind.Cop => "02_postavy/policista/police",
        _ when p.Kind == PKind.Soldier => "02_postavy/vojak/soldier",
        _ => p.CivDir,
    };

    bool DrawPersonSprite(Person p)
    {
        string dir = PersonDir(p);
        bool armed = p.Kind != PKind.Civil;
        string frame = armed && Stars > 0 ? "aim" : WalkFrame(p.AnimWalk);
        string path = $"{dir}_{frame}.png";
        if (!Assets.Have(path)) return false;
        Assets.DrawCentered(path, p.Pos, p.Angle * RadToDeg, PedScale, Pal.White);
        return true;
    }

    bool DrawPlayerSprite()
    {
        const string dir = "02_postavy/hrac/player";
        bool moving = pWalkMoving;
        string frame = holstered ? (moving ? WalkFrame(pWalk) : "holster") : "aim";
        string path = $"{dir}_{frame}.png";
        if (!Assets.Have(path)) return false;
        Assets.DrawCentered(path, pPos, pAngle * RadToDeg, PedScale, Pal.White);
        return true;
    }

    static string WalkFrame(float animWalk) => "walk_" + (((int)(animWalk * 2f)) & 7);

    static readonly string[] WeaponIcons = { "w_pistol", "w_smg", "w_shotgun", "w_minigun", "w_grenade", "w_rocket" };
    static string WeaponIconPath(int i) => i >= 0 && i < WeaponIcons.Length ? $"05_ikony/zbrane/{WeaponIcons[i]}.png" : "";
}
