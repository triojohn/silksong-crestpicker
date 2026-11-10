using System;

namespace CrestPicker.Pure;

/// <summary>
/// Which alpha the shared shop window root must hold, per FSM state, and how to walk
/// toward it. Vanilla drives the root <c>AlphaSelf</c>; we only steer it in settled
/// states, so its own fades stay untouched.
/// Free of Unity types so it can be unit tested.
/// </summary>
public static class WindowPinPolicy
{
    /// <summary>
    /// Target root alpha: <c>1</c> in the settled <c>Open</c> state, <c>0</c> in
    /// <c>Idle</c>, <c>null</c> anywhere else. Every transitional state
    /// (<c>Open Window</c>, <c>Item List Up</c>, <c>Down</c>, <c>Close*</c>,
    /// <c>Special Close</c>) returns <c>null</c> and is left alone.
    /// </summary>
    public static float? RootTarget(string? fsmState)
    {
        if (fsmState == "Open")
        {
            return 1f;
        }

        if (fsmState == "Idle")
        {
            return 0f;
        }

        return null;
    }

    /// <summary>
    /// One linear step from <paramref name="current"/> toward <paramref name="target"/>.
    /// A full <c>0..1</c> travel takes <paramref name="duration"/> seconds, so a single
    /// step is <c>deltaTime / duration</c> large. The result is clamped between the two
    /// values, so it never overshoots and lands exactly on the target on the last step.
    /// A non-positive <paramref name="duration"/> snaps straight to the target.
    /// </summary>
    public static float StepTowards(float current, float target, float deltaTime, float duration = 0.2f)
    {
        if (duration <= 0f)
        {
            return target;
        }

        float step = Math.Sign(target - current) * (deltaTime / duration);
        float next = current + step;
        return Math.Clamp(next, Math.Min(current, target), Math.Max(current, target));
    }
}
