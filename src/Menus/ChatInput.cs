using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace ChickenCoop.Menus;

/// <summary>
/// "Type it in chat" prompts (renaming from the menu). While a prompt is pending, the player's next plain chat line is
/// captured and not shown to anyone; lines starting with ! or / still run as commands, and "cancel" aborts.
/// </summary>
public static class ChatInput
{
    private sealed record Pending(Action<CCSPlayerController, string> OnText, Action<CCSPlayerController>? OnCancel, double Expires);

    private static readonly Dictionary<int, Pending> Prompts = new();

    public static bool IsWaiting(CCSPlayerController? player) =>
        player != null && Prompts.TryGetValue(player.Slot, out var p) && Server.EngineTime < p.Expires;

    public static void Ask(CCSPlayerController player, Action<CCSPlayerController, string> onText,
        Action<CCSPlayerController>? onCancel = null, float timeoutSeconds = 45f)
    {
        if (!player.IsValid || player.IsBot) return;
        Prompts[player.Slot] = new Pending(onText, onCancel, Server.EngineTime + Math.Max(5f, timeoutSeconds));
    }

    /// <summary>"say" / "say_team" listener.</summary>
    public static HookResult OnSay(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid || !Prompts.TryGetValue(player.Slot, out var p)) return HookResult.Continue;
        if (Server.EngineTime >= p.Expires)
        {
            Prompts.Remove(player.Slot);
            return HookResult.Continue;
        }

        var text = info.GetArg(1).Trim();
        if (text.Length == 0 || text[0] == '!' || text[0] == '/') return HookResult.Continue;
        Prompts.Remove(player.Slot);
        if (text.Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            Run(() => p.OnCancel?.Invoke(player));
            return HookResult.Handled;
        }

        // Next frame: the callback may open menus or change entities, which must not happen inside the hook.
        Server.NextFrame(() =>
        {
            if (player.IsValid) Run(() => p.OnText(player, text.Length > 64 ? text[..64] : text));
        });
        return HookResult.Handled;
    }

    private static void Run(Action a)
    {
        try
        {
            a();
        }
        catch (Exception ex)
        {
            ChickenCoopPlugin.Instance?.Logger.LogError(ex, "Chicken Coop chat prompt failed");
        }
    }

    public static void Forget(int slot) => Prompts.Remove(slot);

    public static void Reset() => Prompts.Clear();
}
