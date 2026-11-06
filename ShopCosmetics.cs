using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
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
    private static TMP_Text? _confirm;
    private static string? _confirmOriginal;
    private static TMP_Text? _confirmButton;
    private static string? _confirmButtonOriginal;
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
    internal static ConfigEntry<string>? CentreSource;

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
            _details = null;
            _window = null;
            _backboard = null;
            _frames = 0;
            _missingLogged = false;
            _candidatesLogged = false;
            _sourceWarned = false;
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
        _details = null;
        _window = null;
        _backboard = null;
        _candidatesLogged = false;
        _sourceWarned = false;
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
            BaselineCentres.Clear();
            BaselinePositions.Clear();
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
    /// the mesh bounds centre, not the rect, whose size lags behind the fitter. Both the
    /// target and each label's baseline are measured once per shop session: the source
    /// geometry grows with the shifted text, so re-measuring would feed the shift back
    /// into the target and the labels would drift.
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
        string? name = CentreSource?.Value;
        switch (name)
        {
            case "columns":
            case "rows":
            case "list":
            case "column":
            case "columnCentre":
            case "listGroup":
            case "backboard":
            case "window":
            case "original":
                return name;
            default:
                if (!_sourceWarned)
                {
                    _sourceWarned = true;
                    Plugin.Log.LogWarning($"[SHOPFX] unknown TextCentreSource '{name}', using 'columns'.");
                }

                return "columns";
        }
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

    /// <summary>Resolves the list, the list group, the window and its backboard once per session.</summary>
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
        return !float.IsNaN(x) && !float.IsInfinity(x) && Mathf.Abs(x) < 1000f;
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
