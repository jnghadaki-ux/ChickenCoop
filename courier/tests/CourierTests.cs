using ChickenCourier;
using ChickenCourier.Logic;
using Xunit;

namespace ChickenCourier.Tests;

public class CourierTests
{
    private static CourierConfig Cfg() => new()
    {
        Courage = 100,
        DamageToCourage = 1,
        PanicSeconds = 6,
        CourageAfterPanic = 70,
        CourageRegen = 5,
        LaySeconds = 4,
        ScareCourage = 30,
        ScarePanicSeconds = 2,
    };

    private static List<CourierEvent> Run(CourierState s, double from, double seconds, string? site, bool close, double dt = 0.2)
    {
        var all = new List<CourierEvent>();
        for (var t = from; t < from + seconds - 1e-9; t += dt) all.AddRange(s.Tick(dt, t + dt, site, close));
        return all;
    }

    [Fact]
    public void Ct_damage_drains_courage_until_it_panics_then_it_comes_back()
    {
        var s = new CourierState(Cfg());
        Assert.False(s.Hit(60, 1));
        Assert.Equal(40, s.Courage);
        Assert.True(s.Hit(50, 2));
        Assert.True(s.Panicking(5));
        Assert.Equal(70, s.Courage);
        Assert.False(s.Hit(500, 3)); // no double panic while already panicking
        var events = Run(s, 3, 6, null, false);
        Assert.Contains(CourierEvent.Calmed, events);
        Assert.False(s.Panicking(9));
    }

    [Fact]
    public void Courage_regenerates_after_a_quiet_spell()
    {
        var s = new CourierState(Cfg());
        s.Hit(50, 0);
        Run(s, 0, 2, null, false);
        Assert.Equal(50, s.Courage, 3); // still within 3 s of the hit
        Run(s, 2, 4, null, false);
        Assert.True(s.Courage > 50 && s.Courage <= 100);
        Run(s, 6, 60, null, false);
        Assert.Equal(100, s.Courage, 3);
    }

    [Fact]
    public void Laying_needs_the_site_and_the_handler_for_the_full_time()
    {
        var s = new CourierState(Cfg());
        Assert.Empty(Run(s, 0, 2, null, true));                  // not in a site
        Assert.DoesNotContain(CourierEvent.LayStarted, Run(s, 2, 2, "A", false)); // handler too far
        var started = Run(s, 4, 2, "A", true);
        Assert.Contains(CourierEvent.LayStarted, started);
        Assert.True(s.Laying);
        Assert.Contains(CourierEvent.LayInterrupted, Run(s, 6, 0.2, "A", false));
        Assert.Equal(0, s.LayProgress);
        var done = Run(s, 7, 4.2, "B", true);
        Assert.Contains(CourierEvent.Laid, done);
        Assert.True(s.Laid);
        Assert.Empty(Run(s, 12, 2, "B", true)); // nothing after it's laid
        Assert.False(s.Hit(999, 13));
    }

    [Fact]
    public void A_panic_or_scare_interrupts_laying()
    {
        var s = new CourierState(Cfg());
        Run(s, 0, 2, "A", true);
        Assert.True(s.Laying);
        Assert.True(s.Scare(2));
        Assert.False(s.Laying);
        Assert.Equal(70, s.Courage);
        Assert.True(s.Panicking(3));
        Assert.False(s.Scare(3)); // already panicking
        Assert.DoesNotContain(CourierEvent.LayStarted, Run(s, 2, 1.5, "A", true));
        Assert.Contains(CourierEvent.LayStarted, Run(s, 4.2, 0.4, "A", true));
    }

    [Fact]
    public void Sites_use_bounds_when_known_and_a_radius_otherwise()
    {
        var box = new Site("A", new Point(50, 50, 0), new Point(0, 0, 0), new Point(100, 100, 64));
        Assert.True(box.Contains(new Point(50, 50, 10), 10));
        Assert.True(box.Contains(new Point(120, 50, 10), 10));   // within the 32-unit tolerance
        Assert.False(box.Contains(new Point(200, 50, 10), 10));
        var round = new Site("B", new Point(0, 0, 0));
        Assert.True(round.Contains(new Point(100, 0, 0), 150));
        Assert.False(round.Contains(new Point(200, 0, 0), 150));
    }

    [Fact]
    public void Nearest_handler_within_range()
    {
        var from = new Point(0, 0, 0);
        var cands = new[] { (1, new Point(300, 0, 0)), (2, new Point(100, 0, 0)), (3, new Point(900, 0, 0)) };
        Assert.Equal(2, CourierState.Nearest(from, cands, 700));
        Assert.Null(CourierState.Nearest(from, cands, 50));
    }

    [Theory]
    [InlineData("de_dust2", true)]
    [InlineData("de_mirage", true)]
    [InlineData("cs_office", false)]
    public void Default_maps_are_bomb_maps(string map, bool on)
    {
        Assert.Equal(on, CourierState.MapMatches(map, new CourierConfig().Maps));
    }

    [Theory]
    [InlineData("auto", 1, 2, CourierMode.Delivery)]  // deathmatch
    [InlineData("auto", 0, 1, CourierMode.Bomb)]      // competitive
    [InlineData("auto", 0, 0, CourierMode.Bomb)]      // casual
    [InlineData("AUTO", 1, 0, CourierMode.Bomb)]      // arms race is not deathmatch
    [InlineData("bomb", 1, 2, CourierMode.Bomb)]
    [InlineData("delivery", 0, 1, CourierMode.Delivery)]
    [InlineData("nonsense", 1, 2, CourierMode.Delivery)]
    public void Mode_comes_from_the_config_or_the_game_mode(string mode, int gameType, int gameMode, CourierMode want)
    {
        Assert.Equal(want, CourierState.ResolveMode(mode, gameType, gameMode));
    }

    [Fact]
    public void Drop_zone_is_far_from_the_courier_or_the_farthest_point()
    {
        var from = new Point(0, 0, 0);
        var pts = new List<Point> { from, new(500, 0, 0), new(2000, 0, 0), new(0, 1800, 0) };
        for (var i = 0; i < 20; i++)
        {
            var z = CourierState.PickDropZone(pts, from, 1400, new Random(i))!.Value;
            Assert.True(z.DistanceTo(from) >= 1400);
        }

        Assert.Equal(new Point(2000, 0, 0), CourierState.PickDropZone(pts, from, 5000, new Random(1)));
        Assert.Null(CourierState.PickDropZone(new List<Point> { from }, from, 100, new Random(1)));
        Assert.Null(CourierState.PickDropZone(new List<Point>(), from, 100, new Random(1)));
    }

    [Fact]
    public void Delivery_uses_its_own_time()
    {
        var s = new CourierState(Cfg(), laySeconds: 2);
        var events = Run(s, 0, 2.2, "drop", true);
        Assert.Contains(CourierEvent.Laid, events);
        var bomb = new CourierState(Cfg()); // LaySeconds 4
        Assert.DoesNotContain(CourierEvent.Laid, Run(bomb, 0, 2.2, "A", true));
    }

    [Fact]
    public void A_pause_does_not_eat_a_panic()
    {
        var s = new CourierState(Cfg());
        s.Hit(500, 10);
        Assert.Equal(16, s.PanicUntil);
        s.Shift(30); // a 30 s timeout at t=11
        Assert.True(s.Panicking(40));
        Assert.False(s.Panicking(46));
        s.Shift(-5); // ignored
        Assert.Equal(46, s.PanicUntil);
        var fresh = new CourierState(Cfg());
        fresh.Shift(30);
        Assert.False(fresh.Panicking(1)); // no panic invented
    }

    [Fact]
    public void Every_language_has_every_message()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "lang");
        var en = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(dir, "en.json")))!;
        var files = Directory.GetFiles(dir, "*.json");
        Assert.Equal(6, files.Length);
        foreach (var f in files)
        {
            var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(f))!;
            Assert.Empty(en.Keys.Except(map.Keys));
            foreach (var (k, v) in en)
            {
                var want = System.Text.RegularExpressions.Regex.Matches(v, @"\{\d\}").Select(m => m.Value).ToHashSet();
                var got = System.Text.RegularExpressions.Regex.Matches(map[k], @"\{\d\}").Select(m => m.Value).ToHashSet();
                Assert.True(want.SetEquals(got), $"{Path.GetFileName(f)}: {k}");
            }
        }
    }
}
