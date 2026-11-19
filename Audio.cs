using Raylib_cs;

namespace Z04;

public enum Snd { Shot, Smg, Shotgun, Explosion, Pickup, Coin, Hurt, Siren, Nitro, Quest, Bank, Click, Engine }

/// <summary>
/// Procedurálně generované zvuky - nepotřebují žádné externí soubory.
/// Vlnky se vytvoří v paměti, zapíšou jako .wav do složky u exe a načtou přes Raylib.
/// Pokud se audio zařízení nepodaří otevřít, všechno je tiché (žádné pády).
/// </summary>
public static class Sfx
{
    const int Rate = 22050;
    static bool _ready;
    static float _vol = 0.6f;
    static readonly Dictionary<Snd, Sound> _snd = new();
    static bool _enginePlaying;

    public static void Init()
    {
        try
        {
            Raylib.InitAudioDevice();
            if (!Raylib.IsAudioDeviceReady()) return;

            string dir = Path.Combine(AppContext.BaseDirectory, "sfx");
            Directory.CreateDirectory(dir);

            Add(dir, Snd.Shot, Shot(1600, 0.09f, 0.45f));
            Add(dir, Snd.Smg, Shot(2200, 0.05f, 0.35f));
            Add(dir, Snd.Shotgun, Shot(900, 0.18f, 0.6f));
            Add(dir, Snd.Explosion, Explosion());
            Add(dir, Snd.Pickup, Arp(new[] { 660f, 990f }, 0.12f, 0.4f));
            Add(dir, Snd.Coin, Arp(new[] { 1200f, 1600f }, 0.07f, 0.4f));
            Add(dir, Snd.Hurt, Tone(140, 0.15f, 0.5f, 2));
            Add(dir, Snd.Siren, Siren());
            Add(dir, Snd.Nitro, Nitro());
            Add(dir, Snd.Quest, Arp(new[] { 523f, 659f, 784f, 1046f }, 0.10f, 0.45f));
            Add(dir, Snd.Bank, Arp(new[] { 1046f, 880f, 660f, 1320f }, 0.11f, 0.45f));
            Add(dir, Snd.Click, Tone(880, 0.04f, 0.3f, 0));
            Add(dir, Snd.Engine, Engine());

            _ready = true;
        }
        catch { _ready = false; }
    }

    static void Add(string dir, Snd id, short[] samples)
    {
        string path = Path.Combine(dir, id + ".wav");
        File.WriteAllBytes(path, Wav(samples));
        _snd[id] = Raylib.LoadSound(path);
    }

    public static void Play(Snd id, float pitch = 1f, float vol = 1f)
    {
        if (!_ready || !_snd.TryGetValue(id, out var s)) return;
        Raylib.SetSoundPitch(s, pitch);
        Raylib.SetSoundVolume(s, _vol * vol);
        Raylib.PlaySound(s);
    }

    /// <summary>Smyčka motoru - přehrává se dokola, výška podle rychlosti (0..1).</summary>
    public static void Engine(bool on, float speedFrac)
    {
        if (!_ready || !_snd.TryGetValue(Snd.Engine, out var s)) return;
        if (on)
        {
            Raylib.SetSoundPitch(s, 0.7f + speedFrac * 1.1f);
            Raylib.SetSoundVolume(s, _vol * 0.35f);
            if (!Raylib.IsSoundPlaying(s)) { Raylib.PlaySound(s); }
            _enginePlaying = true;
        }
        else if (_enginePlaying)
        {
            Raylib.StopSound(s);
            _enginePlaying = false;
        }
    }

    public static void Shutdown()
    {
        if (!_ready) return;
        foreach (var s in _snd.Values) Raylib.UnloadSound(s);
        Raylib.CloseAudioDevice();
    }

    // ---------- generátory vln ----------

    static readonly Random R = new(12345);

    static short[] Tone(float freq, float dur, float amp, int wave)
    {
        int n = (int)(Rate * dur);
        var s = new short[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float env = MathF.Min(1f, 20f * t) * MathF.Max(0f, 1f - i / (float)n);
            float ph = freq * t;
            float v = wave switch
            {
                1 => MathF.Sign(MathF.Sin(ph * MathF.Tau)),              // obdelnik
                2 => 2f * MathF.Abs(2f * (ph - MathF.Floor(ph + 0.5f))) - 1f, // trojuhelnik
                _ => MathF.Sin(ph * MathF.Tau),                         // sinus
            };
            s[i] = (short)(v * env * amp * 32000);
        }
        return s;
    }

    static short[] Shot(float freq, float dur, float amp)
    {
        int n = (int)(Rate * dur);
        var s = new short[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float env = MathF.Pow(1f - t, 2.2f);
            float noise = (float)(R.NextDouble() * 2 - 1);
            float tone = MathF.Sin(i / (float)Rate * freq * (1f - t) * MathF.Tau);
            s[i] = (short)((noise * 0.6f + tone * 0.4f) * env * amp * 32000);
        }
        return s;
    }

    static short[] Explosion()
    {
        float dur = 0.7f;
        int n = (int)(Rate * dur);
        var s = new short[n];
        float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float env = MathF.Pow(1f - t, 1.6f);
            float noise = (float)(R.NextDouble() * 2 - 1);
            lp += (noise - lp) * 0.08f;                 // dolni propust -> dunivy bas
            float rumble = MathF.Sin(i / (float)Rate * 60f * MathF.Tau);
            s[i] = (short)((lp * 0.8f + rumble * 0.3f) * env * 0.9f * 32000);
        }
        return s;
    }

    static short[] Arp(float[] freqs, float each, float amp)
    {
        int per = (int)(Rate * each);
        var s = new short[per * freqs.Length];
        for (int k = 0; k < freqs.Length; k++)
            for (int i = 0; i < per; i++)
            {
                float env = MathF.Min(1f, 10f * (i / (float)per)) * MathF.Max(0f, 1f - i / (float)per);
                float v = MathF.Sin(i / (float)Rate * freqs[k] * MathF.Tau);
                s[k * per + i] = (short)(v * env * amp * 32000);
            }
        return s;
    }

    static short[] Siren()
    {
        float dur = 0.9f;
        int n = (int)(Rate * dur);
        var s = new short[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float freq = t < 0.5f ? 760f : 560f;
            float v = MathF.Sin(i / (float)Rate * freq * MathF.Tau);
            s[i] = (short)(v * 0.4f * 32000);
        }
        return s;
    }

    static short[] Nitro()
    {
        float dur = 0.5f;
        int n = (int)(Rate * dur);
        var s = new short[n];
        float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float noise = (float)(R.NextDouble() * 2 - 1);
            lp += (noise - lp) * (0.02f + t * 0.3f);     // otevirajici se filtr = whoosh
            float env = MathF.Sin(t * MathF.PI);
            s[i] = (short)(lp * env * 0.5f * 32000);
        }
        return s;
    }

    static short[] Engine()
    {
        float dur = 0.25f;                                // kratka smycka
        int n = (int)(Rate * dur);
        var s = new short[n];
        for (int i = 0; i < n; i++)
        {
            float ph = i / (float)Rate;
            float v = MathF.Sin(ph * 90f * MathF.Tau) * 0.5f
                    + MathF.Sin(ph * 180f * MathF.Tau) * 0.3f
                    + (float)(R.NextDouble() * 2 - 1) * 0.1f;
            s[i] = (short)(v * 0.6f * 32000);
        }
        return s;
    }

    // ---------- zápis WAV (16-bit PCM mono) ----------

    static byte[] Wav(short[] samples)
    {
        int dataLen = samples.Length * 2;
        var b = new byte[44 + dataLen];
        void Str(int o, string v) { for (int i = 0; i < v.Length; i++) b[o + i] = (byte)v[i]; }
        void I32(int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }
        void I16(int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }

        Str(0, "RIFF"); I32(4, 36 + dataLen); Str(8, "WAVE");
        Str(12, "fmt "); I32(16, 16); I16(20, 1); I16(22, 1);
        I32(24, Rate); I32(28, Rate * 2); I16(32, 2); I16(34, 16);
        Str(36, "data"); I32(40, dataLen);
        for (int i = 0; i < samples.Length; i++) I16(44 + i * 2, samples[i]);
        return b;
    }
}
