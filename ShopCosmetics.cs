using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using CrestPicker.Pure;
using TeamCherry.NestedFadeGroup;
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

    /// <summary>Name to write into each row label in the late pass.</summary>
    private static readonly Dictionary<TMP_Text, string> RowNames =
        new Dictionary<TMP_Text, string>();

    /// <summary>Our own name label per row, used when the vanilla label draws nothing.</summary>
    private static readonly Dictionary<TMP_Text, TMP_Text> FallbackLabels =
        new Dictionary<TMP_Text, TMP_Text>();

    /// <summary>Frame each row label was first seen on, so the empty-mesh test waits two frames.</summary>
    private static readonly Dictionary<TMP_Text, int> MeshChecked = new Dictionary<TMP_Text, int>();

    /// <summary>Vanilla labels hidden behind a fallback label.</summary>
    private static readonly HashSet<TMP_Text> HiddenLabels = new HashSet<TMP_Text>();

    /// <summary>Rows whose fallback label has been reported hidden.</summary>
    private static readonly HashSet<TMP_Text> HiddenReported = new HashSet<TMP_Text>();

    /// <summary>True once the fallback-label count has been logged for this session.</summary>
    private static bool _fallbackLogged;

    /// <summary>Object name of our own row label.</summary>
    private const string OwnLabelName = "CrestPicker Row Name";

    /// <summary>
    /// Row label source, fixed at <c>Own</c>: the row name is drawn by our own label, cloned
    /// from the visible details text and given a fixed size. The old <c>General/RowLabelMode</c>
    /// cfg key is gone, so a leftover value in an existing cfg changes nothing.
    /// </summary>
    private const string RowLabelMode = "Own";

    /// <summary>
    /// Diagnostic switch: when true the row label draws an ASCII probe
    /// (<c>AAAA &lt;index&gt;</c>) instead of the crest name, which separates a font or glyph
    /// problem from a placement or render problem.
    /// </summary>
    internal static bool DebugAsciiRowLabel;

    /// <summary>Visible TMP text of the details panel, cloned for the row labels.</summary>
    private static TMP_Text? _labelSource;

    /// <summary>Details texts already written to the log by the source scan.</summary>
    private static readonly HashSet<TMP_Text> SourceLogged = new HashSet<TMP_Text>();

    /// <summary>True once the invisibility check of our label has been logged.</summary>
    private static bool _invisibleLogged;

    /// <summary>True once the chosen label source has been logged.</summary>
    private static bool _sourceDiagLogged;

    /// <summary>Build attempts made for each row label, keyed by the vanilla label.</summary>
    private static readonly Dictionary<TMP_Text, int> LabelAttempts = new Dictionary<TMP_Text, int>();

    /// <summary>Rows whose label we stopped retrying this session.</summary>
    private static readonly HashSet<TMP_Text> LabelGivenUp = new HashSet<TMP_Text>();

    /// <summary>Outstanding row labels, logged when the count changes.</summary>
    private static int _readyLogged;

    /// <summary>Rows whose missing label has already been reported, one log line per row.</summary>
    private static readonly HashSet<int> MissingLabelRows = new HashSet<int>();

    /// <summary>Why the last label build failed, written into the one missing-label log line.</summary>
    private static string _labelFailure = "unknown";

    /// <summary>True once the first own label and its reference have been logged.</summary>
    private static bool _labelDiagLogged;

    /// <summary>True once the world placement of a pinned label has been logged.</summary>
    private static bool _pinLogged;

    /// <summary>Shared corner buffer for the row-versus-list overlap test.</summary>
    private static readonly Vector3[] Corners = new Vector3[4];

    /// <summary>Max label build attempts per row, one per frame, about a second at 60 fps.</summary>
    private const int MaxLabelAttempts = 60;

    /// <summary>Mesh bounds centre X of every row label, measured before our first shift.</summary>
    private static readonly Dictionary<Transform, float> BaselineCentres =
        new Dictionary<Transform, float>();

    /// <summary>World X of every row label before our first shift.</summary>
    private static readonly Dictionary<Transform, float> BaselinePositions =
        new Dictionary<Transform, float>();

    /// <summary>Path of the item list inside the shop window.</summary>
    private const string ListPath = "Item List Group/Item List";

    /// <summary>Path of the details panel inside the shop window.</summary>
    private const string DetailsPath = "Item List Group/Item Details";

    private static Transform? _root;
    private static Transform? _list;
    private static Transform? _listGroup;
    private static Transform? _details;
    private static Transform? _window;
    private static Transform? _backboard;

    /// <summary>
    /// Selection bar drawn at the selected row (<c>selector</c> under the list group).
    /// Re-resolved every <see cref="SelectorSearchInterval"/> frames while null, because the
    /// window may rebuild it; the row label centres on it horizontally.
    /// </summary>
    private static Transform? _selector;

    /// <summary>Frames between two searches for a missing selection bar.</summary>
    private const int SelectorSearchInterval = 15;
    private static TMP_Text? _confirm;
    private static string? _confirmOriginal;
    private static TMP_Text? _confirmButton;
    private static string? _confirmButtonOriginal;

    /// <summary>Purchase confirmation text the vanilla FSM writes.</summary>
    private static TMP_Text? _purchaseLabel;
    private static string? _purchaseOriginal;
    private static int _frames;
    private static bool _missingLogged;
    private static bool _candidatesLogged;
    private static bool _sourceWarned;

    /// <summary>
    /// World X the row labels centre on, measured once per shop session and then reused.
    /// The source geometry grows with the shifted text, so re-measuring every frame
    /// feeds the shift back into the target and the labels drift.
    /// </summary>
    private static float? _centreTargetX;

    /// <summary>Source name that produced <see cref="_centreTargetX"/>.</summary>
    private static string _centreSource = "none";

    /// <summary>Row count seen when <see cref="_centreTargetX"/> was measured.</summary>
    private static int _centreRows;

    /// <summary>True while the one-off post-shift residual measurement waits for the next frame.</summary>
    private static bool _residualPending;

    /// <summary>True once the residual has been measured and logged for this session.</summary>
    private static bool _residualDone;

    /// <summary>Label whose residual is measured.</summary>
    private static Transform? _residualLabel;

    /// <summary>Target world X the residual is compared against.</summary>
    private static float _residualTargetX;

    /// <summary>Frame the residual shift was applied on.</summary>
    private static int _residualFrame;

    /// <summary>
    /// Config key naming the world X the row labels are centred on. Read on every call,
    /// so an edited cfg file takes effect without a restart. Bound by <see cref="Plugin"/>.
    /// </summary>
    internal static ConfigEntry<string>? CentreSourceConfig;

    /// <summary>
    /// World offset added to the row icon position before the label is placed there.
    /// Read on every call, so an edited cfg file takes effect without a restart.
    /// </summary>
    internal static ConfigEntry<float>? RowLabelOffsetX;

    /// <summary>World vertical offset added to the row icon position for the label.</summary>
    internal static ConfigEntry<float>? RowLabelOffsetY;

    /// <summary>Source names <see cref="CrestPicker.Pure.CentreSource"/> accepts.</summary>
    private static readonly string[] CentreSourceNames =
    {
        "columns", "rows", "list", "column", "columnCentre", "listGroup", "backboard", "window", "original",
    };

    /// <summary>Confirmation text written over the vanilla purchase prompt.</summary>
    internal static string ConfirmText = "Вы уверены?";

    /// <summary>Label written over the vanilla confirmation button.</summary>
    internal static string ConfirmButtonText = "Выбрать";

    /// <summary>Text written over the vanilla purchase confirmation line.</summary>
    internal static string PurchaseText = "Знак получен";

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
            _details = null;
            _window = null;
            _backboard = null;
            _selector = null;
            _frames = 0;
            _missingLogged = false;
            _candidatesLogged = false;
            _sourceWarned = false;
            _fallbackLogged = false;
            _labelSource = null;
            SourceLogged.Clear();
            _invisibleLogged = false;
            _sourceDiagLogged = false;
            _labelDiagLogged = false;
            _pinLogged = false;
            LabelAttempts.Clear();
            LabelGivenUp.Clear();
            MissingLabelRows.Clear();
            HiddenReported.Clear();
            _readyLogged = 0;
            _centreTargetX = null;
            _centreSource = "none";
            _centreRows = 0;
            _residualPending = false;
            _residualDone = false;
            _residualLabel = null;
            _residualTargetX = 0f;
            _residualFrame = 0;
            BaselineCentres.Clear();
            BaselinePositions.Clear();
            _confirmButton = null;
            _confirmButtonOriginal = null;
            _purchaseLabel = null;
            _purchaseOriginal = null;
            HideCounters(root);
            CaptureConfirmButton(root);
            CapturePurchaseText(root);
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

    /// <summary>Late per-frame pass: the FSM writes the confirm label in its own Update,
    /// so our rewrite must be the last word of the frame.</summary>
    internal static void LateTick()
    {
        if (_root == null)
        {
            return;
        }

        try
        {
            PinLabelTransforms();
            ApplyRowNames();
            SyncLabelVisibility();
            ApplyConfirmButton();
            ApplyPurchaseText();
            ShopCrestSchema.LateTick();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPFX] confirm button late pass failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Puts every own row label onto the icon of its row plus the configured offset. The
    /// world point is corrected by the label rect centre, so the drawn text is centred on
    /// the icon; Z comes from the vanilla price label, rotation and scale too. The clone
    /// does not follow the window when the window is moved to the hero, so the world
    /// placement is written absolutely each frame, before the text and the visibility pass.
    /// </summary>
    private static void PinLabelTransforms()
    {
        if (_selector == null && _frames % SelectorSearchInterval == 0)
        {
            _selector = FindSelector();
        }

        foreach (KeyValuePair<TMP_Text, TMP_Text> pair in FallbackLabels)
        {
            TMP_Text? vanilla = pair.Key;
            TMP_Text? label = pair.Value;
            if (vanilla == null || label == null)
            {
                continue;
            }

            Transform source = vanilla.transform;
            Transform target = label.transform;
            target.rotation = source.rotation;

            Vector3 parentScale = target.parent != null ? target.parent.lossyScale : Vector3.one;
            Vector3 worldScale = source.lossyScale;
            target.localScale = new Vector3(
                SafeRatio(worldScale.x, parentScale.x),
                SafeRatio(worldScale.y, parentScale.y),
                SafeRatio(worldScale.z, parentScale.z));

            Vector3 position = source.position;
            ShopItemStats? row = vanilla.GetComponentInParent<ShopItemStats>();
            SpriteRenderer? icon = RowIcon(row);
            float anchorX = _selector != null ? _selector.position.x : (icon != null ? icon.transform.position.x : 0f);
            float targetX;
            float targetY;
            if (_selector != null)
            {
                targetX = anchorX + (RowLabelOffsetX?.Value ?? 0f);
                targetY = (icon != null ? icon.transform.position.y : position.y) + (RowLabelOffsetY?.Value ?? 0f);
            }
            else if (icon != null)
            {
                targetX = icon.transform.position.x + (RowLabelOffsetX?.Value ?? 0f);
                targetY = icon.transform.position.y + (RowLabelOffsetY?.Value ?? 0f);
            }
            else
            {
                targetX = RowCentreX(vanilla, position.x);
                targetY = position.y;
            }

            position.x = targetX - LabelRectOffset(target).x;
            position.y = targetY - LabelRectOffset(target).y;
            if (Vector3.Distance(position, target.position) > 0.0001f)
            {
                target.position = position;
            }

            if (!_pinLogged)
            {
                _pinLogged = true;
                int index = row != null ? RowIndex(row, vanilla) : vanilla.transform.GetSiblingIndex();
                Renderer? own = label.GetComponent<Renderer>();
                Vector3 shift = LabelRectOffset(target);
                Plugin.Log.LogInfo($"[SHOPFX] row label placed row={index}"
                    + $" anchor={(_selector != null ? "selector" : "icon")}"
                    + $" selectorX={(_selector != null ? _selector.position.x : float.NaN):0.###}"
                    + $" iconX={(icon != null ? icon.transform.position.x : float.NaN):0.###}"
                    + $" labelX={target.position.x + shift.x:0.###}"
                    + $" labelY={target.position.y + shift.y:0.###}"
                    + $" alpha={RowAlpha(row):0.###}"
                    + $" visible={(own != null && own.enabled)}");
            }
        }
    }

    /// <summary>World X of the centre of the row rect; the price label X when the row has no rect.</summary>
    /// <param name="vanilla">Vanilla price label inside the row.</param>
    /// <param name="fallback">Value returned when the row rect is not available.</param>
    private static float RowCentreX(TMP_Text vanilla, float fallback)
    {
        ShopItemStats? row = vanilla.GetComponentInParent<ShopItemStats>();
        if (row == null || !(row.transform is RectTransform rowRect))
        {
            return fallback;
        }

        return rowRect.TransformPoint(rowRect.rect.center).x;
    }

    /// <summary>
    /// World offset from the label transform to the centre of its own rect. The clone
    /// keeps the price pivot, so the drawn text is not centred on the transform itself.
    /// </summary>
    /// <param name="label">Transform of our row label.</param>
    private static Vector3 LabelRectOffset(Transform label)
    {
        if (!(label is RectTransform rect))
        {
            return Vector3.zero;
        }

        return rect.TransformPoint(rect.rect.center) - label.position;
    }

    /// <summary>
    /// Converts a wanted world scale into a local scale under a parent of the given world
    /// scale; a zero axis falls back to one instead of dividing by zero.
    /// </summary>
    /// <param name="world">Wanted world size of the axis.</param>
    /// <param name="parent">World size of the same axis on the parent.</param>
    /// <returns>Local scale of that axis.</returns>
    private static float SafeRatio(float world, float parent)
    {
        return Mathf.Abs(parent) > 0.00001f ? world / parent : 1f;
    }

    /// <summary>
    /// Late pass over the rows: repeats the name <c>ShopItemStats.SetItem</c> may have
    /// overwritten with the price during its own Update, and checks once, two frames after
    /// the label was first seen, whether the vanilla mesh draws anything.
    /// </summary>
    private static void ApplyRowNames()
    {
        foreach (KeyValuePair<TMP_Text, string> pair in RowNames)
        {
            TMP_Text? label = pair.Key;
            if (label == null)
            {
                continue;
            }

            if (label.text != pair.Value)
            {
                label.text = pair.Value;
            }

            if (FallbackLabels.TryGetValue(label, out TMP_Text existing))
            {
                if (existing != null)
                {
                    if (existing.text != pair.Value)
                    {
                        existing.text = pair.Value;
                    }

                    continue;
                }

                FallbackLabels.Remove(label);
                HiddenLabels.Remove(label);
                MeshChecked.Remove(label);
            }

            if (!MeshChecked.TryGetValue(label, out int seen))
            {
                MeshChecked[label] = _frames;
                continue;
            }

            if (seen == int.MaxValue || _frames - seen < 2)
            {
                continue;
            }

            MeshChecked[label] = int.MaxValue;
            label.ForceMeshUpdate();
            Renderer? renderer = label.GetComponent<Renderer>();
            if (renderer != null && renderer.bounds.size.x > 0.0001f)
            {
                continue;
            }

            AddFallbackLabel(label, pair.Value);
        }
    }

    /// <summary>
    /// Keeps our own row labels in step with the row they sit on. A label must vanish with
    /// its row when the row scrolls out of the list, so nothing is forced on: the alpha is
    /// read from the row fade group (both the vanilla price label and the row icon are
    /// hidden by us, so their alpha says nothing) and the renderer is only enabled while
    /// that row is actually drawn.
    /// </summary>
    private static void SyncLabelVisibility()
    {
        foreach (KeyValuePair<TMP_Text, TMP_Text> pair in FallbackLabels)
        {
            TMP_Text? vanilla = pair.Key;
            TMP_Text? label = pair.Value;
            if (vanilla == null || label == null)
            {
                continue;
            }

            Renderer? renderer = label.GetComponent<Renderer>();
            if (renderer == null)
            {
                continue;
            }

            ShopItemStats? row = vanilla.GetComponentInParent<ShopItemStats>();
            float alpha = RowAlpha(row);
            bool rowLive = row != null && row.gameObject.activeInHierarchy;
            bool inside = RowInsideList(vanilla);
            bool empty = renderer.bounds.size.x <= 0.0001f;
            bool visible = rowLive && inside && !empty && alpha > 0.01f;

            if (renderer.enabled != visible)
            {
                renderer.enabled = visible;
            }

            if (visible)
            {
                HiddenReported.Remove(vanilla);
                Color colour = label.color;
                if (!Mathf.Approximately(colour.a, alpha))
                {
                    colour.a = alpha;
                    label.color = colour;
                }

                continue;
            }

            ReportLabelHidden(vanilla, row, rowLive, inside, !empty);
        }
    }

    /// <summary>Logs once per row why its fallback label is not drawn.</summary>
    /// <param name="vanilla">Vanilla price label of the row.</param>
    /// <param name="row">Row view of the shop entry, may be null.</param>
    /// <param name="rowLive">True while the row object is active.</param>
    /// <param name="inside">True while the row overlaps the item list.</param>
    /// <param name="hasMesh">True while the label has a non-empty mesh.</param>
    private static void ReportLabelHidden(
        TMP_Text vanilla, ShopItemStats? row, bool rowLive, bool inside, bool hasMesh)
    {
        if (!HiddenReported.Add(vanilla))
        {
            return;
        }

        string why = !rowLive ? "inactive" : !inside ? "outside" : !hasMesh ? "empty" : "alpha";
        int index = row != null ? RowIndex(row, vanilla) : vanilla.transform.GetSiblingIndex();
        Plugin.Log.LogInfo($"[SHOPFX] row label hidden row={index} why={why}");
    }

    /// <summary>Alpha the row is drawn with, taken from its fade group.</summary>
    /// <param name="row">Row view of the shop entry, may be null.</param>
    private static float RowAlpha(ShopItemStats? row)
    {
        if (row == null)
        {
            return 1f;
        }

        NestedFadeGroupBase? group = row.GetComponentInParent<NestedFadeGroupBase>();
        return group != null ? group.AlphaTotal : 1f;
    }

    /// <summary>
    /// True while the row overlaps the item list. Our label is cloned from the details panel
    /// and so carries no list stencil, which lets a scrolled-out row draw under the window;
    /// the row rect is checked against the list rect to hide those rows.
    /// </summary>
    /// <param name="vanilla">Vanilla price label of the row.</param>
    private static bool RowInsideList(TMP_Text vanilla)
    {
        if (!(_list is RectTransform list))
        {
            return true;
        }

        ShopItemStats? row = vanilla.GetComponentInParent<ShopItemStats>();
        if (row == null || !(row.transform is RectTransform rowRect))
        {
            return true;
        }

        if (!WorldYBand(rowRect, out float rowMin, out float rowMax)
            || !WorldYBand(list, out float listMin, out float listMax))
        {
            return true;
        }

        return rowMax > listMin && rowMin < listMax;
    }

    /// <summary>World Y range covered by a rect, false when it has no geometry.</summary>
    /// <param name="rect">Rect to measure.</param>
    /// <param name="min">Lower world Y of the rect.</param>
    /// <param name="max">Upper world Y of the rect.</param>
    private static bool WorldYBand(RectTransform rect, out float min, out float max)
    {
        rect.GetWorldCorners(Corners);
        min = Mathf.Min(Corners[0].y, Corners[2].y);
        max = Mathf.Max(Corners[0].y, Corners[2].y);
        return max - min > 0.0001f;
    }

    /// <summary>
    /// Builds our own name label on the row and hides the vanilla price label. Used only
    /// when the vanilla label's mesh stays empty after a forced rebuild. The object is
    /// registered and the vanilla label hidden before the mesh is built, so a failed build
    /// can be rolled back without leaving orphans behind.
    /// </summary>
    /// <param name="vanilla">Row price label that draws nothing.</param>
    /// <param name="name">Crest name to draw.</param>
    private static void AddFallbackLabel(TMP_Text vanilla, string name)
    {
        GameObject? go = null;
        try
        {
            TMP_FontAsset? font = vanilla.font;
            if (font == null)
            {
                return;
            }

            ShopItemStats? row = vanilla.GetComponentInParent<ShopItemStats>();
            Transform parent = row != null ? row.transform : vanilla.transform.parent;
            go = new GameObject("CrestPicker Row Name");
            go.layer = vanilla.gameObject.layer;
            go.transform.SetParent(parent, true);
            go.transform.position = vanilla.transform.position;
            go.transform.rotation = vanilla.transform.rotation;

            TextMeshPro text = go.AddComponent<TextMeshPro>();
            text.font = font;
            text.fontSize = vanilla.fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = false;
            text.enableWordWrapping = false;
            text.OverflowMode = TextOverflowModes.Overflow;
            text.color = new Color(1f, 1f, 1f, 1f);
            text.text = name;

            RectTransform rect = text.rectTransform;
            rect.sizeDelta = FallbackSize(vanilla, row);
            rect.anchoredPosition = Vector2.zero;

            CopySorting(vanilla, go.GetComponent<MeshRenderer>());

            FallbackLabels[vanilla] = text;
            HiddenLabels.Add(vanilla);
            vanilla.enabled = false;

            text.ForceMeshUpdate();
            MeshRenderer? own = go.GetComponent<MeshRenderer>();
            if (own == null || own.bounds.size.x <= 0.0001f)
            {
                throw new InvalidOperationException("fallback label drew an empty mesh");
            }

            if (!_fallbackLogged)
            {
                _fallbackLogged = true;
                Plugin.Log.LogInfo($"[SHOPFX] row label fallback used rows={RowNames.Count}");
            }
        }
        catch (Exception ex)
        {
            RollbackFallback(vanilla, go);
            Plugin.Log.LogWarning($"[SHOPFX] row label fallback failed: {ex.Message}");
        }
    }

    /// <summary>Size of our own label: the vanilla label's rect, then the row's rect.</summary>
    /// <param name="vanilla">Row price label that draws nothing.</param>
    /// <param name="row">Row view of the entry, may be null.</param>
    private static Vector2 FallbackSize(TMP_Text vanilla, ShopItemStats? row)
    {
        if (vanilla.transform is RectTransform own && own.rect.width > 1f && own.rect.height > 1f)
        {
            return new Vector2(own.rect.width, own.rect.height);
        }

        if (row != null && row.transform is RectTransform rowRect
            && rowRect.rect.width > 1f && rowRect.rect.height > 1f)
        {
            return new Vector2(rowRect.rect.width, rowRect.rect.height);
        }

        return new Vector2(400f, 80f);
    }

    /// <summary>Copies the sorting layer of the vanilla label's own renderer onto ours.</summary>
    /// <param name="vanilla">Row price label that draws nothing.</param>
    /// <param name="renderer">Our own renderer, may be null.</param>
    private static void CopySorting(TMP_Text vanilla, MeshRenderer? renderer)
    {
        if (renderer == null)
        {
            return;
        }

        Renderer? source = vanilla.GetComponent<Renderer>();
        if (source == null || source == renderer)
        {
            source = RowIcon(vanilla.GetComponentInParent<ShopItemStats>());
        }

        if (source != null)
        {
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = source.sortingOrder;
        }
    }

    /// <summary>Drops a half-built fallback label and gives the vanilla label its mesh back.</summary>
    /// <param name="vanilla">Row price label we hid.</param>
    /// <param name="go">Object we created, may be null.</param>
    private static void RollbackFallback(TMP_Text vanilla, GameObject? go)
    {
        FallbackLabels.Remove(vanilla);
        HiddenLabels.Remove(vanilla);
        if (vanilla != null)
        {
            vanilla.enabled = true;
        }

        if (go != null)
        {
            UnityEngine.Object.Destroy(go);
        }
    }

    /// <summary>Puts every touched object back the way the game left it.</summary>
    internal static void End()
    {
        _root = null;
        _list = null;
        _listGroup = null;
        _details = null;
        _window = null;
        _backboard = null;
        _selector = null;
        _candidatesLogged = false;
        _sourceWarned = false;
        _fallbackLogged = false;
        _labelSource = null;
        SourceLogged.Clear();
        _invisibleLogged = false;
        _sourceDiagLogged = false;
        LabelAttempts.Clear();
        LabelGivenUp.Clear();
        _readyLogged = 0;
        _labelDiagLogged = false;
        _centreTargetX = null;
        _centreSource = "none";
        _centreRows = 0;
        _residualPending = false;
        _residualDone = false;
        _residualLabel = null;
        _residualTargetX = 0f;
        _residualFrame = 0;
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

            foreach (KeyValuePair<TMP_Text, TMP_Text> pair in FallbackLabels)
            {
                if (pair.Value != null)
                {
                    UnityEngine.Object.Destroy(pair.Value.gameObject);
                }
            }

            foreach (TMP_Text hidden in HiddenLabels)
            {
                if (hidden != null)
                {
                    hidden.enabled = true;
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

            if (_purchaseLabel != null && _purchaseOriginal != null)
            {
                _purchaseLabel.text = _purchaseOriginal;
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
            RowNames.Clear();
            FallbackLabels.Clear();
            MeshChecked.Clear();
            HiddenLabels.Clear();
            BaselineCentres.Clear();
            BaselinePositions.Clear();
            _confirm = null;
            _confirmOriginal = null;
            _confirmButton = null;
            _confirmButtonOriginal = null;
            _purchaseLabel = null;
            _purchaseOriginal = null;
        }
    }

    /// <summary>
    /// Drops our labels whose row was rebuilt or destroyed, so a fresh row never gets a
    /// second name label stacked on it.
    /// </summary>
    private static void PruneOwnLabels()
    {
        PruneLabelState();

        if (FallbackLabels.Count == 0)
        {
            return;
        }

        List<TMP_Text> dead = new List<TMP_Text>();
        foreach (KeyValuePair<TMP_Text, TMP_Text> pair in FallbackLabels)
        {
            if (pair.Key == null)
            {
                dead.Add(pair.Key!);
                if (pair.Value != null)
                {
                    UnityEngine.Object.Destroy(pair.Value.gameObject);
                }
            }
            else if (pair.Value == null)
            {
                dead.Add(pair.Key);
                HiddenLabels.Remove(pair.Key);
            }
        }

        foreach (TMP_Text key in dead)
        {
            FallbackLabels.Remove(key);
        }
    }

    /// <summary>Forgets retry counters of vanilla labels whose row was destroyed.</summary>
    private static void PruneLabelState()
    {
        if (LabelAttempts.Count > 0)
        {
            List<TMP_Text> stale = new List<TMP_Text>();
            foreach (TMP_Text key in LabelAttempts.Keys)
            {
                if (key == null)
                {
                    stale.Add(key!);
                }
            }

            foreach (TMP_Text key in stale)
            {
                LabelAttempts.Remove(key);
            }
        }

        if (LabelGivenUp.Count > 0)
        {
            List<TMP_Text> stale = new List<TMP_Text>();
            foreach (TMP_Text key in LabelGivenUp)
            {
                if (key == null)
                {
                    stale.Add(key!);
                }
            }

            foreach (TMP_Text key in stale)
            {
                LabelGivenUp.Remove(key);
            }
        }
    }

    /// <summary>
    /// Applies every cosmetic change to the rows and the confirmation prompt. A row keeps the
    /// vanilla selection highlight and our own fixed-size name label; its icon, cost sprite,
    /// material icon and the vanilla cost label are all hidden.
    /// </summary>
    private static void Apply(Transform root)
    {
        if (UsesOwnLabel)
        {
            PruneOwnLabels();
        }

        int rows = 0;
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

            rows++;
            HideSprite(ItemSpriteField?.GetValue(row) as SpriteRenderer);
            HideSprite(CostSpriteField?.GetValue(row) as SpriteRenderer);
            HideSprite(MaterialIconField?.GetValue(row) as SpriteRenderer);
            ReplacePrice(row, crest.DisplayName);
        }

        if (UsesOwnLabel)
        {
            ReportOwnLabels(rows);
        }

        ApplyConfirm(root);
    }

    /// <summary>
    /// Centres the name label on the drawing rows, from what the text actually draws:
    /// the mesh bounds centre, not the rect, whose size lags behind the fitter. Both the
    /// target and each label's baseline are measured once per shop session: the source
    /// geometry grows with the shifted text, so re-measuring would feed the shift back
    /// into the target and the labels would drift.
    /// </summary>
    private static void CenterName(ShopItemStats row)
    {
        if (!(CostTextField?.GetValue(row) is TMP_Text vanilla) || vanilla == null)
        {
            return;
        }

        TMP_Text label = OwnLabelFor(vanilla);

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
        if (renderer == null)
        {
            return;
        }

        if (_centreTargetX == null && !MeasureCentre(target, out _))
        {
            return;
        }

        float targetX = _centreTargetX ?? 0f;
        string source = _centreSource;
        int rows = _centreRows;

        if (source == "original")
        {
            if (PositionStates.TryGetValue(target, out Vector3 home) && target.localPosition.x != home.x)
            {
                Vector3 local = target.localPosition;
                local.x = home.x;
                target.localPosition = local;
            }

            if (LoggedRows.Add(target))
            {
                Plugin.Log.LogInfo($"[SHOPFX] centre '{target.name}' rows={rows}" +
                    $" target={targetX:F3} baseline={renderer.bounds.center.x:F3}" +
                    $" pos={target.position.x:F3} src={source}");
            }

            return;
        }

        if (!BaselineCentres.TryGetValue(target, out float baselineCentreX))
        {
            baselineCentreX = renderer.bounds.center.x;
            BaselineCentres[target] = baselineCentreX;
            BaselinePositions[target] = target.position.x;
            Plugin.Log.LogInfo($"[SHOPFX] centre '{target.name}' rows={rows}" +
                $" target={targetX:F3} baseline={baselineCentreX:F3}" +
                $" pos={BaselinePositions[target]:F3} src={source}");
        }

        Vector3 position = target.position;
        position.x = BaselinePositions[target] + (targetX - baselineCentreX);
        if (Mathf.Abs(position.x - target.position.x) > 0.0005f)
        {
            target.position = position;
        }

        TrackResidual(target, targetX);
    }

    /// <summary>
    /// Schedules a one-off residual check after the first shift, then measures it one frame
    /// later. Confirms the moved transform really moved the drawn text, i.e. that the
    /// target and the text share the same world space.
    /// </summary>
    /// <param name="target">Label that was just shifted.</param>
    /// <param name="targetX">Target world X applied to it.</param>
    private static void TrackResidual(Transform target, float targetX)
    {
        if (_residualDone)
        {
            return;
        }

        if (!_residualPending)
        {
            _residualPending = true;
            _residualLabel = target;
            _residualTargetX = targetX;
            _residualFrame = _frames;
            return;
        }

        if (_frames <= _residualFrame)
        {
            return;
        }

        _residualPending = false;
        _residualDone = true;
        LogResidual();
    }

    /// <summary>Logs how far the drawn text still sits from the target world X.</summary>
    private static void LogResidual()
    {
        if (_residualLabel == null)
        {
            return;
        }

        Renderer? renderer = TextRenderer(_residualLabel);
        if (renderer == null)
        {
            return;
        }

        float text = renderer.bounds.center.x;
        float residual = text - _residualTargetX;
        Plugin.Log.LogInfo($"[SHOPFX] centre residual '{_residualLabel.name}'" +
            $" target={_residualTargetX:F3} text={text:F3} residual={residual:F3}");
        if (Mathf.Abs(residual) > 0.05f)
        {
            Plugin.Log.LogWarning("[SHOPFX] centre residual too big, scale or pivot issue");
        }
    }

    /// <summary>Renderer of the TMP submesh under the label, else the label's own renderer.</summary>
    private static Renderer? TextRenderer(Transform label)
    {
        foreach (Transform sub in label)
        {
            if (sub.name != "New Game Object")
            {
                continue;
            }

            Renderer? submesh = sub.GetComponent<Renderer>();
            if (submesh != null && submesh.enabled)
            {
                return submesh;
            }
        }

        return label.GetComponent<Renderer>();
    }

    /// <summary>
    /// Measures the world X to centre the labels on, once per shop session, and caches it
    /// with its source name and row count. False while no source is available yet.
    /// </summary>
    /// <param name="exclude">Label transform to leave out of any bounds union.</param>
    /// <param name="x">Cached target world X on success.</param>
    private static bool MeasureCentre(Transform exclude, out float x)
    {
        x = 0f;
        if (!CentreTargetX(exclude, out x, out int rows, out string source))
        {
            return false;
        }

        if (!IsSane(x))
        {
            Plugin.Log.LogWarning($"[SHOPFX] centre source '{source}' gave a bad value ({x}), not caching it.");
            return false;
        }

        _centreTargetX = x;
        _centreSource = source;
        _centreRows = rows;
        return true;
    }

    /// <summary>
    /// World X to centre the row labels on, taken from the source named by the
    /// <c>General/TextCentreSource</c> config key. The <c>columns</c> source has no
    /// fallback: its anchors live in another space than the label, so a substitute would
    /// poison the cached target. The other sources fall back to the rows, then the list
    /// rect, then the list group.
    /// </summary>
    /// <param name="exclude">Label transform to leave out of any bounds union.</param>
    private static bool CentreTargetX(Transform exclude, out float x, out int rows, out string source)
    {
        ResolveTargets();
        source = ResolveSourceName();
        LogCandidatesOnce(exclude);

        if (source == "columns")
        {
            // Retry on later frames instead of caching a substitute anchor.
            rows = 0;
            return ColumnsX(out x, out _, out _) && IsSane(x);
        }

        if (TryCentre(source, exclude, out x) && IsSane(x))
        {
            RowCentreX(out _, out rows);
            return true;
        }

        if (RowCentreX(out x, out rows) && IsSane(x))
        {
            source = "rows";
            return true;
        }

        if (CentreX(_list, exclude, out x) && IsSane(x))
        {
            source = "list";
            return true;
        }

        if (CentreX(_listGroup, exclude, out x) && IsSane(x))
        {
            source = "listGroup";
            return true;
        }

        source = "none";
        return false;
    }

    /// <summary>World X of the centre for one named source; false when it has none yet.</summary>
    private static bool TryCentre(string source, Transform exclude, out float x)
    {
        switch (source)
        {
            case "columns":
                return ColumnsX(out x, out _, out _);
            case "rows":
                return RowCentreX(out x, out _);
            case "list":
                return CentreX(_list, exclude, out x);
            case "column":
                return ColumnX(exclude, out x) || CentreX(_list, exclude, out x) ||
                    RowCentreX(out x, out _);
            case "columnCentre":
                return ColumnCentreX(exclude, out x) || CentreX(_list, exclude, out x) ||
                    RowCentreX(out x, out _);
            case "listGroup":
                return CentreX(_listGroup, exclude, out x);
            case "backboard":
                return CentreX(_backboard, exclude, out x);
            case "window":
                return CentreX(_window, exclude, out x);
            case "original":
                return OriginalX(exclude, out x);
            default:
                return ColumnsX(out x, out _, out _);
        }
    }

    /// <summary>Configured source name, with an unknown value warned about once and replaced by <c>columns</c>.</summary>
    private static string ResolveSourceName()
    {
        string resolved = CrestPicker.Pure.CentreSource.Resolve(
            CentreSourceConfig?.Value, CentreSourceNames, "columns", out bool unknown);
        if (unknown && !_sourceWarned)
        {
            _sourceWarned = true;
            Plugin.Log.LogWarning(
                $"[SHOPFX] unknown TextCentreSource '{CentreSourceConfig?.Value}', using 'columns'.");
        }

        return resolved;
    }

    /// <summary>World X of the label's vanilla position, remembered before our first shift.</summary>
    private static bool OriginalX(Transform label, out float x)
    {
        x = 0f;
        if (!PositionStates.TryGetValue(label, out Vector3 home))
        {
            return false;
        }

        Vector3 world = label.parent != null ? label.parent.TransformPoint(home) : home;
        x = world.x;
        return true;
    }

    /// <summary>Logs every centring candidate once per shop session, so a cfg choice can be made from data.</summary>
    private static void LogCandidatesOnce(Transform exclude)
    {
        if (_candidatesLogged)
        {
            return;
        }

        _candidatesLogged = true;
        RowCentreX(out float rowX, out int rowCount);
        string rowText = rowCount > 0 ? rowX.ToString("F3") : "n/a";
        string backboardText = BackboardMinX(out float backboardMin) ? backboardMin.ToString("F3") : "n/a";
        string detailsText = DetailsMinX(out float detailsMin) ? detailsMin.ToString("F3") : "n/a";
        string columnsText = ColumnsX(out float columns, out _, out _) ? columns.ToString("F3") : "n/a";
        Plugin.Log.LogInfo($"[SHOPFX] centre candidates: rows={rowText} rowsUsed={rowCount}" +
            $" listMin={LeftText(_list)}" +
            $" column={ColumnText(exclude)} columnCentre={ColumnCentreText(exclude)}" +
            $" list={SpanText(_list, exclude)} listGroup={SpanText(_listGroup, exclude)}" +
            $" backboard={SpanText(_backboard, exclude)} window={SpanText(_window, exclude)}" +
            $" original={OriginalText(exclude)}" +
            $" backboardMin={backboardText} detailsMin={detailsText} columns={columnsText}" +
            $" chosen={ResolveSourceName()}");
    }

    /// <summary>Min/max world X of a panel, as <c>(min..max, centre..)</c>, or <c>n/a</c>.</summary>
    private static string SpanText(Transform? target, Transform? exclude)
    {
        if (!SpanX(target, exclude, out float min, out float max))
        {
            return "n/a";
        }

        return $"({min:F3}..{max:F3}, centre={(min + max) * 0.5f:F3})";
    }

    /// <summary>Min/max world X of a panel: rect corners when it has a real rect, else its renderers.</summary>
    private static bool SpanX(Transform? target, Transform? exclude, out float min, out float max)
    {
        min = 0f;
        max = 0f;
        if (target == null)
        {
            return false;
        }

        if (target is RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            float lo = Mathf.Min(corners[0].x, corners[2].x);
            float hi = Mathf.Max(corners[0].x, corners[2].x);
            if (hi - lo > 0.0001f)
            {
                min = lo;
                max = hi;
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

        if (!any)
        {
            return false;
        }

        min = bounds.min.x;
        max = bounds.max.x;
        return true;
    }

    /// <summary>Left edge world X of a panel, or false when the panel has no geometry yet.</summary>
    private static bool LeftEdgeX(Transform? target, Transform? exclude, out float min)
    {
        if (SpanX(target, exclude, out min, out _))
        {
            return true;
        }

        min = 0f;
        return false;
    }

    /// <summary>
    /// World X midway between the left edge of the item list and the left edge of the
    /// details panel, i.e. the centre of the list column. The list edge falls back to the
    /// list group, then the window; the details edge to the backboard.
    /// </summary>
    /// <param name="exclude">Label transform to leave out of any bounds union.</param>
    private static bool ColumnX(Transform? exclude, out float x)
    {
        x = 0f;
        bool haveList = LeftEdgeX(_list, exclude, out float listMin) ||
            LeftEdgeX(_listGroup, exclude, out listMin) ||
            LeftEdgeX(_window, exclude, out listMin);
        bool haveDetails = LeftEdgeX(_details, exclude, out float detailsMin) ||
            LeftEdgeX(_backboard, exclude, out detailsMin);
        if (!haveList || !haveDetails)
        {
            return false;
        }

        x = (listMin + detailsMin) * 0.5f;
        return true;
    }

    /// <summary>World X midway between the left edge of the details panel and the right edge of the window.</summary>
    /// <param name="exclude">Label transform to leave out of any bounds union.</param>
    private static bool ColumnCentreX(Transform? exclude, out float x)
    {
        x = 0f;
        bool haveDetails = LeftEdgeX(_details, exclude, out float detailsMin) ||
            LeftEdgeX(_backboard, exclude, out detailsMin);
        if (!haveDetails || !SpanX(_window, exclude, out _, out float windowMax))
        {
            return false;
        }

        x = (detailsMin + windowMax) * 0.5f;
        return true;
    }

    /// <summary>
    /// World X midway between the left edge of the backboard and the left edge of the
    /// details panel. Both anchors are structural and live in the same world space as the
    /// label, so the value can be cached. No substitute is taken when either is missing.
    /// </summary>
    /// <param name="x">Centre world X on success.</param>
    /// <param name="backboardMin">Left edge world X of the backboard.</param>
    /// <param name="detailsMin">Left edge world X of the details panel.</param>
    private static bool ColumnsX(out float x, out float backboardMin, out float detailsMin)
    {
        x = 0f;
        backboardMin = 0f;
        detailsMin = 0f;
        ResolveTargets();
        if (!BackboardMinX(out backboardMin) || !DetailsMinX(out detailsMin))
        {
            return false;
        }

        x = (backboardMin + detailsMin) * 0.5f;
        return true;
    }

    /// <summary>Left edge world X of <c>Window/Shop_Backboard</c>, from its renderer bounds.</summary>
    private static bool BackboardMinX(out float min)
    {
        min = 0f;
        if (_backboard == null)
        {
            return false;
        }

        Renderer? renderer = _backboard.GetComponent<Renderer>() ??
            _backboard.GetComponentInChildren<Renderer>(true);
        if (renderer == null || renderer.bounds.size.x <= 0.0001f)
        {
            return false;
        }

        min = renderer.bounds.min.x;
        return IsSane(min);
    }

    /// <summary>
    /// Left edge world X of <c>Item List Group/Item Details</c>: rect corners first, then
    /// its renderers, then its position.
    /// </summary>
    private static bool DetailsMinX(out float min)
    {
        min = 0f;
        if (_details == null)
        {
            return false;
        }

        if (_details is RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            float lo = Mathf.Min(corners[0].x, corners[2].x);
            float hi = Mathf.Max(corners[0].x, corners[2].x);
            if (hi - lo > 0.0001f)
            {
                min = lo;
                return IsSane(min);
            }
        }

        Renderer? renderer = _details.GetComponent<Renderer>();
        if (renderer == null || renderer.bounds.size.x <= 0.0001f)
        {
            renderer = null;
            foreach (Renderer child in _details.GetComponentsInChildren<Renderer>(true))
            {
                if (child != null && child.enabled && child.gameObject.activeInHierarchy)
                {
                    renderer = child;
                    break;
                }
            }
        }

        min = renderer != null ? renderer.bounds.min.x : _details.position.x;
        return IsSane(min);
    }

    /// <summary>Left edge of a panel as text, for the candidate log.</summary>
    private static string LeftText(Transform? target)
    {
        return LeftEdgeX(target, null, out float min) ? min.ToString("F3") : "n/a";
    }

    /// <summary>Column centre as text, for the candidate log.</summary>
    private static string ColumnText(Transform? exclude)
    {
        return ColumnX(exclude, out float x) ? x.ToString("F3") : "n/a";
    }

    /// <summary>Details-to-window centre as text, for the candidate log.</summary>
    private static string ColumnCentreText(Transform? exclude)
    {
        return ColumnCentreX(exclude, out float x) ? x.ToString("F3") : "n/a";
    }

    /// <summary>Vanilla world X of a label, for the candidate log.</summary>
    private static string OriginalText(Transform label)
    {
        return OriginalX(label, out float x) ? x.ToString("F3") : "n/a";
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

    /// <summary>Resolves the list, the list group, the window, its backboard and the selection bar once per session.</summary>
    private static void ResolveTargets()
    {
        if (_root == null || _list != null)
        {
            return;
        }

        _listGroup = _root.Find("Item List Group") ?? FindByName(_root, "Item List Group");
        _list = _root.Find(ListPath) ?? FindByName(_root, "Item List");
        _details = _root.Find(DetailsPath) ?? FindByName(_root, "Item Details");
        _window = _root.Find("Window") ?? FindByName(_root, "Window");
        _backboard = _root.Find("Window/Shop_Backboard") ?? FindByName(_root, "Shop_Backboard");
        _selector = FindSelector();
    }

    /// <summary>
    /// Selection bar of the list: a <c>selector</c> child of the list group, or of the window
    /// root when the group itself has none. Matched case-insensitively, since the object is
    /// named <c>selector</c> in this build.
    /// </summary>
    /// <returns>The bar, or null when the window does not have one.</returns>
    private static Transform? FindSelector()
    {
        return FindChildNamed(_listGroup, "selector") ?? FindChildNamed(_root, "selector");
    }

    /// <summary>Direct or nested child of that exact name, ignoring letter case.</summary>
    /// <param name="root">Object to search under; null is a miss.</param>
    /// <param name="name">Wanted object name.</param>
    private static Transform? FindChildNamed(Transform? root, string name)
    {
        if (root == null)
        {
            return null;
        }

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child != null && string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
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

    /// <summary>Rejects NaN, infinity and absurd world X values from a half-built UI.</summary>
    private static bool IsSane(float x)
    {
        return Geometry.IsSane(x);
    }

    /// <summary>Row icon renderer, used by the details schema as a sorting reference.</summary>
    /// <param name="row">Row view of one shop entry.</param>
    internal static SpriteRenderer? RowIcon(ShopItemStats? row)
    {
        return row != null ? ItemSpriteField?.GetValue(row) as SpriteRenderer : null;
    }

    /// <summary>
    /// Writes the crest name into our own fixed-size label on the row; the cost icons and the
    /// vanilla <c>Item cost</c> label are already hidden.
    /// </summary>
    private static void ReplacePrice(ShopItemStats row, string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (!(CostTextField?.GetValue(row) is TMP_Text label) || label == null)
        {
            return;
        }

        string text = DrawnLabel(row, label, name!);
        if (EnsureOwnLabel(row, label, text) == null)
        {
            LogMissingLabel(row, label);
        }
    }

    /// <summary>Reports a row whose label could not be built, once per row.</summary>
    /// <param name="row">Row that has no label.</param>
    /// <param name="vanilla">Vanilla label of that row.</param>
    private static void LogMissingLabel(ShopItemStats row, TMP_Text vanilla)
    {
        int index = RowIndex(row, vanilla);
        if (!MissingLabelRows.Add(index))
        {
            return;
        }

        Plugin.Log.LogWarning($"[SHOPFX] row label missing row={index} reason={_labelFailure}");
    }

    /// <summary>Text the row label should draw: the crest name, or the ASCII probe.</summary>
    /// <param name="row">Row the label belongs to.</param>
    /// <param name="vanilla">Vanilla <c>Item cost</c> label of that row.</param>
    /// <param name="name">Crest name.</param>
    /// <returns>Text to write into the label.</returns>
    private static string DrawnLabel(ShopItemStats row, TMP_Text vanilla, string name)
    {
        return DebugAsciiRowLabel ? "AAAA " + RowIndex(row, vanilla) : name;
    }

    /// <summary>Our own label of that row, else the vanilla label itself.</summary>
    private static TMP_Text OwnLabelFor(TMP_Text vanilla)
    {
        return FallbackLabels.TryGetValue(vanilla, out TMP_Text own) && own != null ? own : vanilla;
    }

    /// <summary>True when the row name is drawn by our own label.</summary>
    private static bool UsesOwnLabel
    {
        get { return string.Equals(RowLabelMode, "Own", StringComparison.OrdinalIgnoreCase); }
    }

    /// <summary>True when no row label is drawn at all and the vanilla cost label stays as is.</summary>
    private static bool LabelOff
    {
        get { return string.Equals(RowLabelMode, "Off", StringComparison.OrdinalIgnoreCase); }
    }

    /// <summary>
    /// Creates our own row name label once per row and reuses it afterwards. The vanilla
    /// <c>Item cost</c> label draws nothing for Cyrillic (its root mesh is empty), so it is
    /// switched off for the session and the name goes into a label of our own, cloned from
    /// a TMP text of <c>Item Details</c> that is actually drawn on screen.
    /// </summary>
    /// <param name="row">Row the label belongs to.</param>
    /// <param name="vanilla">Vanilla <c>Item cost</c> label of that row.</param>
    /// <param name="name">Crest name to draw.</param>
    /// <returns>Our label, or null when it could not be built.</returns>
    private static TMP_Text? EnsureOwnLabel(ShopItemStats row, TMP_Text vanilla, string name)
    {
        if (FallbackLabels.TryGetValue(vanilla, out TMP_Text existing) && existing != null)
        {
            if (existing.text != name)
            {
                existing.text = name;
            }

            if (vanilla.enabled)
            {
                vanilla.enabled = false;
            }

            return existing;
        }

        FallbackLabels.Remove(vanilla);
        if (LabelGivenUp.Contains(vanilla))
        {
            return null;
        }

        int attempt = LabelAttempts.TryGetValue(vanilla, out int made) ? made + 1 : 1;
        if (attempt > MaxLabelAttempts)
        {
            LabelAttempts.Remove(vanilla);
            LabelGivenUp.Add(vanilla);
            _labelFailure = $"gave up after {MaxLabelAttempts} attempts";
            Plugin.Log.LogWarning($"[SHOPFX] row label giving up after {MaxLabelAttempts} attempts"
                + $" row={RowIndex(row, vanilla)}");
            return null;
        }

        LabelAttempts[vanilla] = attempt;

        TMP_Text? source = VisibleLabelSource();
        if (source == null || source.font == null || source.fontSharedMaterial == null)
        {
            _labelFailure = "no visible TMP text in 'Item Details'";
            if (attempt == 1)
            {
                Plugin.Log.LogWarning(
                    "[SHOPFX] row label failed step=resolve source: no visible TMP text in 'Item Details' yet");
            }

            return null;
        }

        TMP_FontAsset font = source.font;
        SpriteRenderer? icon = RowIcon(row);
        GameObject go;
        TextMeshPro? text;
        if (!CloneSourceLabel(source, vanilla, icon != null ? icon.gameObject.layer : vanilla.gameObject.layer,
                out go, out text))
        {
            // Last resort: a TMP built from scratch. Its font usually cannot be set, so this
            // path is only here to keep the row from losing its name entirely.
            go = new GameObject(OwnLabelName, typeof(RectTransform));
            go.layer = icon != null ? icon.gameObject.layer : vanilla.gameObject.layer;
            go.transform.SetParent(vanilla.transform.parent, false);
            if (!Step("add component", go, attempt, () => text = go.AddComponent<TextMeshPro>()))
            {
                return null;
            }
        }

        if (!Step("alive", go, attempt, () => RequireLive(text))
            || !Step("assign font", go, attempt, () => text!.font = font)
            || !Step("set material", go, attempt, () => text!.fontSharedMaterial = source.fontSharedMaterial)
            || !Step("style", go, attempt, () => StyleOwnLabel(text!, vanilla))
            || !Step("text", go, attempt, () => text!.SetText(name))
            || !Step("rect", go, attempt, () => CopyLabelRect(vanilla, text!.rectTransform))
            || !Step("sorting", go, attempt, () => CopyRowSorting(icon, go.GetComponent<MeshRenderer>()))
            || !Step("mesh", go, attempt, () => ForceOwnMesh(text!)))
        {
            return null;
        }

        LabelAttempts.Remove(vanilla);
        FallbackLabels[vanilla] = text!;
        HiddenLabels.Add(vanilla);
        vanilla.enabled = false;

        if (!_sourceDiagLogged)
        {
            _sourceDiagLogged = true;
            Plugin.Log.LogInfo($"[SHOPFX] row label source path='{PathOf(source.transform)}'"
                + $" font='{font.name}' verts={VertexCount(source)}");
        }

        Plugin.Log.LogInfo($"[SHOPFX] own row label row={RowIndex(row, vanilla)}"
            + $" attempts={attempt} font={font.name}");

        LogLabelDiagnostics(text!);
        LogInvisibleCheck(text!);
        return text;
    }

    /// <summary>
    /// Clones an already visible TMP text of the details panel. A TMP created at runtime
    /// cannot take a font, while a clone keeps a working font, material, shader and mesh
    /// setup; the rect is then copied from the vanilla row label.
    /// </summary>
    /// <param name="source">Visible details text that becomes the label.</param>
    /// <param name="vanilla">Vanilla <c>Item cost</c> label of the row.</param>
    /// <param name="layer">Layer of the row icon.</param>
    /// <param name="go">Clone object, null when the clone was not made.</param>
    /// <param name="text">Clone label, null when the clone was not made.</param>
    /// <returns>True when the clone is usable.</returns>
    private static bool CloneSourceLabel(TMP_Text source, TMP_Text vanilla, int layer,
        out GameObject go, out TextMeshPro? text)
    {
        go = null!;
        text = null;
        if (!(source is TextMeshPro) || vanilla.transform.parent == null)
        {
            return false;
        }

        GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, vanilla.transform.parent);
        clone.name = OwnLabelName;
        clone.layer = layer;
        TextMeshPro? copy = clone.GetComponent<TextMeshPro>();
        if (copy == null)
        {
            UnityEngine.Object.Destroy(clone);
            return false;
        }

        copy.transform.SetSiblingIndex(vanilla.transform.GetSiblingIndex() + 1);
        go = clone;
        text = copy;
        return true;
    }

    /// <summary>
    /// Picks the first drawn TMP text of the details panel to clone for the row labels.
    /// <c>Item desc</c> wins, then <c>Item name</c>, then any other drawn text. Every text
    /// of the panel is written to the log once, with its font, atlas and vertex count.
    /// </summary>
    /// <returns>Visible source text, or null while the panel still draws nothing.</returns>
    private static TMP_Text? VisibleLabelSource()
    {
        if (_labelSource != null)
        {
            return _labelSource;
        }

        try
        {
            ResolveTargets();
            Transform? details = _details;
            if (details == null)
            {
                return null;
            }

            TMP_Text? best = null;
            int bestRank = int.MaxValue;
            foreach (TMP_Text text in details.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == null)
                {
                    continue;
                }

                int verts = VertexCount(text);
                if (SourceLogged.Add(text))
                {
                    LogTmpText(text, verts);
                }

                if (!(text is TextMeshPro) || verts <= 0 || text.alpha <= 0f)
                {
                    continue;
                }

                int rank = text.name == "Item desc" ? 0 : (text.name == "Item name" ? 1 : 2);
                if (rank < bestRank)
                {
                    bestRank = rank;
                    best = text;
                }
            }

            if (best != null)
            {
                _labelSource = best;
            }

            return best;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPFX] label source lookup failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Writes one details text with its font, atlas and vertex count.</summary>
    /// <param name="text">Text to describe.</param>
    /// <param name="verts">Vertex count of the text.</param>
    private static void LogTmpText(TMP_Text text, int verts)
    {
        TMP_FontAsset? font = text.font;
        Material? mat = text.fontSharedMaterial;
        Renderer? renderer = text.GetComponent<Renderer>();
        if (mat == null && renderer != null)
        {
            mat = renderer.sharedMaterial;
        }

        Plugin.Log.LogInfo($"[SHOPFX] tmptext path='{PathOf(text.transform)}'"
            + $" font='{(font != null ? font.name : "none")}'"
            + $" mat='{(mat != null ? mat.name : "none")}'"
            + $" verts={verts} alpha={text.alpha:0.##}"
            + $" atlas={(font != null && font.atlas != null ? font.atlas.width + "x" + font.atlas.height : "none")}"
            + $" mode={(font != null ? font.fontAssetType.ToString() : "none")}");
    }

    /// <summary>Hierarchy path of a transform below the shop window root.</summary>
    /// <param name="target">Transform to describe.</param>
    private static string PathOf(Transform target)
    {
        List<string> parts = new List<string>();
        Transform? node = target;
        while (node != null && node != _root)
        {
            parts.Add(node.name);
            node = node.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }

    /// <summary>
    /// Logs once why our label might still be invisible: world bounds, depth, camera mask,
    /// layer and sorting. One line, no further guessing.
    /// </summary>
    /// <param name="text">Our label that was just built.</param>
    private static void LogInvisibleCheck(TMP_Text text)
    {
        if (_invisibleLogged)
        {
            return;
        }

        try
        {
            _invisibleLogged = true;
            Renderer? renderer = text.GetComponent<Renderer>();
            Vector3 size = renderer != null ? renderer.bounds.size : Vector3.zero;
            Camera? camera = Camera.main;
            int layer = text.gameObject.layer;
            int mask = camera != null ? camera.cullingMask : 0;
            int order = renderer != null ? renderer.sortingOrder : 0;
            string sort = renderer != null && !string.IsNullOrEmpty(renderer.sortingLayerName)
                ? renderer.sortingLayerName
                : "none";

            Plugin.Log.LogInfo($"[SHOPFX] row label invisible check"
                + $" bounds=({size.x:0.##},{size.y:0.##},{size.z:0.##})"
                + $" worldZ={text.transform.position.z:0.##}"
                + $" camMask={(mask & (1 << layer)) != 0}"
                + $" layer={layer} order={order} sort={sort}"
                + $" verts={VertexCount(text)}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPFX] invisible check failed: {ex.Message}");
        }
    }

    /// <summary>Logs render state of the first own label and of <c>Item Details/Item name</c>.</summary>
    /// <param name="label">Own label that was just built.</param>
    private static void LogLabelDiagnostics(TMP_Text label)
    {
        if (_labelDiagLogged)
        {
            return;
        }

        try
        {
            _labelDiagLogged = true;
            List<KeyValuePair<string, string>> own = LabelFields(label);
            Plugin.Log.LogInfo("[SHOPFX] row label diag " + JoinFields(own));

            TMP_Text? reference = ItemNameLabel();
            if (reference == null)
            {
                Plugin.Log.LogWarning("[SHOPFX] item name diag: 'Item Details/Item name' not found");
                return;
            }

            List<KeyValuePair<string, string>> item = LabelFields(reference);
            Plugin.Log.LogInfo("[SHOPFX] item name diag " + JoinFields(item));

            List<string> diffs = new List<string>();
            for (int i = 0; i < own.Count && i < item.Count; i++)
            {
                if (own[i].Value != item[i].Value)
                {
                    diffs.Add($"{own[i].Key}(own={own[i].Value}, item={item[i].Value})");
                }
            }

            Plugin.Log.LogInfo("[SHOPFX] label vs item name: "
                + (diffs.Count == 0 ? "no differences" : string.Join(", ", diffs)));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPFX] label diag failed: {ex.Message}");
        }
    }

    /// <summary>Renders state values of a label as ordered name/value pairs.</summary>
    /// <param name="text">Label to inspect.</param>
    private static List<KeyValuePair<string, string>> LabelFields(TMP_Text text)
    {
        Renderer? renderer = text.GetComponent<Renderer>();
        Material? mat = renderer != null ? renderer.sharedMaterial : null;
        Shader? shader = mat != null ? mat.shader : null;
        Color face = mat != null && mat.HasProperty("_FaceColor") ? mat.GetColor("_FaceColor") : Color.clear;
        float ztest = mat != null && mat.HasProperty("_ZTest") ? mat.GetFloat("_ZTest") : -1f;
        int queue = mat != null ? mat.renderQueue : -1;
        Vector3 bounds = renderer != null ? renderer.bounds.size : Vector3.zero;
        string sort = renderer != null && !string.IsNullOrEmpty(renderer.sortingLayerName)
            ? renderer.sortingLayerName
            : (renderer != null ? renderer.sortingLayerID.ToString() : "none");

        return new List<KeyValuePair<string, string>>
        {
            Field("layer", text.gameObject.layer.ToString()),
            Field("sort", sort),
            Field("order", renderer != null ? renderer.sortingOrder.ToString() : "none"),
            Field("mat", mat != null ? "'" + mat.name + "'" : "'none'"),
            Field("shader", shader != null ? "'" + shader.name + "'" : "'none'"),
            Field("queue", queue.ToString()),
            Field("ztest", ztest.ToString("0.##")),
            Field("faceColor", $"({face.r:0.##},{face.g:0.##},{face.b:0.##},{face.a:0.##})"),
            Field("textColor", $"({text.color.r:0.##},{text.color.g:0.##},{text.color.b:0.##},{text.color.a:0.##})"),
            Field("alpha", text.alpha.ToString("0.##")),
            Field("verts", VertexCount(text).ToString()),
            Field("bounds", $"({bounds.x:0.##},{bounds.y:0.##},{bounds.z:0.##})"),
        };
    }

    /// <summary>Vertices of the first sub mesh of a label, zero when the mesh is empty.</summary>
    /// <param name="text">Label to inspect.</param>
    private static int VertexCount(TMP_Text text)
    {
        TMP_TextInfo? info = text.textInfo;
        if (info == null || info.meshInfo == null || info.meshInfo.Length == 0)
        {
            return 0;
        }

        return info.meshInfo[0].vertexCount;
    }

    /// <summary>One diagnostic field.</summary>
    /// <param name="name">Field name.</param>
    /// <param name="value">Field value.</param>
    private static KeyValuePair<string, string> Field(string name, string value)
    {
        return new KeyValuePair<string, string>(name, value);
    }

    /// <summary>Joins diagnostic fields into one log line.</summary>
    /// <param name="fields">Fields to join.</param>
    private static string JoinFields(List<KeyValuePair<string, string>> fields)
    {
        List<string> parts = new List<string>(fields.Count);
        foreach (KeyValuePair<string, string> pair in fields)
        {
            parts.Add($"{pair.Key}={pair.Value}");
        }

        return string.Join(" ", parts);
    }

    /// <summary>The game's own Cyrillic label under <c>Item Details</c>, used as the reference.</summary>
    private static TMP_Text? ItemNameLabel()
    {
        return DetailsText("Item name");
    }

    /// <summary>Details panel text with this object name, inactive ones included.</summary>
    /// <param name="name">Object name of the TMP text.</param>
    /// <returns>The text, or null when the panel or the object is missing.</returns>
    private static TMP_Text? DetailsText(string name)
    {
        try
        {
            ResolveTargets();
            Transform? details = _details;
            if (details == null && _root != null)
            {
                _details = _root.Find(DetailsPath) ?? FindByName(_root, "Item Details");
                details = _details;
            }

            if (details != null)
            {
                foreach (TMP_Text text in details.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text != null && text.name == name)
                    {
                        return text;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPFX] details text '{name}' lookup failed: {ex.Message}");
        }

        return null;
    }

    /// <summary><c>Item desc</c> of the details panel, the reference label of the post-open dump.</summary>
    /// <returns>The description text, or null when it is missing.</returns>
    internal static TMP_Text? ItemDescLabel()
    {
        return DetailsText("Item desc");
    }

    /// <summary>First own row label still alive, the subject of the post-open dump.</summary>
    /// <returns>Our label, or null when no row label was built.</returns>
    internal static TMP_Text? OwnRowLabelProbe()
    {
        foreach (KeyValuePair<TMP_Text, TMP_Text> pair in FallbackLabels)
        {
            if (pair.Value != null)
            {
                return pair.Value;
            }
        }

        return null;
    }

    /// <summary>Throws unless the component survived <c>AddComponent</c>.</summary>
    /// <param name="text">Component under construction.</param>
    private static void RequireLive(TMP_Text? text)
    {
        if (text == null || text.gameObject == null)
        {
            throw new InvalidOperationException("component died right after AddComponent");
        }
    }

    /// <summary>Position of the row in the shop list, used by the logs.</summary>
    /// <param name="row">Row the label belongs to.</param>
    /// <param name="vanilla">Vanilla label of that row.</param>
    private static int RowIndex(ShopItemStats row, TMP_Text vanilla)
    {
        return row != null ? row.transform.GetSiblingIndex() : vanilla.transform.GetSiblingIndex();
    }

    /// <summary>Logs the total of ready labels when it changes.</summary>
    /// <param name="rows">Rows seen in this pass.</param>
    private static void ReportOwnLabels(int rows)
    {
        int ready = FallbackLabels.Count;
        if (ready == _readyLogged)
        {
            return;
        }

        _readyLogged = ready;
        Plugin.Log.LogInfo($"[SHOPFX] own row labels ready={ready}/{rows}");
    }

    /// <summary>Runs one build step, naming the step in the log and dropping the object on failure.</summary>
    /// <param name="step">Step name shown in the log.</param>
    /// <param name="go">Object under construction.</param>
    /// <param name="attempt">Attempt number, only the first one is logged.</param>
    /// <param name="action">Work of that step.</param>
    /// <returns>True when the step succeeded.</returns>
    private static bool Step(string step, GameObject go, int attempt, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            _labelFailure = $"step {step}: {ex.Message}";
            if (attempt <= 1)
            {
                Plugin.Log.LogWarning($"[SHOPFX] row label failed step={step}: {ex.Message}");
            }

            if (go != null)
            {
                UnityEngine.Object.Destroy(go);
            }

            return false;
        }
    }

    /// <summary>
    /// Text settings of our own row label: plain white, no wrap, no auto-size, and the exact
    /// font size of the vanilla <c>Item cost</c> label — no shrink factor, no fitter.
    /// </summary>
    private static void StyleOwnLabel(TMP_Text text, TMP_Text vanilla)
    {
        float size = vanilla.fontSize;
        text.color = new Color(1f, 1f, 1f, 1f);
        text.enableAutoSizing = false;
        text.enableWordWrapping = false;
        text.OverflowMode = TextOverflowModes.Overflow;
        text.fontSize = size;
        text.fontSizeMin = size;
        text.fontSizeMax = size;
        text.alignment = TextAlignmentOptions.Center;
    }

    /// <summary>Copies the vanilla label rect so our label sits exactly on the price spot.</summary>
    private static void CopyLabelRect(TMP_Text vanilla, RectTransform rect)
    {
        if (!(vanilla.transform is RectTransform source))
        {
            throw new InvalidOperationException("vanilla label has no RectTransform");
        }

        rect.anchorMin = source.anchorMin;
        rect.anchorMax = source.anchorMax;
        rect.sizeDelta = source.sizeDelta;
        rect.pivot = source.pivot;
        rect.anchoredPosition = source.anchoredPosition;
        rect.localScale = source.localScale;
        rect.localRotation = source.localRotation;
    }

    /// <summary>Sorting of the row icon, raised by one so the name draws over the icon.</summary>
    private static void CopyRowSorting(SpriteRenderer? icon, MeshRenderer? renderer)
    {
        if (icon == null || renderer == null)
        {
            return;
        }

        renderer.sortingLayerID = icon.sortingLayerID;
        renderer.sortingOrder = icon.sortingOrder + 1;
    }

    /// <summary>Builds the mesh and rejects an empty one.</summary>
    private static void ForceOwnMesh(TMP_Text text)
    {
        text.ForceMeshUpdate();
        Renderer? renderer = text.GetComponent<Renderer>();
        if (renderer == null || renderer.bounds.size.x <= 0.0001f)
        {
            throw new InvalidOperationException("own label drew an empty mesh");
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

    /// <summary>Switches one vanilla text off; <see cref="End"/> switches it back on.</summary>
    private static void HideLabel(TMP_Text? label)
    {
        if (label == null || !label.enabled)
        {
            return;
        }

        HiddenLabels.Add(label);
        label.enabled = false;
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
            if (state.Font != null)
            {
                label.font = state.Font;
            }

            if (state.Material != null)
            {
                label.fontSharedMaterial = state.Material;
            }

            label.text = state.Text;
            label.enableWordWrapping = state.WordWrap;
            label.alignment = state.Alignment;
            label.fontSizeMin = state.FontSizeMin;
            label.fontSizeMax = state.FontSizeMax;
            label.fontSize = state.FontSize;
            label.enableAutoSizing = state.AutoSize;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPFX] label restore failed on '{label.name}': {ex.Message}");
        }
    }

    /// <summary>Cost label of one shop row, used by the row diagnostics log.</summary>
    /// <param name="row">Row view of one shop entry.</param>
    internal static TMP_Text? RowLabel(ShopItemStats? row)
    {
        return row != null ? CostTextField?.GetValue(row) as TMP_Text : null;
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
    /// Remembers the vanilla confirm button label once per session, unconditionally:
    /// after a previous session the label already reads our own text, and skipping the
    /// capture then would keep our text as the "original" forever.
    /// </summary>
    private static void CaptureConfirmButton(Transform root)
    {
        _confirmButton = FindConfirmButton(root);
        if (_confirmButton == null)
        {
            if (!_missingLogged)
            {
                _missingLogged = true;
                Plugin.Log.LogWarning("[SHOPFX] 'Action Confirm/Text' not found.");
            }

            _confirmButtonOriginal = null;
            return;
        }

        _confirmButtonOriginal = _confirmButton.text;
        Plugin.Log.LogInfo($"[SHOPFX] confirm label capture='{_confirmButtonOriginal}'" +
            $" -> '{ConfirmButtonText}'");
    }

    /// <summary>Overwrites the confirmation button label; runs after the FSM's own write.</summary>
    private static void ApplyConfirmButton()
    {
        if (string.IsNullOrEmpty(ConfirmButtonText) || _confirmButton == null)
        {
            return;
        }

        if (_confirmButton.text != ConfirmButtonText)
        {
            _confirmButton.text = ConfirmButtonText;
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
    /// True while the vanilla purchase confirmation ("Знак получен") is on screen.
    /// The label object exists but is inactive until the purchase completes.
    /// </summary>
    internal static bool PurchaseMessageShown =>
        _purchaseLabel != null && _purchaseLabel.gameObject.activeInHierarchy;

    /// <summary>
    /// Remembers the purchase confirmation label, so its vanilla text can be written back
    /// on restore. The object exists but is inactive until the purchase completes.
    /// </summary>
    private static void CapturePurchaseText(Transform root)
    {
        _purchaseLabel = FindPurchaseLabel(root);
        if (_purchaseLabel == null)
        {
            Plugin.Log.LogWarning("[SHOPFX] 'Item Confirm Group/Thankyou' not found.");
        }
    }

    /// <summary>
    /// Writes our purchase line while the confirmation is on screen. The FSM owns that
    /// text, so this runs after it in <see cref="LateTick"/>; the vanilla string is
    /// captured before the first overwrite and written back by <see cref="End"/>.
    /// </summary>
    private static void ApplyPurchaseText()
    {
        if (_purchaseLabel == null || string.IsNullOrEmpty(PurchaseText))
        {
            return;
        }

        if (!ShopCrestMenu.IsOpen || !_purchaseLabel.gameObject.activeInHierarchy)
        {
            return;
        }

        string current = _purchaseLabel.text ?? string.Empty;
        if (_purchaseOriginal == null && current != PurchaseText)
        {
            _purchaseOriginal = current;
            Plugin.Log.LogInfo($"[SHOPFX] purchase text capture='{_purchaseOriginal}' -> '{PurchaseText}'");
        }

        if (current != PurchaseText)
        {
            _purchaseLabel.text = PurchaseText;
        }
    }

    /// <summary>Locates the purchase confirmation label by path, then by name.</summary>
    private static TMP_Text? FindPurchaseLabel(Transform root)
    {
        Transform? direct = root.Find("Item Confirm Group/Thankyou");
        TMP_Text? found = direct != null ? direct.GetComponent<TMP_Text>() : null;
        if (found != null)
        {
            return found;
        }

        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text != null && text.name == "Thankyou")
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
            float fontSize, float fontSizeMin, float fontSizeMax, TextAlignmentOptions alignment,
            TMP_FontAsset? font, Material? material)
        {
            Text = text;
            WordWrap = wordWrap;
            AutoSize = autoSize;
            FontSize = fontSize;
            FontSizeMin = fontSizeMin;
            FontSizeMax = fontSizeMax;
            Alignment = alignment;
            Font = font;
            Material = material;
        }

        internal string Text { get; }

        internal bool WordWrap { get; }

        internal bool AutoSize { get; }

        internal float FontSize { get; }

        internal float FontSizeMin { get; }

        internal float FontSizeMax { get; }

        internal TextAlignmentOptions Alignment { get; }

        internal TMP_FontAsset? Font { get; }

        internal Material? Material { get; }

        internal static TextState Capture(TMP_Text label)
        {
            return new TextState(label.text, label.enableWordWrapping, label.enableAutoSizing,
                label.fontSize, label.fontSizeMin, label.fontSizeMax, label.alignment,
                label.font, label.fontSharedMaterial);
        }
    }
}
