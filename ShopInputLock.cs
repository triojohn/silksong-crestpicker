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

    /// <summary>True when <see cref="Block"/> changed the pause flag and it must be restored.</summary>
    private static bool _pauseSaved;

    /// <summary>Vanilla <c>PauseAllowed</c> value captured on block.</summary>
    private static bool _pauseWasAllowed = true;

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
            BlockPause();
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
            RestorePause();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[INPUT] unblock failed ({reason}): {ex.Message}");
        }
    }

    /// <summary>
    /// Remembers the vanilla pause flag and blocks pausing while our shop is up, the same
    /// way the vanilla NPC dialogue keeps it blocked. One log line per transition.
    /// </summary>
    private static void BlockPause()
    {
        if (_pauseSaved)
        {
            return;
        }

        InputHandler? input = Input();
        if (input == null)
        {
            Plugin.Log.LogWarning("[INPUT] pause prevent skipped: no InputHandler");
            return;
        }

        _pauseSaved = true;
        _pauseWasAllowed = input.PauseAllowed;
        input.PreventPause();
        Plugin.Log.LogInfo($"[INPUT] pause prevented (was={_pauseWasAllowed})");
    }

    /// <summary>
    /// Restores exactly the capture taken in <see cref="BlockPause"/>: a pause that was
    /// already blocked stays blocked, so the game's own lock is never handed back.
    /// </summary>
    private static void RestorePause()
    {
        if (!_pauseSaved)
        {
            return;
        }

        _pauseSaved = false;
        InputHandler? input = Input();
        if (input != null && _pauseWasAllowed)
        {
            input.AllowPause();
        }

        Plugin.Log.LogInfo($"[INPUT] pause restored (to={_pauseWasAllowed})");
    }

    /// <summary>Live game input handler, or null while none exists.</summary>
    private static InputHandler? Input()
    {
        try
        {
            GameManager? manager = GameManager.instance;
            return manager != null ? manager.inputHandler : null;
        }
        catch (Exception)
        {
            return null;
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
