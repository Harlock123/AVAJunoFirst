namespace AVAJunoFirst.Engine;

/// <summary>
/// The computer player, for the demonstration (and the tests). It lines up under the nearest enemy ahead, leading it
/// by where it will be when a shot gets there, and fires; the astronaut, when there's no bonus running, comes
/// first. It backs off from anything that gets too close, sidesteps shots coming at it, runs forward to find the
/// enemy when there's none near, and warps when something is about to hit it that it can't avoid.
/// </summary>
public sealed class ShipPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    /// <summary>It looks again only every so often.</summary>
    public const int LookEvery = 5;

    private int _tick, _dz, _dodge, _dodgeFor;
    private Enemy? _target;

    public Controls Decide(GameEngine g)
    {
        _tick++;
        var s = g.Ship;
        if (g.Phase is not (Phase.Play or Phase.Start) || !s.Alive) { Doing = "waiting"; return default; }
        if (_tick % LookEvery == 0 || _target is not { Dead: false } || !g.Enemies.Contains(_target)) _target = Choose(g);

        // Shots and enemies about to arrive: sidestep, or warp if there's no getting out of the way.
        int dodge = 0;
        bool warp = false;
        foreach (var x in g.Shots)
        {
            double t = x.Z / Math.Max(1, -x.Vz + s.Vz);
            if (t < 0 || t > 0.7) continue;
            double at = x.X + x.Vx * t;
            if (Math.Abs(at - s.X) < Ship.Size + 0.05) dodge = at > s.X ? -1 : 1;
        }
        foreach (var e in g.Enemies)
        {
            if (e.Kind == EnemyKind.Astronaut || e.Z < -0.3 || e.Z > 2.2) continue;
            if (Math.Abs(e.X - s.X) < e.Size + Ship.Size + 0.04)
            {
                if (dodge == 0) dodge = e.X > s.X ? -1 : 1;
                if (e.Z < 0.9 && s.Safe <= 0 && s.WarpReady) warp = true;
            }
        }
        if (dodge != 0 && Math.Abs(s.X + dodge * 0.1) > 0.9) dodge = -dodge;
        // Once it picks a way to sidestep, it keeps going that way a moment: deciding afresh every frame, its own
        // step would flip the answer and leave it dithering on the spot.
        if (_dodgeFor > 0) { _dodgeFor--; if (Math.Abs(s.X + _dodge * 0.05) < 0.92) dodge = _dodge; }
        else if (dodge != 0) { _dodge = dodge; _dodgeFor = 15; }

        int dx = 0, dz;
        bool fire = false;
        if (_target is { } e2)
        {
            double t = e2.Z / GameEngine.BoltSpeed, at = Math.Clamp(e2.X + e2.Vx * t, -0.95, 0.95);
            if (Math.Abs(at - s.X) > 0.012) dx = Math.Sign(at - s.X);
            fire = Math.Abs(at - s.X) < e2.Size + 0.012 && e2.Z < GameEngine.Range;
            Doing = dodge != 0 ? "dodging" : e2.Kind == EnemyKind.Astronaut ? "astronaut" : "shooting";
        }
        else Doing = "searching";
        // Too close for comfort: back off. Nothing near: go and find them.
        double nearest = g.Enemies.Where(e => e.Kind != EnemyKind.Astronaut && e.Z > -0.3).Select(e => e.Z).DefaultIfEmpty(99).Min();
        dz = nearest < 3.2 ? -1 : nearest > GameEngine.Range * 0.75 ? 1 : 0;
        if (_tick % 10 == 0) _dz = dz;
        if (dodge != 0) dx = dodge;
        return new Controls(dx, _dz, fire, warp);
    }

    private static Enemy? Choose(GameEngine g)
    {
        var s = g.Ship;
        Enemy? best = null;
        double cost = double.MaxValue;
        foreach (var e in g.Enemies)
        {
            if (e.Z < 0.6 || e.Z > 60) continue;
            double c = e.Z + Math.Abs(e.X - s.X) * 12;
            if (e.Kind == EnemyKind.Astronaut) c = g.BonusTime > 0 ? c * 3 : c * 0.4;
            if (e.Kind is EnemyKind.Spark or EnemyKind.Hopper) c *= 0.7;
            if (c < cost) { cost = c; best = e; }
        }
        return best;
    }
}
