using ChickenCoop.Model;

namespace ChickenCoop.Logic;

/// <summary>Arena moves. Peck beats Flap's counter-poke, Guard blunts Peck and counters, Flap dodges Specials,
/// Special smashes through Guard. Each has an answer.</summary>
public enum ArenaMove
{
    Peck = 0,
    Guard = 1,
    Flap = 2,
    Special = 3,
}

/// <summary>A pet in the arena: stats come from its life story (stage, breed, rounds together, meals, tricks).</summary>
public sealed class Fighter
{
    public required string Name { get; init; }
    public int MaxHp { get; init; }
    public int Hp { get; set; }
    public int Attack { get; init; }
    public int Charge { get; set; }
    /// <summary>Emote id whose effect the special move shows (cosmetic only: every special hits as hard).</summary>
    public string SpecialFx { get; init; } = "confetti";
    public bool Fainted => Hp <= 0;
}

public sealed record TurnResult(
    ArenaMove MoveA, ArenaMove MoveB,
    int DamageToA, int DamageToB,
    bool ADodged, bool BDodged);

/// <summary>Engine-free arena rules (unit-tested).</summary>
public static class ArenaLogic
{
    public const int ChargeNeeded = 3;

    public static Fighter FromPet(PetData p)
    {
        var s = p.Stats;
        var hp = 60 + p.Stage switch
        {
            PetStage.Pullet => 15,
            PetStage.Hen => 30,
            _ => 0,
        } + Math.Min(15, s.RoundsTogether / 10);
        var atk = 10 + Math.Min(6, s.Feeds / 4) + Math.Min(4, (s.Tricks + s.Emotes) / 10);
        if (p.ShowsBreed)
        {
            switch (p.Breed)
            {
                case Breed.Silkie: hp += 10; break;
                case Breed.Polish: atk += 2; break;
                default:
                    hp += 5;
                    atk += 1;
                    break;
            }
        }

        if (p.IsShiny) hp = (int)Math.Round(hp * 1.05);
        return new Fighter
        {
            Name = p.Name ?? "Chicken",
            MaxHp = hp,
            Hp = hp,
            Attack = atk,
            SpecialFx = BestSpecial(p),
        };
    }

    /// <summary>The flashiest effect emote the pet has unlocked, for its special move.</summary>
    public static string BestSpecial(PetData p)
    {
        var s = p.Stats;
        if (s.DeathsBomb > 0) return "explosion";
        if (s.DeathsTaser > 0) return "lightning";
        if (s.DeathsBurn > 0) return "fire";
        if (s.Revenges > 0) return "lasers";
        if (s.Tricks + s.Emotes >= 10) return "sparks";
        return "confetti";
    }

    /// <summary>
    /// How hard a move used against another move hits, as a multiple of the user's attack, plus the chance the target
    /// dodges it. Symmetric table: the same function scores both sides.
    /// </summary>
    public static (double Mult, double Dodge) Hit(ArenaMove user, ArenaMove target) => (user, target) switch
    {
        (ArenaMove.Peck, ArenaMove.Guard) => (0.3, 0),
        (ArenaMove.Peck, ArenaMove.Flap) => (1.0, 0.5),
        (ArenaMove.Peck, _) => (1.0, 0),
        (ArenaMove.Guard, ArenaMove.Peck) => (0.4, 0), // counter
        (ArenaMove.Guard, _) => (0, 0),
        (ArenaMove.Flap, ArenaMove.Guard) => (0.15, 0),
        (ArenaMove.Flap, _) => (0.5, 0),
        (ArenaMove.Special, ArenaMove.Flap) => (1.5, 1.0), // flapped clean over it
        (ArenaMove.Special, _) => (1.5, 0),
        _ => (0, 0),
    };

    /// <summary>A Special without full charge is just a Peck.</summary>
    public static ArenaMove Legal(Fighter f, ArenaMove m) => m == ArenaMove.Special && f.Charge < ChargeNeeded ? ArenaMove.Peck : m;

    /// <summary>Plays one turn: both moves at once. Updates HP and charge.</summary>
    public static TurnResult Resolve(Fighter a, ArenaMove ma, Fighter b, ArenaMove mb, Random rng)
    {
        ma = Legal(a, ma);
        mb = Legal(b, mb);
        var (toB, bDodge) = Damage(a, ma, mb, rng);
        var (toA, aDodge) = Damage(b, mb, ma, rng);
        b.Hp = Math.Max(0, b.Hp - toB);
        a.Hp = Math.Max(0, a.Hp - toA);
        a.Charge = NextCharge(a.Charge, ma);
        b.Charge = NextCharge(b.Charge, mb);
        return new TurnResult(ma, mb, toA, toB, aDodge, bDodge);
    }

    private static (int Damage, bool Dodged) Damage(Fighter user, ArenaMove move, ArenaMove targetMove, Random rng)
    {
        var (mult, dodge) = Hit(move, targetMove);
        if (mult <= 0) return (0, false);
        if (dodge > 0 && rng.NextDouble() < dodge) return (0, true);
        var variance = 0.85 + rng.NextDouble() * 0.3;
        return (Math.Max(1, (int)Math.Round(user.Attack * mult * variance)), false);
    }

    private static int NextCharge(int charge, ArenaMove used) => used switch
    {
        ArenaMove.Special => 0,
        ArenaMove.Guard => Math.Min(ChargeNeeded, charge + 2),
        _ => Math.Min(ChargeNeeded, charge + 1),
    };

    /// <summary>A bot's pick: specials when charged, otherwise a weighted mix.</summary>
    public static ArenaMove BotMove(Fighter self, Random rng)
    {
        if (self.Charge >= ChargeNeeded && rng.NextDouble() < 0.7) return ArenaMove.Special;
        var r = rng.NextDouble();
        if (self.Hp < self.MaxHp / 3 && r < 0.4) return ArenaMove.Guard;
        return r < 0.45 ? ArenaMove.Peck : r < 0.7 ? ArenaMove.Guard : ArenaMove.Flap;
    }

    /// <summary>0 = draw, 1 = A wins, 2 = B wins. After the last turn, the higher share of HP left wins.</summary>
    public static int Winner(Fighter a, Fighter b, bool lastTurn)
    {
        if (a.Fainted && b.Fainted) return 0;
        if (b.Fainted) return 1;
        if (a.Fainted) return 2;
        if (!lastTurn) return -1;
        var fa = (double)a.Hp / a.MaxHp;
        var fb = (double)b.Hp / b.MaxHp;
        return Math.Abs(fa - fb) < 0.001 ? 0 : fa > fb ? 1 : 2;
    }

    /// <summary>
    /// Spectator bets: the whole pool goes to those who backed the winner, split by stake (rounded down, leftovers to
    /// the biggest backer). A draw, or nobody backing the winner, refunds everyone.
    /// </summary>
    public static Dictionary<ulong, int> Payouts(IReadOnlyDictionary<ulong, (int Side, int Stake)> bets, int winner)
    {
        var pay = new Dictionary<ulong, int>();
        if (bets.Count == 0) return pay;
        var backers = bets.Where(b => b.Value.Side == winner).ToList();
        if (winner == 0 || backers.Count == 0)
        {
            foreach (var (id, bet) in bets) pay[id] = bet.Stake;
            return pay;
        }

        var pool = bets.Values.Sum(b => b.Stake);
        var backed = backers.Sum(b => b.Value.Stake);
        var paid = 0;
        foreach (var (id, bet) in backers)
        {
            var share = pool * bet.Stake / backed;
            pay[id] = share;
            paid += share;
        }

        var top = backers.OrderByDescending(b => b.Value.Stake).First().Key;
        pay[top] += pool - paid;
        return pay;
    }
}
