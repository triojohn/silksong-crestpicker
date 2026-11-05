using System;
using System.Collections.Generic;
using System.Reflection;
using TMProOld;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Cosmetic pass over the live shop window while our crests are shown: more compact
/// crest icons, the crest name in place of the price, no currency counters, and our
/// own confirmation text. Every change is recorded first and undone by <see cref="End"/>.
/// Never throws.
/// </summary>
internal static class ShopCosmetics
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>Frames between two currency-counter sweeps.</summary>
    private const int CounterSweepInterval = 15;

    /// <summary>Fraction of the vanilla font size the name may shrink to.</summary>
    private const float MinFontSizeFactor = 0.35f;

    private static readonly FieldInfo? ItemSpriteField =
        typeof(ShopItemStats).GetField("itemSprite", PrivateInstance);

    private static readonly FieldInfo? CostSpriteField =
        typeof(ShopItemStats).GetField("costSprite", PrivateInstance);

    private static readonly FieldInfo? CostTextField =
        typeof(ShopItemStats).GetField("itemCostText", PrivateInstance);

    private static readonly FieldInfo? MaterialIconField =
        typeof(ShopItemStats).GetField("materialCostIcon", PrivateInstance);

    /// <summary>Vanilla local scale of every crest icon we rescaled.</summary>
    private static readonly Dictionary<Transform, Vector3> IconScales = new Dictionary<Transform, Vector3>();

    /// <summary>Vanilla local position of every row object we shifted.</summary>
    private static readonly Dictionary<Transform, Vector3> PositionStates = new Dictionary<Transform, Vector3>();

    /// <summary>Vanilla active state of every object we hid.</summary>
    private static readonly Dictionary<GameObject, bool> ObjectStates = new Dictionary<GameObject, bool>();

    /// <summary>Vanilla text settings of every price label we rewrote.</summary>
    private static readonly Dictionary<TMP_Text, TextState> TextStates =
        new Dictionary<TMP_Text, TextState>();

    private static Transform? _root;
    private static TMP_Text? _confirm;
    private static string? _confirmOriginal;
    private static int _frames;
    private static bool _missingLogged;

    /// <summary>Multiplier applied to the vanilla crest icon scale; 0.2 = five times smaller.</summary>
    internal static float IconScale = 0.2f;

    /// <summary>Shift of the row icon along the row, toward the centre.</summary>
    internal static float CrestIconOffsetX = 0.4f;

    /// <summary>Shift of the row crest name along the row, toward the centre.</summary>
    internal static float CrestNameOffsetX = -0.4f;

    /// <summary>Confirmation text written over the vanilla purchase prompt.</summary>
    internal static string ConfirmText = "Вы уверены?";

    /// <summary>True to enable word wrapping and auto-sizing on the name label.</summary>
    internal static bool ShrinkNameToFit = true;

    /// <summary>Remembers the window and applies the first cosmetic pass.</summary>
    /// <param name="root">Shop window root, the object holding <c>shop_control</c>.</param>
    internal static void Begin(Transform root)
    {
        try
        {
            _root = root;
            _frames = 0;
            _missingLogged = false;
            HideCounters(root);
            Apply(root);
            ShopCrestSchema.Begin(root);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPFX] cosmetic pass failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-applies the pass while the session is up: the game rebuilds rows and
    /// restores price labels on its own, and the FSM may bring the counters back.
    /// </summary>
    internal static void Tick()
    {
        if (_root == null)
        {
            return;
        }

        try
        {
            Apply(_root);
            ShopCrestSchema.Tick();
            if (_frames++ % CounterSweepInterval == 0)
            {
                HideCounters(_root);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPFX] cosmetic tick failed: {ex.Message}");
        }
    }

    /// <summary>Puts every touched object back the way the game left it.</summary>
    internal static void End()
    {
        _root = null;
        try
        {
            ShopCrestSchema.End();
            foreach (KeyValuePair<Transform, Vector3> pair in IconScales)
            {
                if (pair.Key != null)
                {
                    pair.Key.localScale = pair.Value;
                }
            }

            foreach (KeyValuePair<Transform, Vector3> pair in PositionStates)
            {
                if (pair.Key != null)
                {
                    pair.Key.localPosition = pair.Value;
                }
            }

            foreach (KeyValuePair<GameObject, bool> pair in ObjectStates)
            {
                if (pair.Key != null)
                {
                    pair.Key.SetActive(pair.Value);
                }
            }

            foreach (KeyValuePair<TMP_Text, TextState> pair in TextStates)
            {
                RestoreText(pair.Key, pair.Value);
            }

            if (_confirm != null && _confirmOriginal != null)
            {
                _confirm.text = _confirmOriginal;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPFX] cosmetic restore failed: {ex.Message}");
        }
        finally
        {
            IconScales.Clear();
            PositionStates.Clear();
            ObjectStates.Clear();
            TextStates.Clear();
            _confirm = null;
            _confirmOriginal = null;
        }
    }

    /// <summary>Applies every cosmetic change to the rows and the confirmation prompt.</summary>
    private static void Apply(Transform root)
    {
        foreach (ShopItemStats row in root.GetComponentsInChildren<ShopItemStats>(true))
        {
            if (row == null)
            {
                continue;
            }

            ToolCrest? crest = ShopCrestMenu.CrestFor(row.Item);
            if (crest == null)
            {
                continue;
            }

            ScaleIcon(row);
            ReplacePrice(row, crest.DisplayName);
            OffsetRow(row);
        }

        ApplyConfirm(root);
    }

    /// <summary>Pulls the row icon and the row name toward the centre of the row.</summary>
    private static void OffsetRow(ShopItemStats row)
    {
        if (ItemSpriteField?.GetValue(row) is SpriteRenderer icon && icon != null)
        {
            ShiftX(icon.transform, CrestIconOffsetX);
        }

        if (CostTextField?.GetValue(row) is TMP_Text label && label != null)
        {
            ShiftX(label.transform, CrestNameOffsetX);
        }
    }

    /// <summary>Applies <paramref name="dx"/> on top of the vanilla local position.</summary>
    private static void ShiftX(Transform target, float dx)
    {
        if (!PositionStates.TryGetValue(target, out Vector3 original))
        {
            original = target.localPosition;
            PositionStates[target] = original;
            Plugin.Log.LogInfo(
                $"[SHOPFX] row offset '{target.name}' orig={original.x:F3},{original.y:F3}" +
                $" dx={dx:F3} now={original.x + dx:F3}" +
                $" (CrestIconOffsetX={CrestIconOffsetX:F3} CrestNameOffsetX={CrestNameOffsetX:F3})");
        }

        Vector3 wanted = original + new Vector3(dx, 0f, 0f);
        if (target.localPosition != wanted)
        {
            target.localPosition = wanted;
        }
    }

    /// <summary>Row icon renderer, used by the details schema as a sorting reference.</summary>
    /// <param name="row">Row view of one shop entry.</param>
    internal static SpriteRenderer? RowIcon(ShopItemStats? row)
    {
        return row != null ? ItemSpriteField?.GetValue(row) as SpriteRenderer : null;
    }

    /// <summary>Shrinks the row's item sprite by <see cref="IconScale"/>.</summary>
    private static void ScaleIcon(ShopItemStats row)
    {
        if (!(ItemSpriteField?.GetValue(row) is SpriteRenderer icon) || icon == null)
        {
            return;
        }

        Transform target = icon.transform;
        if (!IconScales.TryGetValue(target, out Vector3 original))
        {
            original = target.localScale;
            IconScales[target] = original;
        }

        Vector3 wanted = original * IconScale;
        if (target.localScale != wanted)
        {
            target.localScale = wanted;
        }
    }

    /// <summary>Writes the crest name where the price was and hides the cost icons.</summary>
    private static void ReplacePrice(ShopItemStats row, string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        HideRowIcon(CostSpriteField?.GetValue(row) as SpriteRenderer);
        HideRowIcon(MaterialIconField?.GetValue(row) as SpriteRenderer);

        if (CostTextField?.GetValue(row) is TMP_Text label && label != null)
        {
            FitLabel(label);
            if (label.text != name)
            {
                label.text = name!;
            }
        }
    }

    /// <summary>Hides one cost icon, remembering its vanilla state once.</summary>
    private static void HideRowIcon(SpriteRenderer? renderer)
    {
        if (renderer == null)
        {
            return;
        }

        GameObject go = renderer.gameObject;
        if (!ObjectStates.ContainsKey(go))
        {
            ObjectStates[go] = go.activeSelf;
        }

        if (go.activeSelf)
        {
            go.SetActive(false);
        }
    }

    /// <summary>Makes the price label wrap and shrink instead of overflowing its row.</summary>
    private static void FitLabel(TMP_Text label)
    {
        if (TextStates.ContainsKey(label))
        {
            return;
        }

        TextState state = TextState.Capture(label);
        TextStates[label] = state;
        if (!ShrinkNameToFit)
        {
            return;
        }

        try
        {
            label.enableAutoSizing = false;
            label.fontSizeMax = Mathf.Max(1f, state.FontSize);
            label.fontSizeMin = Mathf.Max(1f, state.FontSize * MinFontSizeFactor);
            label.enableWordWrapping = true;
            label.enableAutoSizing = true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPFX] label fit failed on '{label.name}': {ex.Message}");
        }
    }

    /// <summary>Puts one price label back to its vanilla text and settings.</summary>
    private static void RestoreText(TMP_Text? label, TextState state)
    {
        if (label == null)
        {
            return;
        }

        try
        {
            label.enableAutoSizing = false;
            label.enableWordWrapping = state.WordWrap;
            label.fontSizeMin = state.FontSizeMin;
            label.fontSizeMax = state.FontSizeMax;
            label.fontSize = state.FontSize;
            label.enableAutoSizing = state.AutoSize;
            label.text = state.Text;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPFX] label restore failed on '{label.name}': {ex.Message}");
        }
    }

    /// <summary>Overwrites the purchase prompt while its group is showing.</summary>
    private static void ApplyConfirm(Transform root)
    {
        if (_confirm == null)
        {
            _confirm = FindConfirm(root);
            if (_confirm == null)
            {
                if (!_missingLogged)
                {
                    _missingLogged = true;
                    Plugin.Log.LogWarning("[SHOPFX] 'Item Confirm Group/Confirm/Confirm msg' not found.");
                }

                return;
            }
        }

        if (!_confirm.gameObject.activeInHierarchy || string.IsNullOrEmpty(_confirm.text))
        {
            return;
        }

        if (_confirmOriginal == null && _confirm.text != ConfirmText)
        {
            _confirmOriginal = _confirm.text;
        }

        if (_confirm.text != ConfirmText)
        {
            _confirm.text = ConfirmText;
        }
    }

    /// <summary>Locates the confirmation label by path, then by name.</summary>
    private static TMP_Text? FindConfirm(Transform root)
    {
        Transform? direct = root.Find("Item Confirm Group/Confirm/Confirm msg");
        TMP_Text? found = direct != null ? direct.GetComponent<TMP_Text>() : null;
        if (found != null)
        {
            return found;
        }

        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text != null && text.name == "Confirm msg")
            {
                return text;
            }
        }

        return null;
    }

    /// <summary>
    /// Keeps the currency counters hidden: <see cref="ShopMenuStock.HideCurrencyCounters"/>
    /// for the window's own counters, plus a stack hide for counters living under it.
    /// </summary>
    private static void HideCounters(Transform root)
    {
        foreach (ShopMenuStock stock in root.GetComponentsInChildren<ShopMenuStock>(true))
        {
            if (stock != null)
            {
                stock.HideCurrencyCounters();
            }
        }

        foreach (CurrencyCounter counter in root.GetComponentsInChildren<CurrencyCounter>(true))
        {
            if (counter != null)
            {
                counter.SetStackVisible(false);
            }
        }
    }

    /// <summary>Vanilla text settings of one price label.</summary>
    private readonly struct TextState
    {
        internal TextState(string text, bool wordWrap, bool autoSize,
            float fontSize, float fontSizeMin, float fontSizeMax)
        {
            Text = text;
            WordWrap = wordWrap;
            AutoSize = autoSize;
            FontSize = fontSize;
            FontSizeMin = fontSizeMin;
            FontSizeMax = fontSizeMax;
        }

        internal string Text { get; }

        internal bool WordWrap { get; }

        internal bool AutoSize { get; }

        internal float FontSize { get; }

        internal float FontSizeMin { get; }

        internal float FontSizeMax { get; }

        internal static TextState Capture(TMP_Text label)
        {
            return new TextState(label.text, label.enableWordWrapping, label.enableAutoSizing,
                label.fontSize, label.fontSizeMin, label.fontSizeMax);
        }
    }
}
