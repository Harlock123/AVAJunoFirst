namespace AVAJunoFirst.Engine;

/// <summary>The controls: left and right, forward (1) and back (-1), fire, and warp.</summary>
public readonly record struct Controls(int Dx = 0, int Dz = 0, bool Fire = false, bool Warp = false);

/// <summary>
/// The screen, and the grid seen in perspective from just behind and above the ship. A place on the grid is
/// <c>x</c> across (-1 to 1 is the track) and <c>z</c> ahead of the ship.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768, CX = Width / 2;
    public const double Horizon = 150, ShipY = 690, Camera = 6;
    public const double Depth = (ShipY - Horizon) * Camera, Spread = 400 * Camera;
    /// <summary>How far ahead anything can be seen.</summary>
    public const double Far = 42;
    public const double Edge = 1;

    /// <summary>Where a point on the grid shows on screen, and how big things there look (1 at the ship).</summary>
    public static (double X, double Y, double Scale) Project(double x, double z)
    {
        double d = Math.Max(0.05, z + Camera);
        return (CX + x * Spread / d, Horizon + Depth / d, Camera / d);
    }

    public static bool Visible(double z) => z > -0.6 && z < Far;
}

/// <summary>The player's ship, at the front of the view.</summary>
public sealed class Ship
{
    public double X, Vz;
    public bool Alive = true, WarpReady = true;
    public double Safe, FireIn, Bank;
    public const double Size = 0.075;
}

/// <summary>A shot of the ship's, running away up the grid.</summary>
public sealed class Bolt
{
    public double X, Z;
    public bool Dead;
}

public enum EnemyKind
{
    /// <summary>A spinning vane: hovers at its place on the grid, dives at the ship, and pulls back to come again.</summary>
    Vane,
    /// <summary>A drone: holds its place in a formation up the grid, creeping down it, sliding to and fro and firing.</summary>
    Drone,
    /// <summary>A hopper: sits, then leaps forward at the ship.</summary>
    Hopper,
    /// <summary>A pod: hangs back and lets loose sparks. Takes two hits.</summary>
    Pod,
    /// <summary>A spark from a pod: small, quick, and after the ship.</summary>
    Spark,
    /// <summary>The astronaut: harmless. Shoot it, and every kill for a while after scores more than the last.</summary>
    Astronaut,
}

public sealed class Enemy
{
    public EnemyKind Kind;
    public double X, Z, Vx, Vz, T, Phase, Home, State, FireIn = 2, Life = 8;
    public int Hp = 1, Mode;
    public bool Dead;

    public double Size => Kind switch { EnemyKind.Pod => 0.09, EnemyKind.Spark => 0.04, EnemyKind.Drone => 0.065, _ => 0.07 };
    public int Points => Kind switch
    {
        EnemyKind.Vane => 100, EnemyKind.Drone => 150, EnemyKind.Hopper => 200, EnemyKind.Pod => 300, EnemyKind.Spark => 50, _ => 500,
    };
    /// <summary>One of the wave's own: the wave is over when they're all gone.</summary>
    public bool Counts => Kind is not (EnemyKind.Spark or EnemyKind.Astronaut);
}

/// <summary>An enemy's shot, coming down the grid at the ship.</summary>
public sealed class Shot
{
    public double X, Z, Vx, Vz, Life = 5;
    public bool Dead;
}

/// <summary>A fleck of something blown up, flying out on the screen.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, Age, Life = 1;
    public uint Colour;
}

public enum EffectKind { Score, Burst, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
