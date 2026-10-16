using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAJunoFirst.Engine;
using Effect = AVAJunoFirst.Engine.Effect;

namespace AVAJunoFirst.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAJunoFirst/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    /// <summary>The ground's colours, wave by wave: the two bands that alternate down the track, and its edge.</summary>
    private static readonly (uint A, uint B, uint Edge)[] Palettes =
    [
        (0xFF1A2A6A, 0xFF10184A, 0xFF5CE1FF), (0xFF5A1A4A, 0xFF380E30, 0xFFFF5AD8), (0xFF1A4A2A, 0xFF0E301A, 0xFF72FF8F),
        (0xFF5A3A10, 0xFF3A2208, 0xFFFFB347), (0xFF2A1A5A, 0xFF180E3A, 0xFFB08AFF),
    ];

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF02030A), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame || g.State == GameState.Title)
        {
            DrawSky(dc);
            DrawGround(dc, g.Travel + (g.State == GameState.Title ? _clock * 4 : 0), Palettes[(g.Wave - 1) % Palettes.Length], g.Fury && InGame);
        }
        if (InGame)
        {
            foreach (var b in g.Bolts) DrawBolt(dc, b);
            var things = g.Enemies.Where(e => Field.Visible(e.Z)).OrderByDescending(e => e.Z).ToList();
            foreach (var e in things)
            {
                var (x, y, k) = Field.Project(e.X, e.Z);
                DrawEnemy(dc, e.Kind, x, y - 30 * k, k, e.T + e.Phase, g.Fury, e.Hp);
            }
            foreach (var s in g.Shots.Where(s => Field.Visible(s.Z)))
            {
                var (x, y, k) = Field.Project(s.X, s.Z);
                double r = Math.Max(2, 7 * k);
                dc.DrawEllipse(Brush(0x60FF4A3A), null, new Point(x, y - 14 * k), r * 1.8, r * 1.8);
                dc.DrawEllipse(Brush(0xFFFFB0A0), Pen(Red, 1.5), new Point(x, y - 14 * k), r, r);
            }
            foreach (var d in g.Wreckage)
            {
                double a = Math.Clamp(1 - d.Age / d.Life, 0, 1);
                dc.FillRectangle(Brush(Alpha(d.Colour, a)), new Rect(d.X - 2, d.Y - 2, 4, 4));
            }
            var ship = g.Ship;
            if (ship.Alive && (ship.Safe <= 0 || (int)(_clock * 10) % 2 == 0))
            {
                var (sx, sy, _) = Field.Project(ship.X, 0);
                DrawShip(dc, sx, sy, ship.Bank, ship.Vz, 1);
            }
            DrawScanner(dc);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 14), 24, Yellow, center: true);
    }

    /// <summary>The sky: stars, a ringed planet low on the horizon, and the horizon's glow.</summary>
    private void DrawSky(DrawingContext dc)
    {
        for (int i = 0; i < 90; i++)
        {
            uint h = Hash(i, 5, 2);
            double x = h % 1024, y = 70 + (h >> 10) % (uint)(Field.Horizon - 75);
            dc.DrawEllipse(Brush(Alpha(0xFFE0E8FF, 0.4 + 0.4 * Math.Abs(Math.Sin(_clock + i)))), null, new Point(x, y), 1, 1);
        }
        var p = new Point(800, Field.Horizon - 18);
        dc.DrawEllipse(Brush(0xFF3A2A5A), null, p, 34, 34);
        dc.DrawEllipse(Brush(0xFF5A4A8A), null, new Point(p.X - 8, p.Y - 8), 22, 22);
        dc.DrawEllipse(null, Pen(0xA0C0A0FF, 2), p, 56, 10);
        dc.FillRectangle(Brush(0xFF02030A), new Rect(0, Field.Horizon, Field.Width, 4));
        for (int i = 0; i < 6; i++) dc.FillRectangle(Brush(Alpha(0xFF6A4AFF, 0.12 - i * 0.02)), new Rect(0, Field.Horizon - 2 - i * 3, Field.Width, 3));
    }

    /// <summary>
    /// The ground in perspective: bands across it, alternating, running towards the ship as it goes forward; the
    /// track's lines converging on the horizon; and its edges lit. In a fury the bands go red.
    /// </summary>
    private static void DrawGround(DrawingContext dc, double travel, (uint A, uint B, uint Edge) pal, bool fury)
    {
        const double band = 2;
        double first = -(((travel % band) + band) % band) - band;
        int index = (int)Math.Floor(travel / band);
        for (int i = 0; i < 60; i++)
        {
            double za = first + i * band, zb = za + band;
            if (zb < -1.9) continue;
            za = Math.Max(za, -1.9);
            if (za > 400) break;
            var (_, ya, _) = Field.Project(0, za);
            var (_, yb, _) = Field.Project(0, Math.Min(zb, 400));
            bool odd = ((index + i) & 1) == 0;
            uint col = odd ? pal.A : pal.B;
            if (fury) col = odd ? 0xFF5A1010 : 0xFF380808;
            double wa = Field.Spread / (za + Field.Camera), wb = Field.Spread / (Math.Min(zb, 400) + Field.Camera);
            Poly(dc, Mix(col, 0xFF000000, 0.45), null, 0, new Point(Field.CX - wa * 2.4, ya), new Point(Field.CX + wa * 2.4, ya), new Point(Field.CX + wb * 2.4, yb), new Point(Field.CX - wb * 2.4, yb));
            Poly(dc, col, null, 0, new Point(Field.CX - wa, ya), new Point(Field.CX + wa, ya), new Point(Field.CX + wb, yb), new Point(Field.CX - wb, yb));
        }
        uint edge = fury ? Red : pal.Edge;
        foreach (double x in new[] { -1.0, -0.5, 0, 0.5, 1.0 })
        {
            var (x0, y0, _) = Field.Project(x, -1.9);
            var (x1, y1, _) = Field.Project(x, 4000);
            bool side = Math.Abs(x) == 1;
            dc.DrawLine(Pen(Alpha(edge, side ? 0.3 : 0.12), side ? 8 : 3), new Point(x0, y0), new Point(x1, y1));
            dc.DrawLine(Pen(Alpha(edge, side ? 1 : 0.35), side ? 2.5 : 1.2), new Point(x0, y0), new Point(x1, y1));
        }
    }

    private static void DrawBolt(DrawingContext dc, Bolt b)
    {
        var (x0, y0, k0) = Field.Project(b.X, b.Z);
        var (x1, y1, _) = Field.Project(b.X, b.Z + 1.6);
        dc.DrawLine(Pen(Alpha(Yellow, 0.35), Math.Max(2, 10 * k0)), new Point(x0, y0 - 18 * k0), new Point(x1, y1 - 18 * k0));
        dc.DrawLine(Pen(0xFFFFFFE0, Math.Max(1, 3.5 * k0)), new Point(x0, y0 - 18 * k0), new Point(x1, y1 - 18 * k0));
    }

    /// <summary>The ship, seen from behind: swept wings, twin tail fins, and its engines burning brighter going forward.</summary>
    private void DrawShip(DrawingContext dc, double x, double y, double bank, double vz, double k)
    {
        using var _ = dc.PushTransform(Matrix.CreateTranslation(-x, -y) * Matrix.CreateRotation(bank * 0.18) * Matrix.CreateTranslation(x, y));
        double flame = 8 + Math.Max(0, vz) * 2.2 + Math.Sin(_clock * 40) * 3;
        foreach (int side in new[] { -1, 1 })
        {
            var e = new Point(x + side * 14 * k, y + 4 * k);
            dc.DrawEllipse(Brush(0x806AB0FF), null, new Point(e.X, e.Y + flame * 0.5 * k), 7 * k, (6 + flame * 0.5) * k);
            dc.DrawEllipse(Brush(0xFFFFFFFF), null, e, 4 * k, 4 * k);
        }
        Poly(dc, 0xFFC8D0E0, 0xFF5A6070, 1.4, new Point(x - 60 * k, y + 4 * k), new Point(x - 14 * k, y - 14 * k), new Point(x + 14 * k, y - 14 * k), new Point(x + 60 * k, y + 4 * k), new Point(x + 16 * k, y + 6 * k), new Point(x - 16 * k, y + 6 * k));
        Poly(dc, 0xFFE8ECF8, 0xFF5A6070, 1.4, new Point(x - 14 * k, y + 6 * k), new Point(x - 10 * k, y - 26 * k), new Point(x + 10 * k, y - 26 * k), new Point(x + 14 * k, y + 6 * k));
        foreach (int side in new[] { -1, 1 })
            Poly(dc, Red, 0xFF6A1010, 1, new Point(x + side * 8 * k, y - 20 * k), new Point(x + side * 20 * k, y - 40 * k), new Point(x + side * 16 * k, y - 18 * k));
        dc.DrawEllipse(Brush(Cyan), null, new Point(x, y - 18 * k), 5 * k, 4 * k);
        Poly(dc, Red, null, 0, new Point(x - 60 * k, y + 4 * k), new Point(x - 50 * k, y - 1 * k), new Point(x - 48 * k, y + 4 * k));
        Poly(dc, Red, null, 0, new Point(x + 60 * k, y + 4 * k), new Point(x + 50 * k, y - 1 * k), new Point(x + 48 * k, y + 4 * k));
    }

    /// <summary>
    /// An enemy, at its place in the distance and its size there: the vane's three spinning blades, the drone's
    /// eye between its wings, the hopper's dome on bent legs, the pod's pulsing shell, a spark, or the astronaut.
    /// Furious, they go red.
    /// </summary>
    private void DrawEnemy(DrawingContext dc, EnemyKind kind, double x, double y, double k, double t, bool fury, int hp = 1)
    {
        uint col = fury && kind != EnemyKind.Astronaut ? Red : GameEngine.Colour(kind);
        double s = 46 * k;
        switch (kind)
        {
            case EnemyKind.Vane:
                for (int i = 0; i < 3; i++)
                {
                    double a = t * 6 + i * Math.PI * 2 / 3;
                    Poly(dc, col, Mix(col, 0xFF000000, 0.5), 1, new Point(x, y), new Point(x + Math.Cos(a) * s, y + Math.Sin(a) * s * 0.6), new Point(x + Math.Cos(a + 0.6) * s * 0.6, y + Math.Sin(a + 0.6) * s * 0.36));
                }
                dc.DrawEllipse(Brush(0xFFFFF0C0), null, new Point(x, y), s * 0.2, s * 0.2);
                break;
            case EnemyKind.Drone:
            {
                double flap = Math.Sin(t * 10) * 0.3;
                foreach (int side in new[] { -1, 1 })
                    Poly(dc, Mix(col, 0xFF000000, 0.3), col, 1, new Point(x + side * s * 0.3, y), new Point(x + side * s, y - s * (0.4 + flap)), new Point(x + side * s * 0.9, y + s * 0.2));
                Poly(dc, col, Mix(col, 0xFFFFFFFF, 0.5), 1.2, new Point(x, y - s * 0.5), new Point(x + s * 0.35, y), new Point(x, y + s * 0.5), new Point(x - s * 0.35, y));
                dc.DrawEllipse(Brush(0xFFFFFFFF), null, new Point(x, y), s * 0.14, s * 0.14);
                dc.DrawEllipse(Brush(Red), null, new Point(x, y), s * 0.07, s * 0.07);
                break;
            }
            case EnemyKind.Hopper:
            {
                double crouch = (Math.Sin(t * 5) + 1) * 0.1;
                foreach (int side in new[] { -1, 1 })
                {
                    dc.DrawLine(Pen(col, Math.Max(1, 3 * k)), new Point(x + side * s * 0.4, y), new Point(x + side * s * 0.75, y - s * (0.2 - crouch)));
                    dc.DrawLine(Pen(col, Math.Max(1, 3 * k)), new Point(x + side * s * 0.75, y - s * (0.2 - crouch)), new Point(x + side * s * 0.8, y + s * 0.45));
                }
                dc.DrawEllipse(Brush(Mix(col, 0xFF000000, 0.35)), Pen(col, Math.Max(1, 1.5 * k)), new Point(x, y - s * 0.1 + crouch * s), s * 0.55, s * 0.4);
                dc.DrawEllipse(Brush(0xFFFFF0A0), null, new Point(x - s * 0.18, y - s * 0.22 + crouch * s), s * 0.1, s * 0.1);
                dc.DrawEllipse(Brush(0xFFFFF0A0), null, new Point(x + s * 0.18, y - s * 0.22 + crouch * s), s * 0.1, s * 0.1);
                break;
            }
            case EnemyKind.Pod:
            {
                double p = 1 + Math.Sin(t * 4) * 0.08;
                dc.DrawEllipse(null, Pen(Alpha(col, 0.5), Math.Max(1, 2 * k)), new Point(x, y), s * 0.95 * p, s * 0.6 * p);
                dc.DrawEllipse(Brush(Mix(col, 0xFF000000, hp > 1 ? 0.3 : 0.6)), Pen(col, Math.Max(1, 1.6 * k)), new Point(x, y), s * 0.55, s * 0.72);
                for (int i = 0; i < 3; i++) dc.DrawEllipse(Brush(Yellow), null, new Point(x + (i - 1) * s * 0.22, y + Math.Sin(t * 3 + i) * s * 0.2), s * 0.08, s * 0.08);
                break;
            }
            case EnemyKind.Spark:
                for (int i = 0; i < 4; i++)
                {
                    double a = t * 12 + i * Math.PI / 2;
                    dc.DrawLine(Pen(col, Math.Max(1, 2 * k)), new Point(x, y), new Point(x + Math.Cos(a) * s * 0.5, y + Math.Sin(a) * s * 0.5));
                }
                dc.DrawEllipse(Brush(0xFFFFFFFF), null, new Point(x, y), s * 0.15, s * 0.15);
                break;
            default:
            {
                // The astronaut: a white suit, a gold visor, and an arm waving for rescue.
                double wave = Math.Sin(t * 6) * 0.4;
                dc.DrawRectangle(Brush(0xFFE8ECF8), Pen(0xFF8A90A0, 1), new Rect(x - s * 0.3, y - s * 0.2, s * 0.6, s * 0.6), s * 0.12, s * 0.12);
                dc.DrawEllipse(Brush(0xFFF2F2FF), Pen(0xFF8A90A0, 1), new Point(x, y - s * 0.42), s * 0.3, s * 0.28);
                dc.DrawEllipse(Brush(0xFFFFC84A), null, new Point(x, y - s * 0.42), s * 0.18, s * 0.14);
                dc.DrawLine(Pen(0xFFE8ECF8, Math.Max(1, 4 * k)), new Point(x + s * 0.3, y - s * 0.1), new Point(x + s * 0.6, y - s * (0.5 + wave)));
                dc.DrawLine(Pen(0xFFE8ECF8, Math.Max(1, 4 * k)), new Point(x - s * 0.3, y - s * 0.1), new Point(x - s * 0.5, y + s * 0.2));
                break;
            }
        }
    }

    /// <summary>The scanner, top right: the track from above, the ship at the bottom and everything up to 45 ahead.</summary>
    private void DrawScanner(DrawingContext dc)
    {
        var g = Engine;
        var r = new Rect(Field.Width - 128, 78, 112, 150);
        dc.FillRectangle(Brush(0xA0000010), r);
        dc.DrawRectangle(null, Pen(Alpha(Cyan, 0.6), 1.2), r);
        Point At(double x, double z) => new(r.X + r.Width / 2 + x * r.Width * 0.45, r.Bottom - 10 - Math.Clamp(z, -2, 45) / 45 * (r.Height - 16));
        foreach (var e in g.Enemies)
            dc.FillRectangle(Brush(e.Kind == EnemyKind.Astronaut ? 0xFFFFFFFF : g.Fury ? Red : GameEngine.Colour(e.Kind)), new Rect(At(e.X, e.Z).X - 2, At(e.X, e.Z).Y - 2, 4, 4));
        var p = At(g.Ship.X, 0);
        Poly(dc, Cyan, null, 0, new Point(p.X, p.Y - 5), new Point(p.X + 5, p.Y + 3), new Point(p.X - 5, p.Y + 3));
        Text(dc, "SCANNER", new Point(r.X + r.Width / 2, r.Bottom + 12), 16, Dim, center: true);
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (6 + 30 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 2.5), c, r, r);
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 24), 22, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 38, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 46), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 16), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 46), 30, Dim, right: true);
        if (!InGame) return;
        Text(dc, $"WAVE {g.Wave}", new Point(512, 16), 26, Palettes[(g.Wave - 1) % Palettes.Length].Edge, center: true);
        Text(dc, $"TIME {Math.Ceiling(g.TimeLeft):00}", new Point(512, 46), 28, g.TimeLeft < 10 && (int)(_clock * 4) % 2 == 0 ? Red : White, center: true);
        Text(dc, $"{g.Left} LEFT", new Point(512, 74), 20, Dim, center: true);
        if (g.BonusTime > 0) Text(dc, $"BONUS x{g.Multiplier}", new Point(640, 46), 26, Cyan, center: true);
        Text(dc, g.Ship.WarpReady ? "WARP READY" : "WARP USED", new Point(760, 46), 20, g.Ship.WarpReady ? 0xFF8AE0FF : 0xFF4A5A7A, center: true);
        int spare = Math.Max(0, g.Lives - (g.Ship.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 5); i++) DrawShip(dc, 220 + i * 34, 44, 0, 0, 0.3);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 300;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xC0080A20), new Rect(cx - w / 2, cy - 46, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 42 : 54, g.Phase == Phase.Dying || msg == "TIME UP" ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0x9002030A), new Rect(0, 70, Field.Width, Field.Height - 70));
        Text(dc, "AVA JUNO FIRST", new Point(cx, 110), 104, 0xFF8AE0FF, center: true);
        Text(dc, "AFTER THE 1983 ARCADE CLASSIC", new Point(cx, 172), 28, Dim, center: true);
        Text(dc, "RACE UP THE GRID AND CLEAR EACH WAVE BEFORE THE CLOCK RUNS OUT", new Point(cx, 220), 24, White, center: true);
        (EnemyKind Kind, string Words)[] legend =
        [
            (EnemyKind.Vane, "100"), (EnemyKind.Drone, "150"), (EnemyKind.Hopper, "200"), (EnemyKind.Pod, "300"), (EnemyKind.Spark, "50"), (EnemyKind.Astronaut, "BONUS"),
        ];
        for (int i = 0; i < legend.Length; i++)
        {
            double x = cx - 375 + i * 150;
            DrawEnemy(dc, legend[i].Kind, x, 300, 1.1, _clock + i, false, 2);
            Text(dc, legend[i].Kind.ToString().ToUpperInvariant(), new Point(x, 356), 22, Dim, center: true);
            Text(dc, legend[i].Words, new Point(x, 382), 22, legend[i].Kind == EnemyKind.Astronaut ? Cyan : Yellow, center: true);
        }
        Text(dc, "SHOOT THE ASTRONAUT: EVERY KILL FOR A WHILE AFTER SCORES MORE THAN THE LAST", new Point(cx, 430), 22, Cyan, center: true);
        Text(dc, "WHEN THE TIME RUNS OUT, WHAT'S LEFT TURNS FURIOUS", new Point(cx, 458), 22, Red, center: true);
        DrawShip(dc, cx, 540, Math.Sin(_clock), 4, 1.3);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 616), 46, Yellow, center: true);
        Text(dc, "ARROWS: STEER, UP FORWARD, DOWN BACK    SPACE: FIRE    X OR SHIFT: WARP (ONCE A LIFE)", new Point(cx, 670), 22, Dim, center: true);
        Text(dc, $"P: PAUSE    M: SOUND    EXTRA LIFE AT {GameEngine.FirstExtraLife} AND EVERY {GameEngine.ExtraLifeEvery}", new Point(cx, 700), 22, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "TOP PILOTS", new Point(cx, 140), 68, 0xFF8AE0FF, center: true);
        Text(dc, "RANK NAME    SCORE    WAVE", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }

    /// <summary>A colour part of the way to another.</summary>
    private static uint Mix(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Ch(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return Ch(24) << 24 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }
}
