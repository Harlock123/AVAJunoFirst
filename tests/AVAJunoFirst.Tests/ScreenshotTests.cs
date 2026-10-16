using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAJunoFirst.Engine;
using AVAJunoFirst.Views;
using Controls = AVAJunoFirst.Engine.Controls;

namespace AVAJunoFirst.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set JUNO_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avajunofirst-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("JUNO_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAJunoFirst-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { Fire = false } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 88450, 8);
        g.HighScores.Insert("AVA", 61200, 6);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    /// <summary>A wave, and the computer at work in it until something worth showing.</summary>
    private static void Wave(int wave, string name, Func<GameEngine, bool> busy, int seed = 3, int seconds = 60)
    {
        var (w, c, g) = Open(seed);
        g.Go(wave);
        g.SetLives(20);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Play && g.Ship.Alive && g.Ship.Safe <= 0 && busy(g), seconds));
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_first_wave() =>
        Wave(1, "vanes", g => g.Enemies.Count(e => Field.Visible(e.Z) && e.Z < 18) >= 5 && g.Bolts.Count >= 2 && g.Wreckage.Count > 3);

    [AvaloniaFact]
    public void Drones() => Wave(2, "drones", g => g.Enemies.Count(e => e.Kind == EnemyKind.Drone && e.Z < 16) >= 3 && g.Shots.Count >= 1);

    [AvaloniaFact]
    public void Pods_and_sparks() => Wave(4, "pods", g => g.Enemies.Any(e => e.Kind == EnemyKind.Pod && e.Z < 18) && g.Enemies.Any(e => e.Kind == EnemyKind.Spark && e.Z < 12));

    [AvaloniaFact]
    public void The_astronaut() => Wave(2, "astronaut", g => g.Enemies.Any(e => e.Kind == EnemyKind.Astronaut && e.Z < 16 && e.Z > 4), seconds: 90);

    [AvaloniaFact]
    public void The_bonus() => Wave(3, "bonus", g => g.BonusTime > 0 && g.Multiplier >= 3, seconds: 120);

    [AvaloniaFact]
    public void Fury()
    {
        var (w, c, g) = Open(5);
        g.Go(3);
        g.SetLives(20);
        g.SetTime(1);
        Assert.True(PlayUntil(g, c, g => g.Fury && g.Message == null && g.Enemies.Count(e => Field.Visible(e.Z) && e.Z < 14) >= 4 && g.Ship.Alive, 60));
        Shot(w, c, "fury");
    }
}
