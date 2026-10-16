using AVAJunoFirst.Engine;

namespace AVAJunoFirst.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avajunofirst-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A wave in play with its enemies cleared away but one far off, calm: nothing moves or fires on its own.</summary>
    private static GameEngine Game(int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(wave);
        g.Enemies.Clear();
        g.Put(EnemyKind.Vane, 0.9, 60);
        return g;
    }

    /// <summary>A real game, with plenty of lives.</summary>
    private static GameEngine Real(int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(wave);
        g.SetLives(99);
        return g;
    }

    private static readonly Controls FireButton = new(Fire: true);

    // ───────────────────────────── the grid and the ship ─────────────────────────────

    [Fact]
    public void The_grid_runs_to_the_horizon_in_perspective()
    {
        var (x, y, k) = Field.Project(0, 0);
        Assert.Equal((Field.CX, Field.ShipY, 1.0), (x, y, k));
        Assert.Equal(Field.CX + 400, Field.Project(1, 0).X, 6);
        var (fx, fy, fk) = Field.Project(1, 200);
        Assert.True(fy < Field.Horizon + 30 && fy > Field.Horizon);
        Assert.True(fx - Field.CX < 40 && fk < 0.06);
    }

    [Fact]
    public void The_ship_steers_across_the_track_and_no_further()
    {
        var g = Game();
        g.Run(30, new Controls(Dx: 1));
        Assert.Equal(GameEngine.Lateral * 0.5, g.Ship.X, 2);
        g.Run(120, new Controls(Dx: 1));
        Assert.True(g.Ship.X < Field.Edge && g.Ship.X > 0.9);
    }

    [Fact]
    public void Going_forward_runs_everything_down_the_grid_towards_the_ship()
    {
        var g = Game();
        var e = g.Put(EnemyKind.Drone, 0, 20);
        g.Run(60, new Controls(Dz: 1));
        Assert.Equal(GameEngine.MaxForward, g.Ship.Vz, 1);
        Assert.True(e.Z < 20 - 5, $"{e.Z}");
        g.Idle(120);
        Assert.Equal(0, g.Ship.Vz);
        double z = e.Z;
        g.Run(30, new Controls(Dz: -1));
        Assert.True(g.Ship.Vz < 0);
        Assert.True(e.Z > z);
    }

    // ───────────────────────────── shooting ─────────────────────────────

    [Fact]
    public void Shots_run_up_the_grid_only_so_far_and_only_so_many()
    {
        var g = Game();
        g.Run(1, FireButton);
        var b = Assert.Single(g.Bolts);
        int most = 0;
        for (int i = 0; i < 60; i++) { g.Run(1, FireButton); most = Math.Max(most, g.Bolts.Count); }
        Assert.InRange(most, 2, GameEngine.MaxBolts);
        Assert.All(g.Bolts, x => Assert.True(x.Z <= GameEngine.Range));
        g.Idle(60);
        Assert.Empty(g.Bolts);
    }

    [Fact]
    public void A_shot_in_line_destroys_an_enemy_and_a_pod_takes_two()
    {
        var g = Game();
        g.Put(EnemyKind.Vane, 0, 10);
        g.Run(1, FireButton);
        g.Idle(30);
        Assert.Single(g.Enemies);
        Assert.Equal(100, g.Score);
        var pod = g.Put(EnemyKind.Pod, 0, 10);
        g.Run(1, FireButton);
        g.Idle(30);
        Assert.Contains(pod, g.Enemies);
        g.Run(1, FireButton);
        g.Idle(30);
        Assert.DoesNotContain(pod, g.Enemies);
        Assert.Equal(400, g.Score);
        // Out of line, it misses.
        g.Put(EnemyKind.Vane, 0.4, 10);
        g.Run(1, FireButton);
        g.Idle(30);
        Assert.Equal(2, g.Enemies.Count);
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    [Fact]
    public void A_vane_hovers_at_its_place_then_dives_at_the_ship_and_pulls_back()
    {
        var g = Real();
        g.Enemies.Clear();
        g.Ship.Safe = 999;
        var v = g.Put(EnemyKind.Vane, 0.5, 10);
        v.State = 0.5;
        double closest = v.Z;
        for (int i = 0; i < 60 * 3; i++) { g.Tick(default); closest = Math.Min(closest, v.Z); }
        Assert.True(closest < 1, $"{closest}");
        g.Idle(120);
        Assert.True(v.Z > 4, $"{v.Z}");
    }

    [Fact]
    public void Drones_hold_their_place_on_the_grid()
    {
        var g = Real();
        g.Enemies.Clear();
        g.Ship.Safe = 999;
        var d = g.Put(EnemyKind.Drone, 0, 20);
        g.Idle(120);
        Assert.InRange(d.Z, 17, 21);
        g.Run(60, new Controls(Dz: 1));
        Assert.True(d.Z < 14, $"{d.Z}");
    }

    [Fact]
    public void A_pod_lets_loose_sparks_that_go_for_the_ship()
    {
        var g = Real(3);
        g.Enemies.Clear();
        g.Ship.Safe = 999;
        var pod = g.Put(EnemyKind.Pod, 0.4, 14);
        pod.FireIn = 0.1;
        g.Idle(10);
        var sparks = g.Enemies.Where(e => e.Kind == EnemyKind.Spark).ToList();
        Assert.Equal(2, sparks.Count);
        g.Idle(60);
        Assert.All(sparks, s => Assert.True(s.Z < 10 && Math.Abs(s.X - g.Ship.X) < 0.4));
        Assert.All(sparks, s => Assert.False(s.Counts));
    }

    [Fact]
    public void Enemy_shots_come_down_the_grid_and_hit_unless_the_ship_is_safe()
    {
        var g = Game();
        g.Shots.Add(new Shot { X = 0, Z = 6, Vz = -10 });
        g.Ship.Safe = 1;
        g.Idle(50);
        Assert.Equal(Phase.Play, g.Phase);
        g.Ship.Safe = 0;
        g.Shots.Add(new Shot { X = 0, Z = 6, Vz = -10 });
        g.Idle(50);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("shot", g.Cause);
    }

    [Fact]
    public void Running_into_an_enemy_ends_the_life_and_the_last_ends_the_game()
    {
        var g = Game();
        g.SetLives(1);
        g.Put(EnemyKind.Hopper, 0, 0.1);
        g.Idle(1);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("hopper", g.Cause);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void Coming_back_pushes_the_enemy_back_and_the_ship_is_safe_a_moment()
    {
        var g = Game();
        var near = g.Put(EnemyKind.Drone, 0.5, 4);
        g.Put(EnemyKind.Hopper, 0, 0.1);
        g.Idle(1);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.True(near.Z > 15);
        Assert.True(g.Ship.Safe > 0);
        Assert.True(g.Ship.WarpReady);
    }

    [Fact]
    public void Enemies_that_get_behind_come_round_again_from_far_up()
    {
        var g = Real();
        g.Enemies.Clear();
        g.Ship.Safe = 999;
        var h = g.Put(EnemyKind.Hopper, 0.9, -1.4);
        h.Mode = 1; h.State = 1;
        g.Idle(10);
        Assert.True(h.Z > 30);
    }

    // ───────────────────────────── the astronaut, the warp and the clock ─────────────────────────────

    [Fact]
    public void Shooting_the_astronaut_makes_each_kill_worth_more_than_the_last_for_a_while()
    {
        var g = Game();
        g.Put(EnemyKind.Astronaut, 0, 10);
        g.Run(1, FireButton);
        g.Idle(30);
        Assert.Equal(500, g.Score);
        Assert.True(g.BonusTime > 0);
        foreach (int k in new[] { 2, 3 })
        {
            int before = g.Score;
            g.Put(EnemyKind.Vane, 0, 10);
            g.Run(1, FireButton);
            g.Idle(20);
            Assert.Equal(100 * k, g.Score - before);
        }
        g.Idle((int)(GameEngine.BonusSeconds * 60));
        Assert.Equal(1, g.Multiplier);
        int was = g.Score;
        g.Put(EnemyKind.Vane, 0, 10);
        g.Run(1, FireButton);
        g.Idle(20);
        Assert.Equal(100, g.Score - was);
    }

    [Fact]
    public void The_warp_once_a_life_flings_the_enemy_back()
    {
        var g = Game();
        var near = g.Put(EnemyKind.Drone, 0.2, 3);
        g.Ship.X = 0.5;
        g.Run(1, new Controls(Warp: true));
        Assert.False(g.Ship.WarpReady);
        Assert.True(near.Z > 15);
        Assert.True(g.Ship.Safe > 0);
        Assert.True(g.Ship.X < 0);
        double x = g.Ship.X;
        g.Idle(2);
        g.Run(1, new Controls(Warp: true));
        Assert.Equal(x, g.Ship.X);
    }

    [Fact]
    public void When_the_clock_runs_out_the_enemy_turns_furious_and_closes_in()
    {
        var g = Real();
        g.Ship.Safe = 999;
        g.SetTime(0.05);
        g.Idle(5);
        Assert.True(g.Fury);
        Assert.Equal("TIME UP", g.Message);
        double before = g.Enemies.Where(e => e.Kind == EnemyKind.Vane).Average(e => e.Home);
        g.Idle(60);
        Assert.True(g.Enemies.Where(e => e.Kind == EnemyKind.Vane).Average(e => e.Home) < before - 2);
    }

    [Fact]
    public void Clearing_a_wave_pays_for_the_time_left_then_a_bigger_one_comes()
    {
        var g = Game();
        g.Enemies.Clear();
        g.SetTime(30);
        g.Idle(2);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal(29 * 20, g.Bonus);
        g.Idle((int)(GameEngine.ClearSeconds * 60) + 2);
        Assert.Equal(2, g.Wave);
        Assert.Equal(29 * 20, g.Score);
        Assert.Equal(32, g.Left);
        Assert.Contains(g.Enemies, e => e.Kind == EnemyKind.Drone);
        Assert.True(Real(5).Left > Real(2).Left);
        Assert.Contains(Real(3).Enemies, e => e.Kind == EnemyKind.Pod);
    }

    [Fact]
    public void An_extra_life_at_10000()
    {
        var g = Game();
        int lives = g.Lives;
        g.SetScore(GameEngine.FirstExtraLife - 50);
        g.Put(EnemyKind.Vane, 0, 10);
        g.Run(1, FireButton);
        g.Idle(30);
        Assert.Equal(lives + 1, g.Lives);
    }

    // ───────────────────────────── the whole game ─────────────────────────────

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(GameEngine.AttractSeconds * 60) + 2);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_computer_player_clears_the_first_wave()
    {
        var g = Real();
        for (int i = 0; i < 60 * 120 && g.Wave < 2 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.Wave >= 2, $"still on wave {g.Wave}, {g.Left} left");
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = Real(3, seed: 3);
            for (int i = 0; i < 60 * 40; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Lives} {g.Ship.X:0.000} {g.Travel:0.00} {g.Left} {g.Phase}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Real();
        g.Run(30, new Controls(Dx: 1));
        double x = g.Ship.X;
        g.Pause();
        g.Idle(120);
        Assert.Equal(x, g.Ship.X);
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
