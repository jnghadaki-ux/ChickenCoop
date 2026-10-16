using ChickenCoop.Logic;
using ChickenCoop.Model;
using Xunit;

namespace ChickenCoop.Tests;

public class ArenaTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private static Fighter F(int hp = 100, int atk = 10, int charge = 0) =>
        new() { Name = "F", MaxHp = hp, Hp = hp, Attack = atk, Charge = charge };

    /// <summary>A Random whose NextDouble always returns the same value (variance and dodges become predictable).</summary>
    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
    }

    [Fact]
    public void Stats_grow_with_the_pets_story_and_stay_bounded()
    {
        var c = new CoopConfig();
        var chick = new PetData();
        PetLogic.SetStage(chick, PetStage.Chick, c, Now, 1);
        var hen = new PetData();
        PetLogic.SetStage(hen, PetStage.Hen, c, Now, 1);
        hen.Stats.RoundsTogether = 10_000;
        hen.Stats.Feeds = 10_000;
        hen.Stats.Tricks = 10_000;

        var fc = ArenaLogic.FromPet(chick);
        var fh = ArenaLogic.FromPet(hen);
        Assert.Equal(60, fc.MaxHp);
        Assert.Equal(10, fc.Attack);
        Assert.True(fh.MaxHp > fc.MaxHp && fh.Attack > fc.Attack);
        Assert.True(fh.MaxHp <= 125 && fh.Attack <= 23); // a veteran is stronger, never untouchable
    }

    [Fact]
    public void Breeds_lean_different_ways()
    {
        var c = new CoopConfig();
        PetData Pet(Breed b)
        {
            var p = new PetData();
            PetLogic.SetStage(p, PetStage.Pullet, c, Now, 1);
            p.Breed = b;
            return p;
        }

        var silkie = ArenaLogic.FromPet(Pet(Breed.Silkie));
        var polish = ArenaLogic.FromPet(Pet(Breed.Polish));
        var catalana = ArenaLogic.FromPet(Pet(Breed.Catalana));
        Assert.True(silkie.MaxHp > catalana.MaxHp && catalana.MaxHp > polish.MaxHp);
        Assert.True(polish.Attack > catalana.Attack && catalana.Attack > silkie.Attack);
    }

    [Theory]
    [InlineData(ArenaMove.Peck, ArenaMove.Peck, 10, 10)]
    [InlineData(ArenaMove.Peck, ArenaMove.Guard, 4, 3)]      // guard blunts the peck and counters
    [InlineData(ArenaMove.Guard, ArenaMove.Guard, 0, 0)]
    [InlineData(ArenaMove.Flap, ArenaMove.Flap, 5, 5)]
    [InlineData(ArenaMove.Flap, ArenaMove.Guard, 0, 2)]
    public void Damage_table(ArenaMove a, ArenaMove b, int toA, int toB)
    {
        // NextDouble = 0.5: variance x1.0, and a 50% dodge does not happen.
        var fa = F();
        var fb = F();
        var r = ArenaLogic.Resolve(fa, a, fb, b, new FixedRandom(0.5));
        Assert.Equal(toA, r.DamageToA);
        Assert.Equal(toB, r.DamageToB);
    }

    [Fact]
    public void Special_breaks_guard_but_misses_a_flap()
    {
        var a = F(charge: 3);
        var b = F();
        var r = ArenaLogic.Resolve(a, ArenaMove.Special, b, ArenaMove.Guard, new FixedRandom(0.5));
        Assert.Equal(15, r.DamageToB);
        Assert.Equal(0, a.Charge);

        var c = F(charge: 3);
        var d = F();
        var r2 = ArenaLogic.Resolve(c, ArenaMove.Special, d, ArenaMove.Flap, new FixedRandom(0.5));
        Assert.Equal(0, r2.DamageToB);
        Assert.True(r2.BDodged);
        Assert.Equal(5, r2.DamageToA);
    }

    [Fact]
    public void Special_without_charge_is_a_peck_and_guard_charges_faster()
    {
        var a = F(charge: 1);
        var b = F();
        var r = ArenaLogic.Resolve(a, ArenaMove.Special, b, ArenaMove.Guard, new FixedRandom(0.5));
        Assert.Equal(ArenaMove.Peck, r.MoveA);
        Assert.Equal(2, a.Charge);
        Assert.Equal(2, b.Charge); // guard: +2
        ArenaLogic.Resolve(a, ArenaMove.Guard, b, ArenaMove.Guard, new FixedRandom(0.5));
        Assert.Equal(3, a.Charge); // capped
    }

    [Fact]
    public void Peck_against_flap_can_be_dodged()
    {
        var a = F();
        var b = F();
        var r = ArenaLogic.Resolve(a, ArenaMove.Peck, b, ArenaMove.Flap, new FixedRandom(0.1));
        Assert.True(r.BDodged);
        Assert.Equal(0, r.DamageToB);
    }

    [Fact]
    public void Winner_rules()
    {
        var a = F();
        var b = F();
        Assert.Equal(-1, ArenaLogic.Winner(a, b, false));
        Assert.Equal(0, ArenaLogic.Winner(a, b, true));
        b.Hp = 40;
        Assert.Equal(1, ArenaLogic.Winner(a, b, true));
        b.Hp = 0;
        Assert.Equal(1, ArenaLogic.Winner(a, b, false));
        a.Hp = 0;
        Assert.Equal(0, ArenaLogic.Winner(a, b, false));
    }

    [Fact]
    public void A_whole_duel_always_finishes()
    {
        var rng = new Random(3);
        for (var game = 0; game < 200; game++)
        {
            var a = F(80, 12);
            var b = F(90, 11);
            var winner = -1;
            for (var turn = 1; turn <= 12 && winner < 0; turn++)
            {
                ArenaLogic.Resolve(a, ArenaLogic.BotMove(a, rng), b, ArenaLogic.BotMove(b, rng), rng);
                winner = ArenaLogic.Winner(a, b, turn == 12);
            }

            Assert.InRange(winner, 0, 2);
        }
    }

    [Fact]
    public void Bets_pay_the_backers_of_the_winner()
    {
        var bets = new Dictionary<ulong, (int Side, int Stake)>
        {
            [1] = (1, 2),
            [2] = (1, 1),
            [3] = (2, 3),
        };
        var pay = ArenaLogic.Payouts(bets, 1);
        Assert.Equal(6, pay.Values.Sum()); // the whole pool
        Assert.Equal(4, pay[1]);
        Assert.Equal(2, pay[2]);
        Assert.False(pay.ContainsKey(3));
    }

    [Fact]
    public void Bets_are_refunded_on_a_draw_or_when_nobody_backed_the_winner()
    {
        var bets = new Dictionary<ulong, (int Side, int Stake)> { [1] = (1, 2), [2] = (1, 3) };
        Assert.Equal(new Dictionary<ulong, int> { [1] = 2, [2] = 3 }, ArenaLogic.Payouts(bets, 0));
        Assert.Equal(new Dictionary<ulong, int> { [1] = 2, [2] = 3 }, ArenaLogic.Payouts(bets, 2));
        Assert.Empty(ArenaLogic.Payouts(new Dictionary<ulong, (int, int)>(), 1));
    }

    [Fact]
    public void Arena_wins_have_a_leaderboard()
    {
        var c = new CoopConfig();
        var p = new PetData();
        PetLogic.SetStage(p, PetStage.Hen, c, Now, 1);
        p.Stats.ArenaWins = 4;
        Assert.Equal(4, PetLogic.BoardValue(p, Board.Arena));
    }
}
