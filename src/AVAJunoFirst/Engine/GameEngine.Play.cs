namespace AVAJunoFirst.Engine;

public sealed partial class GameEngine
{
    public const double Lateral = 1.5, Accel = 16, MaxForward = 9, MaxBack = 5, Settle = 7;
    public const double BoltSpeed = 46, FireGap = 0.14, Range = 17;
    public const int MaxBolts = 4;
    public const double WaveSeconds = 45, StartSeconds = 2, DyingSeconds = 2.2, SafeSeconds = 2, ClearSeconds = 3, BonusSeconds = 8;
    public const int FirstExtraLife = 10000, ExtraLifeEvery = 30000;

    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Ship Ship { get; } = new();
    public List<Bolt> Bolts { get; } = [];
    public List<Enemy> Enemies { get; } = [];
    public List<Shot> Shots { get; } = [];
    public List<Debris> Wreckage { get; } = [];
    public ShipPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    /// <summary>The wave's clock: when it runs out, what's left turns furious.</summary>
    public double TimeLeft { get; private set; }
    public bool Fury => TimeLeft <= 0;
    /// <summary>How far the grid has run past, for drawing its bands.</summary>
    public double Travel { get; private set; }
    /// <summary>The astronaut's bonus: how long it lasts yet, and what the next kill is multiplied by.</summary>
    public double BonusTime { get; private set; }
    public int Multiplier { get; private set; } = 1;
    public int Bonus { get; private set; }
    /// <summary>For tests: the enemies hold still and hold their fire, and no astronaut comes.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra;
    private bool _warpPrev, _astronautDue;

    public int Left => Enemies.Count(e => e.Counts);
    public double Pace => Math.Min(1.7, 1 + 0.07 * (Wave - 1));
    public double ShotSpeed => Math.Min(13, 8 + 0.5 * (Wave - 1));

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        _nextExtra = FirstExtraLife;
        Wreckage.Clear();
        Ship.WarpReady = true;
        SetPhase(Phase.Start);
    }

    /// <summary>A wave: its enemies spread up the grid ahead, the drones in their formations.</summary>
    private void BeginWave(int wave)
    {
        Wave = wave;
        Enemies.Clear();
        Shots.Clear();
        Bolts.Clear();
        int total = Math.Min(56, 24 + 4 * wave), drones = wave >= 2 ? Math.Min(12, 4 * (wave / 2)) : 0;
        int hoppers = wave >= 2 ? Math.Min(10, 2 + wave) : 0, pods = wave >= 3 ? Math.Min(4, wave - 2) : 0;
        int vanes = Math.Max(6, total - drones - hoppers - pods);
        for (int i = 0; i < vanes; i++) Add(EnemyKind.Vane, _rng.NextDouble() * 1.6 - 0.8, 18 + i * 3.2 + _rng.NextDouble() * 4);
        for (int g = 0; g < drones / 4; g++)
            for (int i = 0; i < 4; i++)
            {
                var d = Add(EnemyKind.Drone, -0.45 + i * 0.3, 26 + g * 22);
                d.Phase = g * 1.7;
                d.State = -0.45 + i * 0.3;
            }
        for (int i = 0; i < hoppers; i++) Add(EnemyKind.Hopper, _rng.NextDouble() * 1.6 - 0.8, 24 + i * 4);
        for (int i = 0; i < pods; i++) { var p = Add(EnemyKind.Pod, -0.5 + i * 0.35, 36 + i * 6); p.Hp = 2; p.FireIn = 3 + i; }
        _astronautDue = true;
        TimeLeft = WaveSeconds;
        BonusTime = 0;
        Multiplier = 1;
        Ship.X = 0;
        Ship.Vz = 0;
        SetPhase(Phase.Start);
        Say($"WAVE {wave}", $"{Left} TO DESTROY", StartSeconds + 0.3);
        Play(Sfx.WaveStart);
    }

    /// <summary>An enemy at a place on the grid: its anchor, in distance along the grid, is where it hovers.</summary>
    private Enemy Add(EnemyKind kind, double x, double z)
    {
        var e = new Enemy { Kind = kind, X = x, Z = z, Home = Travel + z, Phase = _rng.NextDouble() * 6, FireIn = 1.5 + _rng.NextDouble() * 3, State = 2 + _rng.NextDouble() * 4 };
        Enemies.Add(e);
        return e;
    }

    private void Simulate(double dt, Controls c)
    {
        MoveDebris(dt);
        switch (Phase)
        {
            case Phase.Start:
                Steer(dt, c);
                if (PhaseTime >= StartSeconds) { SetPhase(Phase.Play); Music(true); }
                break;
            case Phase.Play:
                Steer(dt, c);
                Fire(dt, c);
                Warp(c);
                Clock(dt);
                MoveEnemies(dt);
                MoveShots(dt);
                Hits();
                if (Phase == Phase.Play && Left == 0) Clear();
                break;
            case Phase.Dying:
                MoveEnemies(dt);
                MoveShots(dt);
                if (PhaseTime >= DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Respawn();
                }
                break;
            case Phase.Clear:
                Steer(dt, default);
                if (PhaseTime >= 1 && PhaseTime - dt < 1 && Bonus > 0) { Score += Bonus; CheckExtra(); }
                if (PhaseTime >= ClearSeconds) BeginWave(Wave + 1);
                break;
        }
        PhaseTime += dt;
    }

    // ───────────────────────────── the ship ─────────────────────────────

    /// <summary>Across the track, and forward or back: going forward runs everything down the grid towards the ship.</summary>
    private void Steer(double dt, Controls c)
    {
        var s = Ship;
        s.Safe = Math.Max(0, s.Safe - dt);
        s.X = Math.Clamp(s.X + c.Dx * Lateral * dt, -Field.Edge + 0.05, Field.Edge - 0.05);
        s.Bank += (c.Dx - s.Bank) * Math.Min(1, 8 * dt);
        if (c.Dz != 0) s.Vz = Math.Clamp(s.Vz + c.Dz * Accel * dt, -MaxBack, MaxForward);
        else s.Vz -= Math.Sign(s.Vz) * Math.Min(Math.Abs(s.Vz), Settle * dt);
        double moved = s.Vz * dt;
        Travel += moved;
        foreach (var e in Enemies) e.Z -= moved;
        foreach (var x in Shots) x.Z -= moved;
    }

    private void Fire(double dt, Controls c)
    {
        var s = Ship;
        s.FireIn -= dt;
        if (!c.Fire || s.FireIn > 0 || Bolts.Count >= MaxBolts) return;
        s.FireIn = FireGap;
        Bolts.Add(new Bolt { X = s.X, Z = 0.3 });
        Play(Sfx.Fire);
    }

    /// <summary>
    /// The warp, once a life: a jump to elsewhere on the track, everything close by flung back up the grid, and a
    /// moment's safety.
    /// </summary>
    private void Warp(Controls c)
    {
        bool pressed = c.Warp && !_warpPrev;
        _warpPrev = c.Warp;
        var s = Ship;
        if (!pressed || !s.WarpReady) return;
        s.WarpReady = false;
        s.X = Math.Clamp(-s.X + (_rng.NextDouble() - 0.5) * 0.4, -0.85, 0.85);
        s.Safe = 1.5;
        foreach (var e in Enemies.Where(e => e.Z < 6)) { e.Z += 14; e.Home += 14; }
        Shots.Clear();
        Play(Sfx.Warp);
        Effects.Add(new Effect { Kind = EffectKind.Word, X = Field.CX, Y = 560, Text = "WARP", Life = 1, Colour = 0xFF8AE0FF });
    }

    private void Clock(double dt)
    {
        bool was = Fury;
        TimeLeft = Math.Max(0, TimeLeft - dt);
        if (Fury && !was)
        {
            Play(Sfx.Fury);
            Say("TIME UP", "THEY'RE FURIOUS NOW", 2);
        }
        if (BonusTime > 0 && (BonusTime -= dt) <= 0) Multiplier = 1;
        // The astronaut drifts in a little way into the wave.
        if (_astronautDue && !Calm && TimeLeft < WaveSeconds - 12)
        {
            _astronautDue = false;
            var a = Add(EnemyKind.Astronaut, _rng.NextDouble() * 1.2 - 0.6, 30);
            a.Vx = 0.25;
        }
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    private void MoveEnemies(double dt)
    {
        var s = Ship;
        double pace = Pace * (Fury ? 1.5 : 1);
        foreach (var e in Enemies.ToList())
        {
            e.T += dt;
            if (Calm) continue;
            switch (e.Kind)
            {
                case EnemyKind.Vane:
                    // Hovers about its place on the grid, weaving; every so often, if the ship is near enough, it
                    // dives across at it, then pulls back up to its place to come again.
                    if (e.Mode == 0)
                    {
                        e.Vz = Math.Clamp((e.Home - Travel - e.Z) * 1.2, -3, 3) * pace;
                        e.Vx = Math.Sin(e.T * 2 + e.Phase) * 0.55;
                        e.State -= dt * (Fury ? 3 : 1);
                        if (e.State <= 0 && e.Z < 16) e.Mode = 1;
                    }
                    else if (e.Mode == 1)
                    {
                        e.Vz = -5.5 * pace;
                        e.Vx = Math.Clamp((s.X - e.X) * 3, -1.3, 1.3) * pace;
                        if (e.Z < 0.4) { e.Mode = 2; e.State = 0; }
                    }
                    else
                    {
                        e.Vz = 7 * pace;
                        e.Vx = Math.Sin(e.T * 3) * 0.4;
                        if ((e.State += dt) > 1.3) { e.Mode = 0; e.State = (2.5 + _rng.NextDouble() * 4) / pace; }
                    }
                    break;
                case EnemyKind.Drone:
                    // Holds its place on the grid, creeping slowly down it, the formation sliding to and fro.
                    e.Home -= dt * (Fury ? 3 : 0.5);
                    e.Vz = Math.Clamp((e.Home - Travel - e.Z) * 1.5, -6, 6) * pace;
                    e.Vx = (e.State + Math.Sin(e.T * 0.8 + e.Phase) * 0.45 - e.X) * 2;
                    break;
                case EnemyKind.Hopper:
                    // Sits a moment, then leaps forward and across.
                    e.State -= dt;
                    if (e.State <= 0)
                    {
                        e.Mode = 1 - e.Mode;
                        e.State = e.Mode == 1 ? 0.45 : 1.1 / pace;
                        if (e.Mode == 1) e.Vx = Math.Clamp((s.X - e.X) * 1.6, -0.9, 0.9);
                    }
                    e.Vz = e.Mode == 1 ? -10 * pace : 0;
                    if (e.Mode == 0) e.Vx = 0;
                    break;
                case EnemyKind.Pod:
                    e.Vz = Math.Clamp((14 - e.Z) * 0.6, -1.5, 1.5);
                    e.Vx = Math.Sin(e.T * 0.5 + e.Phase) * 0.25;
                    if ((e.FireIn -= dt) <= 0 && Phase == Phase.Play && Field.Visible(e.Z))
                    {
                        e.FireIn = Math.Max(3, 6 - 0.3 * Wave) / (Fury ? 1.5 : 1);
                        for (int i = 0; i < 2; i++) Add(EnemyKind.Spark, e.X + (i == 0 ? -0.06 : 0.06), e.Z - 0.3);
                        Play(Sfx.Spawn);
                    }
                    break;
                case EnemyKind.Spark:
                    e.Vz = -7 * pace;
                    e.Vx = Math.Clamp((s.X - e.X) * 2.5, -1.2, 1.2);
                    if ((e.Life -= dt) <= 0) e.Dead = true;
                    break;
                case EnemyKind.Astronaut:
                    e.Vz = Math.Clamp((18 - e.Z) * 0.4, -2, 2);
                    if (Math.Abs(e.X) > 0.8) e.Vx = -Math.Sign(e.X) * Math.Abs(e.Vx);
                    break;
            }
            // Furious, they all close in.
            if (Fury && e.Kind is EnemyKind.Vane) e.Home -= 3 * dt;
            e.X = Math.Clamp(e.X + e.Vx * dt, -Field.Edge, Field.Edge);
            e.Z += e.Vz * dt;
            // Past the ship and gone behind: it comes round again from far up the grid; a spark just goes.
            if (e.Z < -1.5)
            {
                if (e.Kind == EnemyKind.Spark) e.Dead = true;
                else { e.Z += 40; e.Mode = 0; e.Home = Travel + e.Z; }
            }
            if (e.Z > 70) e.Z = 70;
            // Fire, if it's in range and in front of the ship.
            if (e.Kind is EnemyKind.Vane or EnemyKind.Drone && Phase == Phase.Play && e.Z > 3 && e.Z < 26)
            {
                e.FireIn -= dt;
                if (e.FireIn <= 0)
                {
                    e.FireIn = (e.Kind == EnemyKind.Drone ? 3 : 4.2) * (0.7 + _rng.NextDouble() * 0.6) / (Fury ? 1.6 : 1) / Math.Min(1.4, Pace);
                    // Aimed, roughly: closer to the mark as the waves go on.
                    double t = e.Z / ShotSpeed, aim = Math.Min(0.9, 0.55 + 0.06 * Wave) + (_rng.NextDouble() - 0.5) * 0.3;
                    Shots.Add(new Shot { X = e.X, Z = e.Z, Vz = -ShotSpeed, Vx = (s.X - e.X) / t * aim });
                    Play(Sfx.EnemyShot);
                }
            }
        }
        Enemies.RemoveAll(e => e.Dead);
    }

    private void MoveShots(double dt)
    {
        foreach (var x in Shots)
        {
            x.X += x.Vx * dt;
            x.Z += x.Vz * dt;
            x.Life -= dt;
            if (x.Z < -1 || x.Life <= 0) x.Dead = true;
        }
        Shots.RemoveAll(x => x.Dead);
    }

    // ───────────────────────────── hits ─────────────────────────────

    private void Hits()
    {
        var s = Ship;
        foreach (var b in Bolts)
        {
            double from = b.Z;
            b.Z += BoltSpeed * Field.Step;
            if (b.Z > Range) { b.Dead = true; continue; }
            // Whatever it passed through on the way, the nearest first.
            var e = Enemies.Where(e => !e.Dead && Math.Abs(e.X - b.X) < e.Size + 0.02 && e.Z >= from - 0.4 && e.Z <= b.Z + 0.4).OrderBy(e => e.Z).FirstOrDefault();
            if (e == null) continue;
            b.Dead = true;
            if (--e.Hp > 0) { Burst(e, 0xFFFFFFFF, 6); continue; }
            Kill(e);
        }
        Bolts.RemoveAll(b => b.Dead);
        Enemies.RemoveAll(e => e.Dead);
        if (!s.Alive || s.Safe > 0) return;
        foreach (var e in Enemies)
            if (e.Kind != EnemyKind.Astronaut && Math.Abs(e.Z) < 0.45 && Math.Abs(e.X - s.X) < e.Size + Ship.Size) { Die(e.Kind.ToString().ToLowerInvariant()); return; }
        foreach (var x in Shots)
            if (Math.Abs(x.Z) < 0.35 && Math.Abs(x.X - s.X) < Ship.Size + 0.02) { x.Dead = true; Die("shot"); return; }
    }

    private void Kill(Enemy e)
    {
        e.Dead = true;
        if (e.Kind == EnemyKind.Astronaut)
        {
            // The bonus starts: each kill from now, for a while, worth one more times as much as the last.
            BonusTime = BonusSeconds;
            Multiplier = 2;
            Award(e.Points, e);
            Play(Sfx.Astronaut);
            var (x, y, _) = Field.Project(e.X, e.Z);
            Effects.Add(new Effect { Kind = EffectKind.Word, X = x, Y = y - 30, Text = "BONUS!", Life = 1.4, Colour = 0xFF8AE0FF });
            return;
        }
        int points = e.Points * (BonusTime > 0 ? Multiplier : 1);
        if (BonusTime > 0) Multiplier = Math.Min(8, Multiplier + 1);
        Award(points, e);
        Burst(e, Colour(e.Kind), 14);
        Play(Sfx.Kill);
    }

    public static uint Colour(EnemyKind kind) => kind switch
    {
        EnemyKind.Vane => 0xFFFF9A3A, EnemyKind.Drone => 0xFF5CE1FF, EnemyKind.Hopper => 0xFF72FF8F, EnemyKind.Pod => 0xFFC08AFF,
        EnemyKind.Spark => 0xFFFFE14A, _ => 0xFFF2F2FF,
    };

    private void Die(string cause)
    {
        var s = Ship;
        s.Alive = false;
        Cause = cause;
        Lives--;
        Music(false);
        Play(Sfx.Death);
        var (x, y, _) = Field.Project(s.X, 0);
        for (int i = 0; i < 30; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = 60 + _rng.NextDouble() * 260;
            Wreckage.Add(new Debris { X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Life = 0.6 + _rng.NextDouble(), Colour = i % 2 == 0 ? 0xFFF2F2FF : 0xFFFF6A3A });
        }
        SetPhase(Phase.Dying);
        if (Lives <= 0) Say("GAME OVER", null, DyingSeconds);
    }

    /// <summary>Back on the grid, everything close by pushed back, the warp ready again.</summary>
    private void Respawn()
    {
        var s = Ship;
        s.Alive = true;
        s.X = 0;
        s.Vz = 0;
        s.Safe = SafeSeconds;
        s.WarpReady = true;
        foreach (var e in Enemies.Where(e => e.Z < 8)) { e.Z += 16; e.Home += 16; e.Mode = 0; }
        Shots.Clear();
        Bolts.Clear();
        SetPhase(Phase.Play);
        Music(true);
    }

    private void Clear()
    {
        Bonus = (int)TimeLeft * 20;
        Shots.Clear();
        Enemies.Clear();
        Music(false);
        Play(Sfx.WaveClear);
        Say($"WAVE {Wave} CLEARED", Bonus > 0 ? $"TIME BONUS {Bonus}" : null, ClearSeconds);
        SetPhase(Phase.Clear);
    }

    private void Award(int points, Enemy e)
    {
        Score += points;
        var (x, y, _) = Field.Project(e.X, e.Z);
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y - 16, Text = points.ToString(), Life = 0.8, Colour = BonusTime > 0 ? 0xFF8AE0FF : 0xFFFFE14A });
        CheckExtra();
    }

    private void CheckExtra()
    {
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    private void Burst(Enemy e, uint colour, int n)
    {
        var (x, y, k) = Field.Project(e.X, e.Z);
        for (int i = 0; i < n; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = (40 + _rng.NextDouble() * 200) * Math.Max(0.3, k);
            Wreckage.Add(new Debris { X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Life = 0.4 + _rng.NextDouble() * 0.5, Colour = colour });
        }
    }

    private void MoveDebris(double dt)
    {
        foreach (var d in Wreckage)
        {
            d.Age += dt;
            d.X += d.Vx * dt;
            d.Y += d.Vy * dt;
        }
        Wreckage.RemoveAll(d => d.Age >= d.Life);
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into a wave, past its number.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    internal Enemy Put(EnemyKind kind, double x, double z)
    {
        var e = Add(kind, x, z);
        if (kind == EnemyKind.Pod) e.Hp = 2;
        return e;
    }

    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
    internal void SetTime(double t) => TimeLeft = t;
}
