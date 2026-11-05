using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Diagnostics for the shop stock: every <c>ShopMenuStock.SetStock</c> call is logged
/// with the array it got, the field before and after, and the calling stack, so an
/// overwrite by the game FSM is visible as a log line. Read-only, never throws.
/// </summary>
internal static class StockTrace
{
    /// <summary>Prefix of every line this class writes.</summary>
    internal const string Tag = "[STOCKTRACE]";

    /// <summary>Calls logged while our shop session is not open.</summary>
    private const int AlwaysLogLimit = 100;

    private static readonly FieldInfo? StockField =
        typeof(ShopMenuStock).GetField("stock", BindingFlags.Instance | BindingFlags.NonPublic);

    private static int _calls;

    /// <summary>True while a SetStock call should be written to the log.</summary>
    internal static bool Enabled
    {
        get
        {
            try
            {
                return ShopCrestMenu.IsOpen || _calls <= AlwaysLogLimit;
            }
            catch (Exception)
            {
                return _calls <= AlwaysLogLimit;
            }
        }
    }

    /// <summary>Counts a logged call so the always-on window ends.</summary>
    /// <returns>The call number, starting at one.</returns>
    internal static int Count()
    {
        return ++_calls;
    }

    /// <summary>
    /// First three item names of an array, so different stock arrays are told apart
    /// in the log. Null and empty arrays get their own marker.
    /// </summary>
    /// <param name="items">Stock array; may be null.</param>
    /// <returns>Comma-separated names, never null.</returns>
    internal static string Names(ShopItem[]? items)
    {
        if (items == null)
        {
            return "<null>";
        }

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < items.Length && i < 3; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            ShopItem item = items[i];
            builder.Append(item != null ? item.name : "<none>");
        }

        return builder.Length == 0 ? "<empty>" : builder.ToString();
    }

    /// <summary>Real value of the private <c>stock</c> field: length and first names.</summary>
    /// <param name="stock">Component to read; must not be null.</param>
    /// <returns>Readable snapshot such as <c>6[A,B,C]</c>.</returns>
    internal static string Field(ShopMenuStock stock)
    {
        ShopItem[]? current = StockField?.GetValue(stock) as ShopItem[];
        return $"{(current != null ? current.Length : -1)}[{Names(current)}]";
    }

    /// <summary>Up to <paramref name="frames"/> callers of the current call, type and method.</summary>
    /// <param name="frames">How many stack frames to write.</param>
    /// <returns>Chain separated by <c>&lt;-</c>, never null.</returns>
    internal static string Caller(int frames)
    {
        StackFrame[] stack = new StackTrace(2, false).GetFrames() ?? Array.Empty<StackFrame>();
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < stack.Length && i < frames; i++)
        {
            MethodBase? method = stack[i].GetMethod();
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

/// <summary>
/// Logs both sides of every <c>ShopMenuStock.SetStock</c>: what went in, what the
/// field held before, what it holds after, and who called. Temporary diagnostics.
/// </summary>
[HarmonyPatch(typeof(ShopMenuStock), nameof(ShopMenuStock.SetStock), new[] { typeof(ShopItem[]) })]
internal static class StockSetTracePatch
{
    private static bool _logThis;
    private static int _callNumber;

    private static void Prefix(ShopMenuStock __instance, ShopItem[] newStock)
    {
        _logThis = false;
        try
        {
            if (!StockTrace.Enabled)
            {
                return;
            }

            _logThis = true;
            _callNumber = StockTrace.Count();
            Plugin.Log.LogInfo(
                $"{StockTrace.Tag} #{_callNumber} SetStock BEFORE path='{ShopCrestMenu.Path(__instance.transform)}'" +
                $" activeSelf={__instance.gameObject.activeSelf}" +
                $" new={newStock?.Length ?? -1}[{StockTrace.Names(newStock)}]" +
                $" cur={StockTrace.Field(__instance)}" +
                $" callers={StockTrace.Caller(6)}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"{StockTrace.Tag} prefix failed: {ex.Message}");
        }
    }

    private static void Postfix(ShopMenuStock __instance)
    {
        if (!_logThis)
        {
            return;
        }

        try
        {
            Plugin.Log.LogInfo(
                $"{StockTrace.Tag} #{_callNumber} SetStock AFTER  path='{ShopCrestMenu.Path(__instance.transform)}'" +
                $" field={StockTrace.Field(__instance)}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"{StockTrace.Tag} postfix failed: {ex.Message}");
        }
    }
}
