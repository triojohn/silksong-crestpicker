using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TeamCherry.NestedFadeGroup;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Temporary diagnostics: every write to <see cref="NestedFadeGroupBase.AlphaSelf"/> of the
/// shop window is logged with the value, the group path and the caller stack, so the code
/// that keeps lowering the root alpha becomes visible in the log. Read-only, never throws.
/// </summary>
internal static class AlphaTrace
{
    /// <summary>Prefix of every line this class writes.</summary>
    internal const string Tag = "[ALPHATRACE]";

    /// <summary>Lines allowed per scene, so a fading window cannot flood the log.</summary>
    private const int LineLimit = 300;

    /// <summary>How many methods the caller chain shows.</summary>
    private const int StackFrames = 4;

    /// <summary>Part of the window name; groups below it are traced, others are ignored.</summary>
    private const string WindowMark = "Shop Menu";

    private static int _lines;

    /// <summary>True once the limit line was written for this scene.</summary>
    private static bool _limitLogged;

    /// <summary>Scene handle of the last write, so a scene change restarts the count.</summary>
    private static int _sceneHandle;

    /// <summary>
    /// Patches the <c>AlphaSelf</c> setter. Called from <see cref="Plugin.Awake"/>; a failure
    /// is logged there and leaves the rest of the plugin untouched.
    /// </summary>
    /// <param name="harmony">Harmony instance of the plugin.</param>
    internal static void Apply(Harmony harmony)
    {
        MethodInfo? setter = AccessTools.PropertySetter(
            typeof(NestedFadeGroupBase), nameof(NestedFadeGroupBase.AlphaSelf));
        if (setter == null)
        {
            Plugin.Log.LogWarning($"{Tag} apply failed: no setter for AlphaSelf");
            return;
        }

        harmony.Patch(setter, postfix: new HarmonyMethod(typeof(AlphaTrace), nameof(Postfix)));
        Plugin.Log.LogInfo($"{Tag} tracing {setter.DeclaringType?.FullName}.AlphaSelf");
    }

    /// <summary>Postfix of the setter: logs writes inside the shop window only.</summary>
    /// <param name="__instance">Fade group that got the value.</param>
    /// <param name="__0">Value that was written.</param>
    private static void Postfix(NestedFadeGroupBase __instance, float __0)
    {
        try
        {
            if (__instance == null)
            {
                return;
            }

            Transform transform = __instance.transform;
            if (!InShopWindow(transform))
            {
                return;
            }

            int handle = transform.gameObject.scene.handle;
            if (handle != _sceneHandle)
            {
                _sceneHandle = handle;
                _lines = 0;
                _limitLogged = false;
            }

            if (_limitLogged)
            {
                return;
            }

            if (_lines >= LineLimit)
            {
                _limitLogged = true;
                Plugin.Log.LogInfo($"{Tag} limit reached");
                return;
            }

            _lines++;
            Plugin.Log.LogInfo(Describe(transform, __0));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"{Tag} postfix failed: {ex.Message}");
        }
    }

    /// <summary>One trace line: path, value, our session flags and the caller chain.</summary>
    private static string Describe(Transform transform, float value)
    {
        StringBuilder line = new StringBuilder(Tag);
        line.Append($" path='{ShopCrestMenu.Path(transform)}'");
        line.Append($" value={value:F2}");
        line.Append($" ourOpen={ShopCrestMenu.OurOpen}");
        line.Append($" ourClosing={ShopCrestMenu.OurClosing}");
        line.Append($" stack={Caller()}");
        return line.ToString();
    }

    /// <summary>True when the transform or one of its parents is the shop window.</summary>
    private static bool InShopWindow(Transform transform)
    {
        Transform? current = transform;
        while (current != null)
        {
            if (current.name.IndexOf(WindowMark, StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    /// <summary>
    /// The last <see cref="StackFrames"/> methods that led to the write, the setter first,
    /// types and names only, no arguments.
    /// </summary>
    private static string Caller()
    {
        StackFrame[] frames = new StackTrace(2, false).GetFrames() ?? Array.Empty<StackFrame>();
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < frames.Length && i < StackFrames; i++)
        {
            MethodBase? method = frames[i].GetMethod();
            if (builder.Length > 0)
            {
                builder.Append(" <- ");
            }

            builder.Append(method?.DeclaringType?.Name ?? "?");
            builder.Append('.');
            builder.Append(method?.Name ?? "?");
        }

        return builder.Length == 0 ? "<none>" : builder.ToString();
    }
}
