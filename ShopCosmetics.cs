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

    /// <summary>Vanilla local position of every row name we centred.</summary>
    private static readonly Dictionary<Transform, Vector3> PositionStates = new Dictionary<Transform, Vector3>();

    /// <summary>Row names whose centring has been logged once.</summary>
    private static readonly HashSet<Transform> LoggedRows = new HashSet<Transform>();

    /// <summary>Vanilla active state of every object we hid.</summary>
    private static readonly Dictionary<GameObject, bool> ObjectStates = new Dictionary<GameObject, bool>();

    /// <summary>Vanilla text settings of every price label we rewrote.</summary>
    private static readonly Dictionary<TMP_Text, TextState> TextStates =
        new Dictionary<TMP_Text, TextState>();

    /// <summary>Path of the item list inside the shop window.</summary>
    private const string ListPath = "Item List Group/Item List";

    private static Transform? _root;
    private static Transform? _list;
    private static Transform? _listGroup;
    private static TMP_Text? _confirm;
    private static string? _confirmOriginal;
    private static TMP_Text? _confirmButton;
    private static string? _confirmButtonOriginal;
    private static int _frames;
    private static bool _missingLogged;

    /// <summary>Confirmation text written over the vanilla purchase prompt.</summary>
    internal static string ConfirmText = "Вы уверены?";

    /// <summary>Label written over the vanilla confirmation button.</summary>
    internal static string ConfirmButtonText = "Выбрать";

    /// <summary>True to enable word wrapping and auto-sizing on the name label.</summary>
    internal static bool ShrinkNameToFit = true;

    /// <summary>Remembers the window and applies the first cosmetic pass.</summary>
    /// <param name="root">Shop window root, the object holding <c>shop_control</c>.</param>
    internal static void Begin(Transform root)
    {
        try
        {
            _root = root;
            _list = null;
            _listGroup = null;
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
        _list = null;
        _listGroup = null;
        try
        {
            ShopCrestSchema.End();
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

            if (_confirmButton != null && _confirmButtonOriginal != null)
            {
                _confirmButton.text = _confirmButtonOriginal;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPFX] cosmetic restore failed: {ex.Message}");
        }
        finally
        {
            PositionStates.Clear();
            LoggedRows.Clear();
            ObjectStates.Clear();
            TextStates.Clear();
            _confirm = null;
            _confirmOriginal = null;
            _confirmButton = null;
            _confirmButtonOriginal = null;
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

            HideSprite(ItemSpriteField?.GetValue(row) as SpriteRenderer);
            ReplacePrice(row, crest.DisplayName);
            CenterName(row);
        }

        ApplyConfirm(root);
        ApplyConfirmButton(root);
    }

    /// <summary>
    /// Centres the name label on the drawing rows, from what the text actually draws:
    /// the mesh bounds centre, not the rect, whose size lags behind the fitter. The
    /// shift is recomputed every frame from the current centre, never accumulated.
    /// </summary>
    private static void CenterName(ShopItemStats row)
    {
        if (!(CostTextField?.GetValue(row) is TMP_Text label) || label == null)
        {
            return;
        }

        Transform target = label.transform;
        if (!PositionStates.ContainsKey(target))
        {
            PositionStates[target] = target.localPosition;
        }

        if (label.alignment != TextAlignmentOptions.Center)
        {
            label.alignment = TextAlignmentOptions.Center;
        }

        Renderer? renderer = label.GetComponent<Renderer>();
        if (renderer == null || !CentreTargetX(target, out float targetX, out int rows, out string source))
        {
            return;
        }

        float textX = renderer.bounds.center.x;
        float delta = targetX - textX;
        if (Mathf.Abs(delta) > 0.0005f)
        {
            Vector3 position = target.position;
            position.x += delta;
            target.position = position;
        }

        if (LoggedRows.Add(target))
        {
            Plugin.Log.LogInfo($"[SHOPFX] centre '{target.name}' rows={rows}" +
                $" target={targetX:F3} textX={textX:F3} delta={delta:F3} src={source}");
        }
    }

    /// <summary>
    /// World X to centre the row labels on: the mean of the drawn row positions, which
    /// stays put while the labels move. Falls back to the list rect, then the list group.
    /// </summary>
    /// <param name="exclude">Label transform to leave out of any bounds union.</param>
    private static bool CentreTargetX(Transform exclude, out float x, out int rows, out string source)
    {
        ResolveTargets();

        if (RowCentreX(out x, out rows))
        {
            source = "rows";
            return true;
        }

        if (CentreX(_list, exclude, out x))
        {
            source = "list";
            return true;
        }

        if (CentreX(_listGroup, exclude, out x))
        {
            source = "listGroup";
            return true;
        }

        source = "none";
        return false;
    }

    /// <summary>Mean world X of the rows that are actually drawn, with their count.</summary>
    private static bool RowCentreX(out float x, out int rows)
    {
        x = 0f;
        rows = 0;
        if (_root == null)
        {
            return false;
        }

        float sum = 0f;
        foreach (ShopItemStats row in _root.GetComponentsInChildren<ShopItemStats>(true))
        {
            if (row == null || !row.gameObject.activeInHierarchy)
            {
                continue;
            }

            sum += row.transform.position.x;
            rows++;
        }

        if (rows == 0)
        {
            return false;
        }

        x = sum / rows;
        return true;
    }

    /// <summary>Resolves the list and the list group once per session.</summary>
    private static void ResolveTargets()
    {
        if (_root == null || _list != null)
        {
            return;
        }

        _listGroup = _root.Find("Item List Group") ?? FindByName(_root, "Item List Group");
        _list = _root.Find(ListPath) ?? FindByName(_root, "Item List");
    }

    /// <summary>First descendant with the given name.</summary>
    private static Transform? FindByName(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child != null && child.name == name)
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>
    /// World X of a panel: rect corners when it has a real rect, else the centre of what
    /// it draws, else its position. Our own name labels are left out of the union: they
    /// are moved to this very centre, so counting them would feed the target back.
    /// </summary>
    /// <param name="exclude">Label transform to leave out of the bounds union.</param>
    private static bool CentreX(Transform? target, Transform? exclude, out float x)
    {
        x = 0f;
        if (target == null)
        {
            return false;
        }

        if (target is RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            float min = Mathf.Min(corners[0].x, corners[2].x);
            float max = Mathf.Max(corners[0].x, corners[2].x);
            if (max - min > 0.0001f)
            {
                x = (min + max) * 0.5f;
                return true;
            }
        }

        Renderer? own = target.GetComponent<Renderer>();
        if (own != null && own.enabled && !IsNameLabel(own.transform, exclude))
        {
            Bounds ownBounds = own.bounds;
            if (ownBounds.size.x > 0.0001f)
            {
                x = ownBounds.center.x;
                return true;
            }
        }

        bool any = false;
        Bounds bounds = new Bounds(target.position, Vector3.zero);
        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (IsNameLabel(renderer.transform, exclude))
            {
                continue;
            }

            if (any)
            {
                bounds.Encapsulate(renderer.bounds);
            }
            else
            {
                bounds = renderer.bounds;
                any = true;
            }
        }

        x = any ? bounds.center.x : target.position.x;
        return true;
    }

    /// <summary>True for the price label we rewrote, on any row.</summary>
    private static bool IsNameLabel(Transform target, Transform? exclude)
    {
        if (exclude != null && (target == exclude || target.IsChildOf(exclude)))
        {
            return true;
        }

        return target.name == "Item cost";
    }

    /// <summary>Row icon renderer, used by the details schema as a sorting reference.</summary>
    /// <param name="row">Row view of one shop entry.</param>
    internal static SpriteRenderer? RowIcon(ShopItemStats? row)
    {
        return row != null ? ItemSpriteField?.GetValue(row) as SpriteRenderer : null;
    }

    /// <summary>Writes the crest name where the price was and hides the cost icons.</summary>
    private static void ReplacePrice(ShopItemStats row, string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        HideSprite(CostSpriteField?.GetValue(row) as SpriteRenderer);
        HideSprite(MaterialIconField?.GetValue(row) as SpriteRenderer);

        if (CostTextField?.GetValue(row) is TMP_Text label && label != null)
        {
            FitLabel(label);
            if (label.text != name)
            {
                label.text = name!;
            }
        }
    }

    /// <summary>Hides one sprite object, remembering its vanilla state once.</summary>
    private static void HideSprite(SpriteRenderer? renderer)
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
            label.alignment = state.Alignment;
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

    /// <summary>Overwrites the confirmation button label while its group is showing.</summary>
    private static void ApplyConfirmButton(Transform root)
    {
        if (string.IsNullOrEmpty(ConfirmButtonText))
        {
            return;
        }

        if (_confirmButton == null)
        {
            _confirmButton = FindConfirmButton(root);
            if (_confirmButton == null)
            {
                if (!_missingLogged)
                {
                    _missingLogged = true;
                    Plugin.Log.LogWarning("[SHOPFX] 'Action Confirm/Text' not found.");
                }

                return;
            }
        }

        if (!_confirmButton.gameObject.activeInHierarchy)
        {
            return;
        }

        string wanted = ConfirmButtonText;
        if (_confirmButtonOriginal == null && _confirmButton.text != wanted)
        {
            _confirmButtonOriginal = _confirmButton.text;
        }

        if (_confirmButton.text != wanted)
        {
            _confirmButton.text = wanted;
        }
    }

    /// <summary>Locates the confirmation button label by path, then by parent name.</summary>
    private static TMP_Text? FindConfirmButton(Transform root)
    {
        string[] paths = { "Item Confirm Group/Action Confirm/Text", "Action Confirm/Text" };
        foreach (string path in paths)
        {
            Transform? direct = root.Find(path);
            TMP_Text? found = direct != null ? direct.GetComponent<TMP_Text>() : null;
            if (found != null)
            {
                return found;
            }
        }

        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text != null && text.name == "Text" && text.transform.parent != null
                && text.transform.parent.name == "Action Confirm")
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
            float fontSize, float fontSizeMin, float fontSizeMax, TextAlignmentOptions alignment)
        {
            Text = text;
            WordWrap = wordWrap;
            AutoSize = autoSize;
            FontSize = fontSize;
            FontSizeMin = fontSizeMin;
            FontSizeMax = fontSizeMax;
            Alignment = alignment;
        }

        internal string Text { get; }

        internal bool WordWrap { get; }

        internal bool AutoSize { get; }

        internal float FontSize { get; }

        internal float FontSizeMin { get; }

        internal float FontSizeMax { get; }

        internal TextAlignmentOptions Alignment { get; }

        internal static TextState Capture(TMP_Text label)
        {
            return new TextState(label.text, label.enableWordWrapping, label.enableAutoSizing,
                label.fontSize, label.fontSizeMin, label.fontSizeMax, label.alignment);
        }
    }
}
