namespace ChickenCourier.Logic;

public enum CourierEvent
{
    Panicked,
    Calmed,
    LayStarted,
    LayInterrupted,
    Laid,
}

/// <summary>Bomb: the courier carries the C4 to a bombsite. Delivery: deathmatch, whoever walks it to the drop zone scores.</summary>
public enum CourierMode
{
    Bomb,
    Delivery,
}

/// <summary>A simple point type so the rules stay engine-free (and unit-tested).</summary>
public readonly record struct Point(float X, float Y, float Z)
{
    public float DistanceTo(Point o)
    {
        var dx = X - o.X;
        var dy = Y - o.Y;
        var dz = Z - o.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

/// <summary>A bombsite: an axis-aligned box when the map's trigger bounds are known, else a sphere round its centre.</summary>
public sealed record Site(string Name, Point Centre, Point? Min = null, Point? Max = null)
{
    public bool Contains(Point p, float radius, float tolerance = 32f)
    {
        if (Min is { } a && Max is { } b && b.X - a.X > 1 && b.Y - a.Y > 1)
            return p.X >= a.X - tolerance && p.X <= b.X + tolerance
                && p.Y >= a.Y - tolerance && p.Y <= b.Y + tolerance
                && p.Z >= a.Z - 96f && p.Z <= b.Z + 96f;
        return p.DistanceTo(Centre) <= radius;
    }
}

/// <summary>
/// The courier's round: courage, panic, laying progress. The plugin calls <see cref="Tick"/> a few times a second and
/// <see cref="Hit"/> / <see cref="Scare"/> on damage and E presses, then acts on the events.
/// </summary>
public sealed class CourierState
{
    private readonly CourierConfig _cfg;
    private readonly double _laySeconds;

    /// <param name="laySeconds">How long laying (or delivering) takes; LaySeconds from the config when null.</param>
    public CourierState(CourierConfig cfg, double? laySeconds = null)
    {
        _cfg = cfg;
        _laySeconds = laySeconds ?? cfg.LaySeconds;
        Courage = cfg.Courage;
    }

    public double Courage { get; private set; }
    public double PanicUntil { get; private set; }
    public double LastHitAt { get; private set; } = double.MinValue;
    public double LayProgress { get; private set; }
    public bool Laying { get; private set; }
    public bool Laid { get; private set; }
    public string? LayingSite { get; private set; }

    public bool Panicking(double now) => now < PanicUntil;
    public double CourageFraction => Math.Clamp(Courage / Math.Max(1, _cfg.Courage), 0, 1);
    public double LayFraction => Math.Clamp(LayProgress / Math.Max(0.1, _laySeconds), 0, 1);

    /// <summary>The game was paused for <paramref name="seconds"/>: push the clocks on so a pause doesn't eat a panic.</summary>
    public void Shift(double seconds)
    {
        if (seconds <= 0) return;
        if (PanicUntil > 0) PanicUntil += seconds;
        if (LastHitAt > double.MinValue) LastHitAt += seconds;
    }

    /// <summary>CT damage. Returns true when it breaks the chicken's nerve (starts a panic).</summary>
    public bool Hit(double damage, double now)
    {
        if (Laid || damage <= 0) return false;
        LastHitAt = now;
        if (Panicking(now)) return false;
        Courage = Math.Max(0, Courage - damage * _cfg.DamageToCourage);
        if (Courage > 0) return false;
        Panic(now, _cfg.PanicSeconds);
        Courage = _cfg.CourageAfterPanic;
        return true;
    }

    /// <summary>A CT pressed E on it: a short panic and some courage lost. Returns true if it worked.</summary>
    public bool Scare(double now)
    {
        if (Laid || Panicking(now)) return false;
        LastHitAt = now;
        Courage = Math.Max(1, Courage - _cfg.ScareCourage);
        Panic(now, _cfg.ScarePanicSeconds);
        return true;
    }

    private void Panic(double now, double seconds)
    {
        PanicUntil = now + seconds;
        if (Laying)
        {
            Laying = false;
            LayProgress = 0;
        }
    }

    /// <param name="site">The bombsite the chicken stands in, or null.</param>
    /// <param name="handlerClose">The handler is alive and within LayHandlerRange.</param>
    public List<CourierEvent> Tick(double dt, double now, string? site, bool handlerClose)
    {
        var events = new List<CourierEvent>();
        if (Laid) return events;

        if (PanicUntil > 0 && now >= PanicUntil && now - dt < PanicUntil) events.Add(CourierEvent.Calmed);
        if (now - LastHitAt > 3 && !Panicking(now)) Courage = Math.Min(_cfg.Courage, Courage + _cfg.CourageRegen * dt);

        var canLay = site != null && handlerClose && !Panicking(now);
        if (canLay)
        {
            if (!Laying)
            {
                Laying = true;
                LayingSite = site;
                LayProgress = 0;
                events.Add(CourierEvent.LayStarted);
            }

            LayProgress += dt;
            if (LayProgress >= _laySeconds)
            {
                Laid = true;
                Laying = false;
                events.Add(CourierEvent.Laid);
            }
        }
        else if (Laying)
        {
            Laying = false;
            LayProgress = 0;
            events.Add(CourierEvent.LayInterrupted);
        }

        return events;
    }

    /// <summary>Text bar for the floating marker, e.g. ▮▮▮▮▮▯▯▯.</summary>
    public static string Bar(double fraction, int cells = 8)
    {
        var filled = (int)Math.Round(Math.Clamp(fraction, 0, 1) * cells);
        return new string('▮', filled) + new string('▯', cells - filled);
    }

    public static bool MapMatches(string map, IEnumerable<string> patterns) =>
        patterns.Select(p => p.Trim()).Where(p => p.Length > 0).Any(p =>
            p.EndsWith('*') ? map.StartsWith(p[..^1], StringComparison.OrdinalIgnoreCase) : map.Equals(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The config's Mode: "bomb" or "delivery" as set; anything else ("auto") is Delivery on deathmatch
    /// (game_type 1, game_mode 2) and Bomb everywhere else.
    /// </summary>
    public static CourierMode ResolveMode(string? mode, int gameType, int gameMode) =>
        (mode ?? "").Trim().ToLowerInvariant() switch
        {
            "bomb" => CourierMode.Bomb,
            "delivery" or "dm" or "deathmatch" => CourierMode.Delivery,
            _ => gameType == 1 && gameMode == 2 ? CourierMode.Delivery : CourierMode.Bomb,
        };

    /// <summary>
    /// Delivery mode: a random spawn point at least <paramref name="minDistance"/> from the courier, or the farthest one
    /// when none is that far. Null with fewer than two points.
    /// </summary>
    public static Point? PickDropZone(IReadOnlyList<Point> points, Point from, float minDistance, Random rng)
    {
        var others = points.Where(p => p.DistanceTo(from) > 1f).ToList();
        if (others.Count == 0) return null;
        var far = others.Where(p => p.DistanceTo(from) >= minDistance).ToList();
        if (far.Count > 0) return far[rng.Next(far.Count)];
        return others.OrderByDescending(p => p.DistanceTo(from)).First();
    }

    /// <summary>Picks the closest candidate within range, or null.</summary>
    public static int? Nearest(Point from, IEnumerable<(int Slot, Point At)> candidates, float range)
    {
        int? best = null;
        var bestD = float.MaxValue;
        foreach (var (slot, at) in candidates)
        {
            var d = at.DistanceTo(from);
            if (d <= range && d < bestD)
            {
                best = slot;
                bestD = d;
            }
        }

        return best;
    }
}
