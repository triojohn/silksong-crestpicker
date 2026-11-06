using System;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Blocks hero control while our crest shop is up and releases it on every exit path.
/// The blocker token is a static object, so a lock left behind by a session that never
/// cleaned up can always be removed again by the next one. Only our own token is ever
/// added or removed: the shop FSM's own blockers and lock states stay untouched.
/// Never throws.
/// </summary>
internal static class ShopInputLock
{
    /// <summary>Stable blocker identity; the hero stores it in its own blocker set.</summary>
    private static readonly object Token = new object();

    /// <summary>True while our token is in the hero's blocker set.</summary>
    private static bool _blocked;

    /// <summary>
    /// Takes control away from the hero: removes any leftover lock of ours first, then
    /// adds the blocker and relinquishes control.
    /// </summary>
    /// <param name="reason">Short reason written to the log once per transition.</param>
    internal static void Block(string reason)
    {
        HeroController? hero = Hero();
        if (hero == null)
        {
            Plugin.Log.LogWarning($"[INPUT] block skipped ({reason}): no HeroController");
            return;
        }

        try
        {
            hero.RemoveInputBlocker(Token);
            hero.RegainControl();
            hero.AddInputBlocker(Token);
            hero.RelinquishControl();
            if (!_blocked)
            {
                Plugin.Log.LogInfo($"[INPUT] blocked ({reason})");
            }

            _blocked = true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[INPUT] block failed ({reason}): {ex.Message}");
        }
    }

    /// <summary>
    /// Releases our own lock only, and only when we actually hold one, so control
    /// relinquished by the game itself is never handed back by mistake.
    /// </summary>
    /// <param name="reason">Short reason written to the log once per transition.</param>
    internal static void Unblock(string reason)
    {
        if (!_blocked)
        {
            return;
        }

        _blocked = false;
        HeroController? hero = Hero();
        if (hero == null)
        {
            Plugin.Log.LogInfo($"[INPUT] unblocked ({reason}): no HeroController");
            return;
        }

        try
        {
            hero.RemoveInputBlocker(Token);
            hero.RegainControl();
            Plugin.Log.LogInfo($"[INPUT] unblocked ({reason})");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[INPUT] unblock failed ({reason}): {ex.Message}");
        }
    }

    /// <summary>Live hero controller, or null while none exists.</summary>
    private static HeroController? Hero()
    {
        try
        {
            return HeroController.instance;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
