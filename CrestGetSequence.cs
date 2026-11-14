using System;
using System.Collections;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Long crest-get sequence, the same shape as the vanilla stand pickup
/// (<c>CollectableItemPickup.Pickup</c>): hero animation, item-get reaction, follow-up
/// clips. Runs after the window that granted the crest has closed, because the hero and
/// the effect are hidden behind an open panel. Never throws; animation control is handed
/// back on every path.
/// </summary>
internal sealed class CrestGetSequence : MonoBehaviour
{
    /// <summary>First pickup clip, the vanilla stand variant.</summary>
    private const string ClipStart = "Collect Stand 1";

    /// <summary>Fallback for the first clip when the stand variant is absent.</summary>
    private const string ClipStartFallback = "Collect Normal 1";

    /// <summary>Second pickup clip, played right after the reaction.</summary>
    private const string ClipSecond = "Collect Stand 2";

    /// <summary>Last pickup clip; the sequence waits for it to finish.</summary>
    private const string ClipEnd = "Collect Stand 3";

    /// <summary>Vanilla pause between the first clip and the reaction.</summary>
    private const float FirstWait = 0.75f;

    /// <summary>Vanilla pause between the reaction and the last clip.</summary>
    private const float SecondWait = 0.5f;

    /// <summary>Crest the sequence plays for; used in the log lines only.</summary>
    private string _crestName = string.Empty;

    /// <summary>
    /// Starts the sequence for a crest that was just unlocked. A second call while one
    /// is running is ignored, so a repeated close event cannot double the animation.
    /// </summary>
    /// <param name="crestName">Name of the crest, for the log lines.</param>
    internal static void Run(string crestName)
    {
        try
        {
            HeroController? hero = HeroController.instance;
            if (hero == null)
            {
                Plugin.Log.LogWarning("[GET] hero unavailable, playing the short reaction only");
                CrestPickerService.PlayGetReaction(crestName);
                return;
            }

            if (hero.GetComponent<CrestGetSequence>() != null)
            {
                Plugin.Log.LogInfo($"[GET] sequence already running, skipped '{crestName}'");
                return;
            }

            CrestGetSequence sequence = hero.gameObject.AddComponent<CrestGetSequence>();
            sequence._crestName = crestName;
            Plugin.Log.LogInfo($"[GET] sequence start '{crestName}'");
            sequence.StartCoroutine(sequence.GuardedPlay());
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GET] sequence start failed: {ex.Message}");
            CrestPickerService.PlayGetReaction(crestName);
        }
    }

    /// <summary>Vanilla stand pickup order: animation, reaction, animation, last clip.</summary>
    private IEnumerator Play()
    {
        HeroController? hero = HeroController.instance;
        HeroController? stoppedHero = null;
        try
        {
            HeroAnimationController? heroAnim = hero != null ? hero.AnimCtrl : null;
            tk2dSpriteAnimator? animator = heroAnim != null ? heroAnim.animator : null;
            if (hero == null || heroAnim == null || animator == null)
            {
                Plugin.Log.LogWarning($"[GET] hero animation unavailable for '{_crestName}'");
                CrestPickerService.PlayGetReaction(_crestName);
                yield break;
            }

            hero.StopAnimationControl();
            stoppedHero = hero;

            tk2dSpriteAnimationClip? first =
                heroAnim.GetClip(ClipStart) ?? heroAnim.GetClip(ClipStartFallback);
            if (first == null)
            {
                Plugin.Log.LogWarning($"[GET] clip missing '{ClipStart}'");
                CrestPickerService.PlayGetReaction(_crestName);
                yield break;
            }

            animator.Play(first);
            yield return new WaitForSeconds(FirstWait);

            CrestPickerService.PlayGetReaction(_crestName);

            tk2dSpriteAnimationClip? second = heroAnim.GetClip(ClipSecond);
            if (second == null)
            {
                Plugin.Log.LogWarning($"[GET] clip missing '{ClipSecond}'");
                yield break;
            }

            animator.Play(second);
            yield return new WaitForSeconds(SecondWait);

            tk2dSpriteAnimationClip? last = heroAnim.GetClip(ClipEnd);
            if (last == null)
            {
                Plugin.Log.LogWarning($"[GET] clip missing '{ClipEnd}'");
                yield break;
            }

            yield return animator.PlayAnimWait(last);
            Plugin.Log.LogInfo("[GET] sequence done");
        }
        finally
        {
            if (stoppedHero != null)
            {
                try
                {
                    stoppedHero.StartAnimationControl();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[GET] animation control restore failed: {ex.Message}");
                }
            }

            Destroy(this);
        }
    }

    /// <summary>
    /// Drives <see cref="Play"/> and turns any failure into one log line: a coroutine
    /// cannot catch around its own yields, so the catch lives in this wrapper.
    /// </summary>
    private IEnumerator GuardedPlay()
    {
        IEnumerator steps = Play();
        try
        {
            while (true)
            {
                bool moved;
                try
                {
                    moved = steps.MoveNext();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[GET] sequence failed for '{_crestName}': {ex.Message}");
                    yield break;
                }

                if (!moved)
                {
                    yield break;
                }

                yield return steps.Current;
            }
        }
        finally
        {
            // A coroutine killed by Unity never resumes its inner iterator, so dispose it
            // here: that is what runs Play's finally and hands animation control back.
            (steps as IDisposable)?.Dispose();
        }
    }
}
