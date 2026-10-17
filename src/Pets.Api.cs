using ChickenCoop.Logic;
using ChickenCoop.Model;
using ChickenCoop.Shared;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace ChickenCoop;

/// <summary>The <see cref="IChickenCoopApi"/> other plugins get through the "chickencoop:api" capability.</summary>
internal sealed class CoopApi : IChickenCoopApi
{
    private readonly ChickenCoopPlugin _plugin;

    public CoopApi(ChickenCoopPlugin plugin) => _plugin = plugin;

    public event Action<CCSPlayerController, int>? StageChanged;
    public event Action<CCSPlayerController, CCSPlayerController?>? PetKilled;
    public event Action<CCSPlayerController, string>? MoodChanged;
    public event Action<CCSPlayerController?, CCSPlayerController?, bool>? DuelFinished;
    public event Action<CCSPlayerController, CCSPlayerController>? EggGifted;
    public event Action<CCSPlayerController, string>? CollectionAdded;

    private PetData? Pet(CCSPlayerController? p) => p is { IsValid: true } ? _plugin.Data(p) : null;

    public int GetStage(CCSPlayerController player) => (int)(Pet(player)?.Stage ?? PetStage.None);

    public string? GetPetName(CCSPlayerController player) => Pet(player) is { IsHatched: true } p ? p.Name : null;

    public string? GetBreed(CCSPlayerController player) => Pet(player) is { ShowsBreed: true } p ? p.Breed.ToString() : null;

    public string? GetShiny(CCSPlayerController player) => Pet(player) is { IsShiny: true } p ? p.Shiny : null;

    public bool IsPetOut(CCSPlayerController player) => player is { IsValid: true } && _plugin.IsPetOut(player);

    public int GetFeedBags(CCSPlayerController player) => Pet(player)?.FeedBags ?? 0;

    public int GiveFeedBags(CCSPlayerController player, int amount)
    {
        var p = Pet(player);
        if (p == null) return 0;
        p.FeedBags = Math.Clamp(p.FeedBags + amount, 0, Math.Max(_plugin.Config.Food.MaxBags, 0));
        _plugin.MarkDirty();
        _plugin.LogTransfer($"api {amount:+#;-#;0} bags -> {ChickenCoopPlugin.Who(player)}");
        if (amount > 0) _plugin.Tell(player, "api.bags", amount, p.FeedBags);
        return p.FeedBags;
    }

    public int GiveBasketEggs(CCSPlayerController player, int amount)
    {
        var p = Pet(player);
        if (p == null) return 0;
        var added = 0;
        for (var i = 0; i < amount; i++)
            if (PetLogic.CollectEgg(p, _plugin.Config)) added++;
        _plugin.MarkDirty();
        if (added > 0) _plugin.Tell(player, "api.eggs", added, p.BasketEggs);
        return added;
    }

    public bool GiveEgg(CCSPlayerController player)
    {
        var p = Pet(player);
        if (p == null || p.HasPet) return false;
        PetLogic.NewEgg(p, ChickenCoopPlugin.NewSeed(player), _plugin.Config, DateTime.UtcNow);
        _plugin.MarkDirty();
        _plugin.Tell(player, "egg.found", PetLogic.Duration(PetLogic.StageSeconds(PetStage.Egg, _plugin.Config)));
        _plugin.TrySpawnPet(player);
        RaiseStage(player, PetStage.Egg);
        return true;
    }

    public bool UnlockHat(CCSPlayerController player, string hatId)
    {
        var p = Pet(player);
        var hat = Catalog.FindHat(hatId ?? "");
        if (p == null || hat == null) return false;
        if (!p.GiftedHats.Contains(hat.Id))
        {
            p.GiftedHats.Add(hat.Id);
            _plugin.MarkDirty();
            _plugin.Tell(player, "hat.unlocked", hat.Name);
        }

        return true;
    }

    public string? GetMood(CCSPlayerController player) =>
        Pet(player) is { IsHatched: true } p ? PetLogic.MoodOf(p).ToString().ToLowerInvariant() : null;

    public double GetHappiness(CCSPlayerController player) => Pet(player) is { IsHatched: true } p ? p.Happiness : -1;

    public double AddHappiness(CCSPlayerController player, double amount)
    {
        if (Pet(player) is not { IsHatched: true } p) return -1;
        _plugin.WithUnlocks(player, p, () => PetLogic.Cheer(p, amount, _plugin.Config));
        return p.Happiness;
    }

    public string? GetAbility(CCSPlayerController player) => Pet(player) is { } p ? PetLogic.EffectiveAbility(p, _plugin.Config) : null;

    public IReadOnlyList<string> GetCollection(CCSPlayerController player) =>
        Pet(player) is { } p ? p.Collection.ToList() : Array.Empty<string>();

    public void SuppressPet(CCSPlayerController player, bool suppress) => _plugin.Suppress(player, suppress);

    public void SuppressAll(bool suppress) => _plugin.SuppressEveryone(suppress);

    internal void RaiseStage(CCSPlayerController player, PetStage stage) =>
        Safe(() => StageChanged?.Invoke(player, (int)stage));

    internal void RaiseKilled(CCSPlayerController owner, CCSPlayerController? killer) =>
        Safe(() => PetKilled?.Invoke(owner, killer));

    internal void RaiseMood(CCSPlayerController owner, Mood mood) =>
        Safe(() => MoodChanged?.Invoke(owner, mood.ToString().ToLowerInvariant()));

    internal void RaiseDuel(CCSPlayerController? winner, CCSPlayerController? loser, bool draw) =>
        Safe(() => DuelFinished?.Invoke(winner is { IsValid: true } ? winner : null, loser is { IsValid: true } ? loser : null, draw));

    internal void RaiseGift(CCSPlayerController from, CCSPlayerController to) => Safe(() => EggGifted?.Invoke(from, to));

    internal void RaiseCollection(CCSPlayerController player, string id) => Safe(() => CollectionAdded?.Invoke(player, id));

    private void Safe(Action a)
    {
        try
        {
            a();
        }
        catch (Exception ex)
        {
            _plugin.Logger.LogError(ex, "A Chicken Coop API listener failed");
        }
    }
}

public sealed partial class ChickenCoopPlugin
{
    internal void Suppress(CCSPlayerController player, bool suppress)
    {
        if (!player.IsValid) return;
        if (suppress)
        {
            _suppressed.Add(player.Slot);
            if (_actors.TryGetValue(player.Slot, out var a)) Despawn(a, false);
        }
        else if (_suppressed.Remove(player.Slot))
        {
            TrySpawnPet(player);
        }
    }

    internal void SuppressEveryone(bool suppress)
    {
        if (_suppressAll == suppress) return;
        _suppressAll = suppress;
        if (suppress)
            foreach (var a in _actors.Values) Despawn(a, false);
        else
            foreach (var p in Players()) TrySpawnPet(p);
    }
}
