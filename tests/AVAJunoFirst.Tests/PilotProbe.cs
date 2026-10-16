using AVAJunoFirst.Engine;

namespace AVAJunoFirst.Tests;

/// <summary>How the computer player gets on, wave by wave (set JUNO_PROBE to run it, for tuning; JUNO_PROBE=lazy for one that just weaves and fires).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("JUNO_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "juno-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var waves = new List<string>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int wave = 1, t = 0, furies = 0;
            double start = 0;
            for (; t < 60 * 900 && g.Wave <= 6 && g.State == GameState.Playing; t++)
            {
                var c = mode == "lazy" ? new Controls(Dx: t % 200 < 100 ? -1 : 1, Fire: true) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Play) doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"W{g.Wave} {g.Cause}"] = deaths.GetValueOrDefault($"W{g.Wave} {g.Cause}") + 1;
                if (g.Phase == Phase.Clear && last != Phase.Clear) { waves.Add($"W{wave}:{(t - start) / 60:0}s{(g.Fury ? "!" : "")}"); if (g.Fury) furies++; }
                if (g.Wave != wave) { wave = g.Wave; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: wave {g.Wave} after {t / 60:0}s score {g.Score} | {string.Join(" ", waves)} (! = ran out of time)");
            output.WriteLine("   deaths " + string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine($"   left {g.Left} lives {g.Lives}");
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}
