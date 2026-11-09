using System;
using System.Collections.Generic;
using System.Reflection;
using CrestPicker.Pure;
using GlobalSettings;
using HarmonyLib;
using HutongGames.PlayMaker;
using TeamCherry.NestedFadeGroup;
using TMProOld;
using UnityEngine;

namespace CrestPicker;

/// <summary>How <see cref="ShopCrestSchema"/> draws the crest body.</summary>
internal enum SchemaRenderMode
{
    /// <summary>Instantiate the live vanilla crest card of the selected crest.</summary>
    Clone,

    /// <summary>Draw the silhouette and the vanilla <c>templateSlots</c> icons.</summary>
    VanillaSprites,

    /// <summary>Draw the silhouette and generated circle dots.</summary>
    Generated,
}

/// <summary>
/// Cosmetic pass over the shop details panel while our crests are shown: the vanilla
/// name and description labels are kept, and a crest schema takes the place of the item
/// picture - the monochrome silhouette plus one coloured dot per slot. Every created
/// object is destroyed and every hidden object restored by <see cref="End"/>.
/// Never throws.
/// </summary>
internal static class ShopCrestSchema
{
    /// <summary>Schema height in world units before <see cref="Scale"/> is applied.</summary>
    private const float BaseHeight = 1.2f;

    /// <summary>Fraction of the details panel the details schema fills.</summary>
    private const float PanelFill = 0.8f;

    /// <summary>Dot diameter factor for a locked slot.</summary>
    private const float LockedDotFactor = 0.85f;

    /// <summary>Dot alpha for a locked slot.</summary>
    private const float LockedAlpha = 0.45f;

    /// <summary>Dot fits tried per frame before the next frame takes over.</summary>
    private const int MaxDotAttempts = 3;

    /// <summary>Relative error a dot fit accepts as done.</summary>
    private const float DotTolerance = 0.05f;

    /// <summary>Path of the details panel inside the shop window.</summary>
    private const string DetailsPath = "Item List Group/Item Details";

    /// <summary>Confirm group whose vanilla item picture the confirm schema replaces.</summary>
    private const string ConfirmGroupPath = "Item Confirm Group";

    /// <summary>Sorting orders the confirm schema is lifted above the backboard by.</summary>
    private const int ConfirmOrderOffset = 2;

    /// <summary>Silhouette renderer inside the vanilla crest card the clone is fitted by.</summary>
    private const string SilhouetteName = "crestSilhouette";

    /// <summary>Fallback colours when the game's tool type palette is unavailable.</summary>
    private static readonly Dictionary<ToolItemType, Color> FallbackColors =
        new Dictionary<ToolItemType, Color>
        {
            [ToolItemType.Red] = new Color(1f, 0.42f, 0.42f, 1f),
            [ToolItemType.Blue] = new Color(0.36f, 0.82f, 1f, 1f),
            [ToolItemType.Yellow] = new Color(1f, 0.82f, 0.4f, 1f),
            [ToolItemType.Skill] = new Color(0.88f, 0.88f, 0.88f, 1f),
        };

    /// <summary>Vanilla active state of every object we hid.</summary>
    private static readonly Dictionary<GameObject, bool> ObjectStates = new Dictionary<GameObject, bool>();

    /// <summary>Neutral holder of both schema containers, parented outside every fade group.</summary>
    private const string SchemaRootName = "CrestPicker Schemas";

    /// <summary>Child of a container that carries the fit scale; the container stays at scale 1.</summary>
    private const string ContentName = "Content";

    private static Transform? _root;
    private static Transform? _schemaRoot;
    private static Transform? _details;
    private static TMP_Text? _nameLabel;
    private static TMP_Text? _descLabel;
    private static Transform? _confirmGroup;
    private static TMP_Text? _confirmMessage;

    /// <summary>Schema objects created for the current crest; destroyed on rebuild.</summary>
    private static Schema? _detailsSchema;
    private static Schema? _confirmSchema;
    private static ToolCrest? _builtFor;
    private static Sprite? _dotSprite;
    private static bool _missingLogged;
    private static bool _confirmOrderLogged;

    /// <summary>Last <c>activeSelf</c> of the confirm group that was logged; null before the first log.</summary>
    private static bool? _confirmGroupActiveLogged;

    /// <summary>Bounds the details schema drew on the last frame it was visible.</summary>
    private static Bounds _detailsBounds;

    /// <summary>True once the details schema was hidden behind the confirm screen.</summary>
    private static bool _detailsHiddenLogged;

    /// <summary>True from the close start until the next <see cref="Begin"/>; blocks any re-show.</summary>
    private static bool _closed;

    /// <summary>Row index the details FSM last asked <c>ShopMenuStock.GetName</c> about; -1 before.</summary>
    private static int _selectedIndex = -1;

    /// <summary>How the current crest was picked, so the log line is written only on a change.</summary>
    private static string _pickSource = string.Empty;
    private static int _pickIndex = -1;
    private static string _pickName = string.Empty;

    /// <summary>True once the first details text correction has been logged for this session.</summary>
    private static bool _detailsCorrectedLogged;

    /// <summary>True once the close-start alpha was logged for this session.</summary>
    private static bool _closeAlphaLogged;

    /// <summary>Flags for the private fields read off the vanilla crest widgets.</summary>
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly FieldInfo? TemplateSlotsField =
        typeof(InventoryToolCrest).GetField("templateSlots", PrivateInstance);

    private static readonly FieldInfo? SlotTypeSpriteField =
        typeof(InventoryToolCrestSlot).GetField("slotTypeSprite", PrivateInstance);

    /// <summary>Vanilla slot sprites indexed by <see cref="ToolItemType"/>; read once per session.</summary>
    private static Sprite?[]? _templateSprites;

    private static bool _templateRead;
    private static bool _inventoryFound;
    private static bool _spritesLogged;
    private static readonly HashSet<string> LoggedReasons = new HashSet<string>();

    /// <summary>True to draw the crest schema at all.</summary>
    internal static bool Enabled = true;

    /// <summary>How the schema body is drawn; clone keeps the vanilla crest card.</summary>
    internal static SchemaRenderMode Render = SchemaRenderMode.Clone;

    /// <summary>Multiplier applied to the schema size.</summary>
    internal static float Scale = 1f;

    /// <summary>Multiplier for the slot dot diameter, as a fraction of the schema height.</summary>
    internal static float DotSize = 0.11f;

    /// <summary>Dot drop, as a fraction of the drawn silhouette height, in world units.</summary>
    internal static float DotOffsetY = -0.02f;

    /// <summary>Raw <c>CrestDotSize</c> value from the cfg, before the legacy migration.</summary>
    internal static float DotSizeRaw = 0.11f;

    /// <summary>Confirm-screen schema size relative to the details schema.</summary>
    internal static float ConfirmScale = 1f;

    /// <summary>Remembers the window, hides the duplicate name and draws the schema.</summary>
    /// <param name="root">Shop window root, the object holding <c>shop_control</c>.</param>
    internal static void Begin(Transform root)
    {
        try
        {
            _root = root;
            _details = null;
            _nameLabel = null;
            _descLabel = null;
            _confirmGroup = null;
            _confirmMessage = null;
            _builtFor = null;
            _missingLogged = false;
            _detailsHiddenLogged = false;
            _closed = false;
            _closeAlphaLogged = false;
            _selectedIndex = -1;
            _pickSource = string.Empty;
            _pickIndex = -1;
            _pickName = string.Empty;
            _detailsCorrectedLogged = false;
            _confirmGroupActiveLogged = null;
            _detailsBounds = default;
            DestroySchemaRoot();
            Apply();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SCHEMA] begin failed: {ex.Message}");
        }
    }

    /// <summary>Redraws the schema when the selected crest changed; cheap on a match.</summary>
    internal static void Tick()
    {
        if (_root == null)
        {
            return;
        }

        try
        {
            Apply();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SCHEMA] tick failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Hides both schema containers as soon as the window starts closing. They live
    /// outside the fade hierarchy, so the vanilla fade cannot reach them: without this
    /// they stay opaque while the window already goes dark.
    /// </summary>
    /// <param name="alpha">Window fade alpha at the close start; negative when unknown.</param>
    internal static void Hide(float alpha = -1f)
    {
        _closed = true;
        try
        {
            bool changed = false;
            changed |= HideContainer(_detailsSchema);
            changed |= HideContainer(_confirmSchema);
            if (!_closeAlphaLogged)
            {
                _closeAlphaLogged = true;
                Plugin.Log.LogInfo($"[SCHEMA] hidden at close start alpha={(alpha < 0f ? 1f : alpha):F3}");
            }
            else if (changed)
            {
                Plugin.Log.LogInfo("[SCHEMA] hidden on close");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SCHEMA] hide on close failed: {ex.Message}");
        }
    }

    /// <summary>Switches one schema container off; false when it was already off or absent.</summary>
    private static bool HideContainer(Schema? schema)
    {
        if (schema == null || schema.Container == null || !schema.Container.activeSelf)
        {
            return false;
        }

        schema.Container.SetActive(false);
        return true;
    }

    /// <summary>Destroys the schema objects and clears the restore records.</summary>
    internal static void End()
    {
        _closeAlphaLogged = false;
        _root = null;
        _details = null;
        _nameLabel = null;
        _descLabel = null;
        _confirmGroup = null;
        _confirmMessage = null;
        _builtFor = null;
        _detailsHiddenLogged = false;
        _confirmGroupActiveLogged = null;
        _detailsBounds = default;
        _selectedIndex = -1;
        _pickSource = string.Empty;
        _pickIndex = -1;
        _pickName = string.Empty;
        _detailsCorrectedLogged = false;
        try
        {
            DestroySchema();
            DestroySchemaRoot();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SCHEMA] destroy failed: {ex.Message}");
        }
        finally
        {
            ObjectStates.Clear();
            _closed = false;
        }
    }

    /// <summary>Keeps the schema in step with the selection; the details texts are left to the vanilla FSM.</summary>
    private static void Apply()
    {
        if (_closed)
        {
            return;
        }

        if (!ResolveDetails())
        {
            return;
        }

        if (!Enabled)
        {
            return;
        }

        ConfirmGroup();

        ToolCrest? crest = SelectedCrest();
        if (crest != null && crest != _builtFor)
        {
            Build(crest);
        }

        Place();
    }

    /// <summary>
    /// Puts the details schema in the middle of the lower half of the details panel and
    /// the confirm schema on the confirm screen picture. Runs every frame because the
    /// panel's layout group keeps moving its children.
    /// </summary>
    private static void Place()
    {
        LogConfirmGroupState();
        FitPending();

        if (_detailsSchema == null || _details == null || !TryBounds(_details, out Bounds bounds))
        {
            return;
        }

        float lowerCentreY = Mathf.Lerp(bounds.min.y, bounds.center.y, 0.5f);
        Vector3 target = new Vector3(bounds.center.x, lowerCentreY, bounds.center.z);
        if (_detailsSchema.Container.activeSelf)
        {
            PlaceAt(_detailsSchema, target);
            _detailsBounds = ContainerBounds(_detailsSchema.Container.transform);
        }

        if (_confirmSchema == null)
        {
            ShowDetails();
            return;
        }

        string reason = ConfirmHiddenReason();
        bool showing = reason.Length == 0;
        if (_confirmSchema.Container.activeSelf != showing)
        {
            _confirmSchema.Container.SetActive(showing);
            Plugin.Log.LogInfo(showing
                ? "[SCHEMA] confirm visible"
                : $"[SCHEMA] confirm hidden ({reason})");
        }

        if (showing)
        {
            HideDetails();
            PlaceConfirm(_confirmSchema);
        }
        else
        {
            ShowDetails();
        }
    }

    /// <summary>Logs the confirm group <c>activeSelf</c> once per change, with the current message text.</summary>
    private static void LogConfirmGroupState()
    {
        if (_confirmGroup == null)
        {
            return;
        }

        bool active = _confirmGroup.gameObject.activeSelf;
        if (_confirmGroupActiveLogged == active)
        {
            return;
        }

        _confirmGroupActiveLogged = active;
        string text = _confirmMessage != null ? SafeText(_confirmMessage) : string.Empty;
        Plugin.Log.LogInfo($"[SCHEMA] confirm group activeSelf={active} text='{text}'");
    }

    /// <summary>
    /// Hides our details schema while the confirm screen covers the panel: the confirm
    /// schema is drawn on the same spot, so both would overlap.
    /// </summary>
    private static void HideDetails()
    {
        if (_detailsSchema == null)
        {
            return;
        }

        GameObject go = _detailsSchema.Container;
        if (!ObjectStates.ContainsKey(go))
        {
            ObjectStates[go] = go.activeSelf;
        }

        if (!go.activeSelf)
        {
            return;
        }

        go.SetActive(false);
        if (!_detailsHiddenLogged)
        {
            _detailsHiddenLogged = true;
            Plugin.Log.LogInfo("[SCHEMA] details hidden while confirm visible");
        }
    }

    /// <summary>Puts our details schema back to the state it had before the confirm screen.</summary>
    private static void ShowDetails()
    {
        if (_detailsSchema == null)
        {
            return;
        }

        GameObject go = _detailsSchema.Container;
        if (ObjectStates.TryGetValue(go, out bool wasActive) && go.activeSelf != wasActive)
        {
            go.SetActive(wasActive);
        }
    }

    /// <summary>Bounds of the details schema; the last visible ones while it is hidden.</summary>
    private static Bounds DetailsSchemaBounds()
    {
        if (_detailsSchema != null && _detailsSchema.Container.activeSelf)
        {
            _detailsBounds = ContainerBounds(_detailsSchema.Container.transform);
        }

        return _detailsBounds;
    }

    /// <summary>Moves a schema in world space so its own drawn centre lands on <paramref name="target"/>.</summary>
    private static void PlaceAt(Schema schema, Vector3 target)
    {
        Transform container = schema.Container.transform;
        Bounds bounds = ContainerBounds(container);
        Vector3 shift = target - bounds.center;
        shift.z = 0f;
        if (shift.sqrMagnitude > 0.0000001f)
        {
            container.position += shift;
        }
    }

    /// <summary>
    /// Puts the confirm schema on the details schema, scaled by <see cref="ConfirmScale"/>,
    /// and keeps it inside the window backboard. The position comes from the drawn bounds
    /// of the container itself, so no parent scale chain is assumed.
    /// </summary>
    private static void PlaceConfirm(Schema schema)
    {
        if (_detailsSchema == null)
        {
            return;
        }

        Transform container = schema.Container.transform;
        Bounds detailsBounds = DetailsSchemaBounds();
        float wanted = detailsBounds.size.y * ConfirmScale;
        float current = ContainerBounds(container).size.y;
        Transform content = schema.Content.transform;
        if (current > 0.0001f && wanted > 0.0001f && Mathf.Abs(current - wanted) > wanted * 0.001f)
        {
            float factor = wanted / current;
            Vector3 local = content.localScale;
            content.localScale = new Vector3(local.x * factor, local.y * factor, local.z);
        }

        PlaceAt(schema, detailsBounds.center);

        if (_root != null)
        {
            Transform? board = _root.Find("Window/Shop_Backboard");
            if (board != null && TryBounds(container, out Bounds inner) && TryBounds(board, out Bounds outer))
            {
                Vector3 shift = ClampShift(inner, outer);
                if (shift != Vector3.zero)
                {
                    container.position += shift;
                }
            }
        }

        if (!schema.PlacedLogged)
        {
            schema.PlacedLogged = true;
            Plugin.Log.LogInfo($"[SCHEMA] confirm placed at bounds centre={Vec(container.position)}" +
                $" h={ContainerBounds(container).size.y:F3} detailsH={detailsBounds.size.y:F3}" +
                $" confirmScale={ConfirmScale:F3}");
        }
    }

    /// <summary>Shift that moves <paramref name="inner"/> fully inside <paramref name="outer"/>.</summary>
    private static Vector3 ClampShift(Bounds inner, Bounds outer)
    {
        return new Vector3(
            ClampAxis(inner.center.x, inner.extents.x, outer.min.x, outer.max.x),
            ClampAxis(inner.center.y, inner.extents.y, outer.min.y, outer.max.y),
            0f);
    }

    /// <summary>Delta that pulls one axis centre inside the outer bounds, zero when it does not fit.</summary>
    private static float ClampAxis(float centre, float half, float min, float max)
    {
        if (half * 2f > max - min)
        {
            return 0f;
        }

        return Geometry.ClampAxis(centre, half, min, max) - centre;
    }

    private static string Vec(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2})";
    }

    /// <summary>Retries the fit of a schema whose renderers had no bounds when built.</summary>
    private static void FitPending()
    {
        if (_detailsSchema != null)
        {
            Fit(_detailsSchema);
        }

        if (_confirmSchema != null)
        {
            Fit(_confirmSchema);
        }
    }

    /// <summary>
    /// Empty when the confirm schema belongs on screen, otherwise why it does not:
    /// our schema replaces the vanilla item picture, so it follows the confirmation
    /// group and its message object. The renderer flag and the fade alpha are not
    /// consulted: the game leaves the mesh disabled while the text is on screen.
    /// </summary>
    private static string ConfirmHiddenReason()
    {
        if (_confirmGroup == null)
        {
            return "no group";
        }

        GameObject group = _confirmGroup.gameObject;
        if (!group.activeInHierarchy)
        {
            return "group inactive";
        }

        if (_confirmMessage == null)
        {
            return "no confirm msg";
        }

        GameObject message = _confirmMessage.gameObject;
        if (!message.activeInHierarchy)
        {
            return "confirm msg inactive";
        }

        return string.IsNullOrEmpty(SafeText(_confirmMessage)) ? "confirm msg empty" : string.Empty;
    }

    /// <summary>Active state name of the item list FSM, or null when it is out of reach.</summary>
    private static string? ItemListState()
    {
        if (_root == null)
        {
            return null;
        }

        Transform? list = _root.Find("Item List Group/Item List");
        if (list == null)
        {
            foreach (Transform child in _root.GetComponentsInChildren<Transform>(true))
            {
                if (child != null && child.name == "Item List")
                {
                    list = child;
                    break;
                }
            }
        }

        PlayMakerFSM? fsm = list != null ? list.GetComponent<PlayMakerFSM>() : null;
        return fsm != null ? fsm.ActiveStateName : null;
    }

    /// <summary>World bounds of a UI panel: rect corners, then child renderers, then its position.</summary>
    private static bool TryBounds(Transform target, out Bounds bounds)
    {
        bounds = new Bounds(target.position, Vector3.zero);
        RectTransform? rect = target as RectTransform;
        if (rect != null)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector3 size = corners[2] - corners[0];
            if (Mathf.Abs(size.x) > 0.001f && Mathf.Abs(size.y) > 0.001f)
            {
                bounds = new Bounds((corners[0] + corners[2]) * 0.5f,
                    new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));
                return true;
            }
        }

        bool any = false;
        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
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

        return true;
    }

    /// <summary>Finds the details panel and its name label, once per session.</summary>
    private static bool ResolveDetails()
    {
        if (_details != null && _nameLabel != null)
        {
            return true;
        }

        if (_root == null)
        {
            return false;
        }

        _details = FindDetails(_root);
        _nameLabel = _details != null ? FindText(_details, "Item name") : null;
        _descLabel = _details != null ? FindText(_details, "Item desc") : null;
        if (_details == null || _nameLabel == null)
        {
            _details = null;
            _nameLabel = null;
            _descLabel = null;
            if (!_missingLogged)
            {
                _missingLogged = true;
                Plugin.Log.LogWarning($"[SCHEMA] details panel or 'Item name' not found under '{ShopCrestMenu.Path(_root)}'.");
            }

            return false;
        }

        return true;
    }

    /// <summary>Records the row index the details FSM just asked the stock about.</summary>
    /// <param name="itemNum">Row index passed to <c>ShopMenuStock.GetName</c> or <c>GetDesc</c>.</param>
    internal static void NoteDetailsIndex(int itemNum)
    {
        // Other shops call the same methods; only our window's rows map onto our crests.
        if (itemNum >= 0 && ShopCrestMenu.IsOpen)
        {
            _selectedIndex = itemNum;
        }
    }

    /// <summary>
    /// Keeps the details name and description on the selected crest. The vanilla
    /// <c>Item Name Control</c> FSM writes both from <c>GetName</c>/<c>GetDesc</c> in its
    /// own Update, so this late pass only corrects a text that differs.
    /// </summary>
    internal static void LateTick()
    {
        if (_closed || _root == null || !ResolveDetails())
        {
            return;
        }

        try
        {
            ToolCrest? crest = SelectedCrest();
            if (crest == null)
            {
                return;
            }

            string name = (string)crest.DisplayName ?? string.Empty;
            string desc = (string)crest.Description ?? string.Empty;
            bool changed = WriteText(_nameLabel, name);
            changed |= WriteText(_descLabel, desc);
            if (!changed || _detailsCorrectedLogged)
            {
                return;
            }

            _detailsCorrectedLogged = true;
            Plugin.Log.LogInfo($"[SHOPFX] details text corrected name='{name}' desc='{desc}'");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPFX] details late pass failed: {ex.Message}");
        }
    }

    /// <summary>Writes <paramref name="want"/> when the label shows something else.</summary>
    /// <param name="label">Label to check, may be null.</param>
    /// <param name="want">Text the label should show.</param>
    /// <returns>True when the text was rewritten.</returns>
    private static bool WriteText(TMP_Text? label, string want)
    {
        if (label == null || want.Length == 0 || SafeText(label) == want)
        {
            return false;
        }

        label.text = want;
        return true;
    }

    /// <summary>
    /// Crest the details panel currently describes: the row index the FSM asked about,
    /// then the row under the selector bar, then the name text, description text last.
    /// </summary>
    private static ToolCrest? SelectedCrest()
    {
        ToolCrest? byIndex = SelectedCrestByIndex();
        if (byIndex != null)
        {
            LogPick(_selectedIndex, "fsm", byIndex);
            return byIndex;
        }

        int selectorRow = SelectedRowBySelector();
        if (selectorRow >= 0)
        {
            ToolCrest? bySelector = ShopCrestMenu.CrestForRow(selectorRow);
            if (bySelector != null)
            {
                LogPick(selectorRow, "selector", bySelector);
                return bySelector;
            }
        }

        if (_nameLabel == null || _root == null)
        {
            return null;
        }

        string? name = SafeText(_nameLabel);
        ToolCrest? byName = Match(name, true);
        if (byName != null)
        {
            LogPick(-1, "text", byName);
            return byName;
        }

        TMP_Text? desc = _descLabel ?? (_details != null ? FindText(_details, "Item desc") : null);
        ToolCrest? byDesc = Match(desc != null ? SafeText(desc) : null, false);
        if (byDesc != null)
        {
            LogPick(-1, "text", byDesc);
        }

        return byDesc;
    }

    /// <summary>Crest of the row the details FSM asked about, null when the index is stale.</summary>
    private static ToolCrest? SelectedCrestByIndex()
    {
        return _selectedIndex < 0 ? null : ShopCrestMenu.CrestForRow(_selectedIndex);
    }

    /// <summary>Row under the selector bar; -1 when the selector or the rows are missing.</summary>
    private static int SelectedRowBySelector()
    {
        if (_root == null || !ShopCosmetics.TrySelectorY(out float selectorY))
        {
            return -1;
        }

        float[] rowYs = ShopCrestMenu.RowYs();
        return rowYs.Length == 0 ? -1 : RowPick.PickRowIndex(selectorY, rowYs, RowTolerance(rowYs));
    }

    /// <summary>Half the smallest gap between rows, so the selector picks only a real row.</summary>
    /// <param name="rowYs">Row Y positions; NaN entries are skipped.</param>
    /// <returns>Row step over two, unlimited when there is at most one row.</returns>
    private static float RowTolerance(float[] rowYs)
    {
        List<float> ys = new List<float>(rowYs.Length);
        foreach (float y in rowYs)
        {
            if (!float.IsNaN(y) && !float.IsInfinity(y))
            {
                ys.Add(y);
            }
        }

        if (ys.Count <= 1)
        {
            return float.PositiveInfinity;
        }

        ys.Sort();
        float smallest = float.MaxValue;
        for (int i = 1; i < ys.Count; i++)
        {
            float gap = ys[i] - ys[i - 1];
            if (gap > 0f && gap < smallest)
            {
                smallest = gap;
            }
        }

        return smallest == float.MaxValue ? float.PositiveInfinity : smallest * 0.5f;
    }

    /// <summary>Writes the row pick once, so a change of crest or source is visible in the log.</summary>
    /// <param name="index">Row the crest came from; -1 for the text path.</param>
    /// <param name="source">Path that resolved the crest.</param>
    /// <param name="crest">Resolved crest.</param>
    private static void LogPick(int index, string source, ToolCrest crest)
    {
        string name = crest.name ?? string.Empty;
        if (source == _pickSource && index == _pickIndex && name == _pickName)
        {
            return;
        }

        _pickSource = source;
        _pickIndex = index;
        _pickName = name;
        Plugin.Log.LogInfo($"[SCHEMA] row index={index} source={source} crest='{name}'");
    }

    /// <summary>Compares one text against every crest of the window.</summary>
    /// <param name="text">Text shown by the details panel.</param>
    /// <param name="byName">True to compare display names, false to compare descriptions.</param>
    private static ToolCrest? Match(string? text, bool byName)
    {
        if (string.IsNullOrEmpty(text) || _root == null)
        {
            return null;
        }

        foreach (ShopItemStats row in _root.GetComponentsInChildren<ShopItemStats>(true))
        {
            ToolCrest? crest = row != null ? ShopCrestMenu.CrestFor(row.Item) : null;
            if (crest == null)
            {
                continue;
            }

            string candidate = byName ? crest.DisplayName : crest.Description;
            if (!string.IsNullOrEmpty(candidate) && candidate == text)
            {
                return crest;
            }
        }

        return null;
    }

    private static string SafeText(TMP_Text label)
    {
        try
        {
            return label.text ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>Builds the silhouette and the slot dots for <paramref name="crest"/>.</summary>
    private static void Build(ToolCrest crest)
    {
        DestroySchema();
        _builtFor = crest;

        Sprite? silhouette = crest.CrestSilhouette;
        bool darkened = silhouette == null;
        if (silhouette == null)
        {
            silhouette = crest.CrestSprite;
        }

        if (silhouette == null || _details == null || _nameLabel == null)
        {
            Plugin.Log.LogWarning($"[SCHEMA] '{crest.name}' has no silhouette or sprite, schema skipped.");
            return;
        }

        _detailsSchema = BuildSchema(crest, silhouette, darkened, 1f, true, "Crest Schema");
        _confirmSchema = BuildSchema(crest, silhouette, darkened, ConfirmScale, false,
            "Confirm Crest Schema");
        Plugin.Log.LogInfo($"[SCHEMA] built '{crest.name}' mode={Render} live={LiveContainers()}");
    }

    /// <summary>
    /// Creates one schema instance at container scale 1 and fits it to the details
    /// panel (when <paramref name="fitPanel"/>) or to <see cref="BaseHeight"/> times
    /// <paramref name="sizeFactor"/> world units from the size actually drawn, so no
    /// assumption about the parent scale chain is made. The container lives under the
    /// neutral schema root and is placed by the caller.
    /// </summary>
    private static Schema? BuildSchema(ToolCrest crest, Sprite silhouette, bool darkened,
        float sizeFactor, bool fitPanel, string label)
    {
        Transform? host = SchemaHost();
        if (host == null)
        {
            if (!_missingLogged)
            {
                _missingLogged = true;
                Plugin.Log.LogWarning($"[SCHEMA] '{label}' has no fade-free parent, schema skipped.");
            }

            return null;
        }

        SpriteRenderer? reference = ReferenceRenderer() ?? BackboardRenderer();
        float targetH = BaseHeight * Scale * sizeFactor;
        float targetW = 0f;
        Vector2 panel = Vector2.zero;
        if (fitPanel && TryPanelSize(out panel))
        {
            targetH = PanelFill * panel.y * Scale;
            targetW = PanelFill * panel.x * Scale;
        }

        GameObject container = new GameObject(label);
        container.transform.SetParent(host, false);
        container.transform.localScale = Vector3.one;

        GameObject content = new GameObject(ContentName);
        content.transform.SetParent(container.transform, false);
        content.transform.localScale = Vector3.one;
        LogContainer(container.transform);

        if (Render == SchemaRenderMode.Clone)
        {
            Schema? clone = CloneSchema(crest, container, content, targetH, targetW, panel, label);
            if (clone != null)
            {
                LiftConfirm(clone, fitPanel);
                LogSprites("clone");
                return clone;
            }
        }

        bool vanilla = Render != SchemaRenderMode.Generated && TemplateSprites() != null;
        SpriteRenderer body = AddRenderer(content, "Schema Silhouette", silhouette, reference, 0);
        body.color = darkened ? new Color(0.3f, 0.3f, 0.3f, 1f) : Color.white;

        Schema schema = new Schema(label, container, content, body, targetH)
        {
            TargetWidth = targetW,
            PanelSize = panel,
        };
        AddDots(schema, crest, reference);
        Fit(schema);
        LiftConfirm(schema, fitPanel);
        LogSprites(vanilla ? "vanilla" : "generated");
        return schema;
    }

    /// <summary>
    /// Scales the container once to <see cref="Schema.TargetHeight"/> from the height
    /// actually drawn, then fits every dot to a fraction of the height the silhouette
    /// draws *after* that scale, up to a few attempts per frame.
    /// </summary>
    private static void Fit(Schema schema)
    {
        if (!FitContainer(schema))
        {
            return;
        }

        float h2 = WorldHeight(schema.Body);
        if (h2 <= 0.0001f)
        {
            return;
        }

        Vector3 dotOffset = ApplyDotOffset(schema, h2);

        if (!schema.BoundsLogged)
        {
            schema.BoundsLogged = true;
            Bounds bounds = schema.Body.bounds;
            Transform content = schema.Content.transform;
            Plugin.Log.LogInfo($"[SCHEMA] {schema.Label} silhouette" +
                $" bounds={bounds.size.x:F3}x{bounds.size.y:F3}x{bounds.size.z:F3}" +
                $" h2={h2:F3} targetH={schema.TargetHeight:F3}" +
                $" lossyScale={content.lossyScale.y:F3} contentScale={content.localScale.x:F3}");
        }

        float wanted = h2 * DotSize;
        float firstBefore = schema.Dots.Count > 0 ? WorldHeight(schema.Dots[0]) : 0f;
        Vector3 firstScaleBefore = schema.Dots.Count > 0
            ? schema.Dots[0].transform.localScale
            : Vector3.zero;
        int attempts = 0;
        bool exhausted = false;

        for (int i = 0; i < schema.Dots.Count; i++)
        {
            if (schema.DotDone[i])
            {
                continue;
            }

            SpriteRenderer dot = schema.Dots[i];
            Transform scale = dot.transform;
            float target = wanted * (schema.Locked[i] ? LockedDotFactor : 1f);
            Color color = dot.color;
            color.a = schema.Locked[i] ? LockedAlpha : 1f;
            dot.color = color;

            for (int attempt = 0; attempt < MaxDotAttempts; attempt++)
            {
                float measured = WorldHeight(dot);
                if (measured <= 0.0001f)
                {
                    break;
                }

                if (Mathf.Abs(measured - target) <= target * DotTolerance)
                {
                    schema.DotDone[i] = true;
                    break;
                }

                attempts++;
                float factor = target / measured;
                Vector3 current = scale.localScale;
                scale.localScale = new Vector3(current.x * factor, current.y * factor, 1f);
            }

            if (schema.DotDone[i] || IsClose(WorldHeight(dot), target))
            {
                schema.DotDone[i] = true;
            }
            else if (attempts >= MaxDotAttempts)
            {
                exhausted = true;
            }
        }

        if (schema.Logged || (!schema.AllDone && !exhausted))
        {
            return;
        }

        schema.Logged = true;
        string first = schema.Dots.Count > 0
            ? $"dot0 measuredBefore={firstBefore:F3} measuredAfter={WorldHeight(schema.Dots[0]):F3}" +
                $" localScaleBefore={firstScaleBefore.x:F3} localScaleAfter={schema.Dots[0].transform.localScale.x:F3}"
            : "dot0 n/a";
        Plugin.Log.LogInfo($"[SCHEMA] {schema.Label} H2={h2:F3} CrestDotSize={DotSize:F3}" +
            $" dotSizeRaw={DotSizeRaw:F3} dotSizeUsed={DotSize:F3}" +
            $" dotOffset={dotOffset.y:F4} offsetY={DotOffsetY:F3}" +
            $" desiredDotWorld={wanted:F3} {first} dots={schema.Dots.Count} attempts={attempts}");
    }

    /// <summary>
    /// Drops every dot by <see cref="DotOffsetY"/> times the drawn height, once. The shift
    /// is built in world space so no container orientation can flip the direction.
    /// </summary>
    private static Vector3 ApplyDotOffset(Schema schema, float h2)
    {
        if (schema.OffsetApplied || schema.BasePositions.Count != schema.Dots.Count)
        {
            return schema.Offset;
        }

        schema.OffsetApplied = true;
        Vector3 world = new Vector3(0f, DotOffsetY * h2, 0f);
        Vector3 local = schema.Content.transform.InverseTransformVector(world);
        schema.Offset = local;
        for (int i = 0; i < schema.Dots.Count; i++)
        {
            schema.Dots[i].transform.localPosition = schema.BasePositions[i] + local;
        }

        return local;
    }

    /// <summary>
    /// Scales the container to the wanted height and width once, when it reports a
    /// real size, and logs the numbers the dot fit depends on.
    /// </summary>
    private static bool FitContainer(Schema schema)
    {
        if (schema.ContainerFitted)
        {
            return true;
        }

        Bounds bounds = schema.Body.bounds;
        float measuredH = WorldHeight(schema.Body);
        if (measuredH <= 0.0001f)
        {
            return false;
        }

        float scaleApplied = schema.TargetHeight / measuredH;
        if (schema.TargetWidth > 0.0001f && bounds.size.x > 0.0001f)
        {
            scaleApplied = Mathf.Min(scaleApplied, schema.TargetWidth / bounds.size.x);
        }

        Transform transform = schema.Content.transform;
        transform.localScale *= scaleApplied;
        schema.ContainerFitted = true;
        if (schema.PanelSize.x > 0f && schema.PanelSize.y > 0f)
        {
            Plugin.Log.LogInfo($"[SCHEMA] fit panel=({schema.PanelSize.x:F3} x {schema.PanelSize.y:F3})" +
                $" silhouette=({bounds.size.x:F3} x {bounds.size.y:F3}) scale={scaleApplied:F4}");
        }

        return true;
    }

    /// <summary>World bounds of everything drawn under a root; a zero-size bounds when nothing is.</summary>
    private static Bounds ContainerBounds(Transform target)
    {
        bool any = false;
        Bounds bounds = default;
        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || (renderer is SpriteRenderer sprite && sprite.sprite == null) ||
                renderer.bounds.size.sqrMagnitude <= 0.0000001f)
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

        return any ? bounds : new Bounds(target.position, Vector3.zero);
    }

    /// <summary>World height of everything drawn under a root, used to fit a whole clone.</summary>
    private static float ContainerHeight(Transform target)
    {
        return ContainerBounds(target).size.y;
    }

    /// <summary>World width and height of the details panel; false when it draws nothing.</summary>
    private static bool TryPanelSize(out Vector2 size)
    {
        size = Vector2.zero;
        if (_details == null || !TryBounds(_details, out Bounds bounds))
        {
            return false;
        }

        if (bounds.size.x <= 0.001f || bounds.size.y <= 0.001f)
        {
            return false;
        }

        size = new Vector2(bounds.size.x, bounds.size.y);
        return true;
    }

    /// <summary>True when the measured height is within the dot tolerance of the wanted one.</summary>
    private static bool IsClose(float measured, float target)
    {
        return measured > 0.0001f && Mathf.Abs(measured - target) <= target * DotTolerance;
    }

    /// <summary>World height the renderer actually draws; sprite size when bounds are empty.</summary>
    private static float WorldHeight(Renderer? renderer)
    {
        if (renderer == null)
        {
            return 0f;
        }

        float measured = renderer.bounds.size.y;
        if (measured > 0.0001f)
        {
            return measured;
        }

        SpriteRenderer? sprite = renderer as SpriteRenderer;
        return sprite != null && sprite.sprite != null
            ? SpriteHeight(sprite.sprite) * Mathf.Abs(renderer.transform.lossyScale.y)
            : 0f;
    }

    /// <summary>Native world height of one sprite at scale 1.</summary>
    private static float SpriteHeight(Sprite sprite)
    {
        return sprite.rect.height / Mathf.Max(1f, sprite.pixelsPerUnit);
    }

    /// <summary>
    /// Vanilla slot sprites read once off the <c>templateSlots</c> of a live crest
    /// widget, indexed by <see cref="ToolItemType"/>; null when they are out of reach.
    /// </summary>
    private static Sprite?[]? TemplateSprites()
    {
        if (_templateRead)
        {
            return _templateSprites;
        }

        _templateRead = true;
        try
        {
            InventoryToolCrest? live = LiveCrest();
            _inventoryFound = live != null;
            if (live == null)
            {
                LogOnce("[SCHEMA] no InventoryToolCrest in scene");
                return null;
            }

            if (!(TemplateSlotsField?.GetValue(live) is InventoryToolCrestSlot[] slots) || slots.Length == 0)
            {
                LogOnce("[SCHEMA] templateSlots read failed: field missing, null or empty");
                return null;
            }

            Sprite?[] sprites = new Sprite?[slots.Length];
            int found = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                Sprite? sprite = slots[i] != null
                    ? SlotTypeSpriteField?.GetValue(slots[i]) as Sprite
                    : null;
                sprites[i] = sprite;
                if (sprite != null)
                {
                    found++;
                }
            }

            _templateSprites = found > 0 ? sprites : null;
            if (found == 0)
            {
                LogOnce("[SCHEMA] templateSlots read failed: no slotTypeSprite");
            }
        }
        catch (Exception ex)
        {
            _templateSprites = null;
            LogOnce($"[SCHEMA] templateSlots read failed: {ex.Message}");
        }

        return _templateSprites;
    }

    /// <summary>First crest widget of a loaded scene that is not an asset or a hidden copy.</summary>
    private static InventoryToolCrest? LiveCrest()
    {
        foreach (InventoryToolCrest crest in Resources.FindObjectsOfTypeAll<InventoryToolCrest>())
        {
            if (crest == null)
            {
                continue;
            }

            GameObject go = crest.gameObject;
            if (go.scene.isLoaded && go.hideFlags == HideFlags.None)
            {
                return crest;
            }
        }

        return null;
    }

    /// <summary>Sprite of one slot type: the vanilla template sprite, else the generated circle.</summary>
    private static Sprite? DotSprite(ToolItemType type)
    {
        if (Render == SchemaRenderMode.Generated)
        {
            return DotSprite();
        }

        Sprite?[]? sprites = TemplateSprites();
        int index = (int)type;
        if (sprites != null && index >= 0 && index < sprites.Length && sprites[index] != null)
        {
            return sprites[index];
        }

        return DotSprite();
    }

    /// <summary>
    /// Copies the vanilla crest card of <paramref name="crest"/> into the schema when the
    /// template sprites are unavailable. Null when no such crest is in the loaded scene.
    /// </summary>
    private static Schema? CloneSchema(ToolCrest crest, GameObject container, GameObject content,
        float targetH, float targetW, Vector2 panel, string label)
    {
        try
        {
            InventoryToolCrest? source = CrestTemplate(crest);
            if (source == null)
            {
                LogOnce($"[SCHEMA] no clone with CrestData={crest.name}");
                return null;
            }

            GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, content.transform, false);
            clone.name = label;
            clone.SetActive(false);
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;

            // The vanilla components drive themselves from a manager the clone does not have.
            InventoryToolCrestSlot[] slots = clone.GetComponentsInChildren<InventoryToolCrestSlot>(true);
            foreach (InventoryToolCrestSlot slot in slots)
            {
                slot.enabled = false;
            }

            InventoryToolCrest? widget = clone.GetComponent<InventoryToolCrest>();
            if (widget != null)
            {
                widget.enabled = false;
                UnityEngine.Object.Destroy(widget);
            }

            clone.SetActive(true);
            ResetFades(clone);
            foreach (InventoryToolCrestSlot slot in slots)
            {
                slot.ItemFlashAmount = 0f;
                UnityEngine.Object.Destroy(slot);
            }

            Renderer? body = SilhouetteRenderer(clone.transform);
            if (body == null)
            {
                Plugin.Log.LogWarning($"[SCHEMA] '{label}' clone has no silhouette to draw, schema skipped.");
                UnityEngine.Object.Destroy(clone);
                return null;
            }

            Schema schema = new Schema(label, container, content, body, targetH)
            {
                TargetWidth = targetW,
                PanelSize = panel,
            };
            Fit(schema);
            float whole = ContainerHeight(clone.transform);
            Plugin.Log.LogInfo($"[SCHEMA] {label} cloned from '{source.name}'" +
                $" (active={source.gameObject.activeInHierarchy}) slots={slots.Length}" +
                $" wholeH={whole:F3} targetH={targetH:F3}");
            return schema;
        }
        catch (Exception ex)
        {
            LogOnce($"[SCHEMA] clone failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Loaded crest widget of <paramref name="crest"/>, inactive one preferred.</summary>
    private static InventoryToolCrest? CrestTemplate(ToolCrest crest)
    {
        InventoryToolCrest? active = null;
        foreach (InventoryToolCrest candidate in Resources.FindObjectsOfTypeAll<InventoryToolCrest>())
        {
            if (candidate == null)
            {
                continue;
            }

            GameObject go = candidate.gameObject;
            if (!go.scene.isLoaded || go.hideFlags != HideFlags.None || candidate.CrestData != crest)
            {
                continue;
            }

            if (!go.activeInHierarchy)
            {
                return candidate;
            }

            active ??= candidate;
        }

        return active;
    }

    /// <summary>Clears the faded look the vanilla widget had in its list.</summary>
    private static void ResetFades(GameObject clone)
    {
        foreach (NestedFadeGroupBase group in clone.GetComponentsInChildren<NestedFadeGroupBase>(true))
        {
            if (group != null)
            {
                group.AlphaSelf = 1f;
            }
        }
    }

    /// <summary>Biggest renderer of a built object, fallback when no silhouette is named.</summary>
    private static Renderer? LargestRenderer(Transform target)
    {
        Renderer? best = null;
        float height = 0f;
        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || (renderer is SpriteRenderer sprite && sprite.sprite == null))
            {
                continue;
            }

            float measured = renderer.bounds.size.y;
            if (measured > height)
            {
                height = measured;
                best = renderer;
            }
        }

        return best;
    }

    /// <summary>
    /// Silhouette renderer of a cloned vanilla crest card: the fit must measure the
    /// silhouette alone, because the slot icons sit tens of units away from it and
    /// would shrink the whole card to a dot. Falls back to any renderer named like a
    /// silhouette, then to the largest one.
    /// </summary>
    private static Renderer? SilhouetteRenderer(Transform clone)
    {
        Renderer? named = null;
        Renderer? containing = null;
        foreach (Renderer renderer in clone.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || (renderer is SpriteRenderer sprite && sprite.sprite == null))
            {
                continue;
            }

            string name = renderer.gameObject.name;
            if (string.Equals(name, SilhouetteName, StringComparison.OrdinalIgnoreCase))
            {
                named = renderer;
                break;
            }

            if (containing == null &&
                name.IndexOf("silhouette", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                containing = renderer;
            }
        }

        return named ?? containing ?? LargestRenderer(clone);
    }

    /// <summary>One <c>[SCHEMA] sprites:</c> line per session.</summary>
    private static void LogSprites(string used)
    {
        if (_spritesLogged)
        {
            return;
        }

        _spritesLogged = true;
        Sprite?[]? sprites = _templateSprites;
        int count = 0;
        if (sprites != null)
        {
            foreach (Sprite? sprite in sprites)
            {
                if (sprite != null)
                {
                    count++;
                }
            }
        }

        string templates = sprites != null ? $"{count}/{sprites.Length}" : "n/a";
        Plugin.Log.LogInfo($"[SCHEMA] sprites: inventory={(_inventoryFound ? "true" : "false")}" +
            $" templates={templates} used={used}");
    }

    /// <summary>Logs one reason once per session, however often the path is retried.</summary>
    private static void LogOnce(string message)
    {
        if (LoggedReasons.Add(message))
        {
            Plugin.Log.LogWarning(message);
        }
    }

    /// <summary>
    /// Neutral parent of every schema container: a <c>CrestPicker Schemas</c> object under
    /// the first ancestor of the shop window that carries no fade group. Parented under a
    /// fade group, the containers were walked by <c>NestedFadeGroupBase.UpdateAlpha</c>
    /// after destruction (null reference) and inherited a 3-4x lossy scale.
    /// </summary>
    private static Transform? SchemaHost()
    {
        if (_schemaRoot != null)
        {
            return _schemaRoot;
        }

        if (_root == null)
        {
            return null;
        }

        Transform? host = null;
        for (Transform? current = _root; current != null; current = current.parent)
        {
            if (current.GetComponent<NestedFadeGroupBase>() == null)
            {
                host = current;
                break;
            }
        }

        if (host == null)
        {
            Transform top = _root;
            while (top.parent != null)
            {
                top = top.parent;
            }

            host = top;
            LogOnce($"[SCHEMA] no fade-free ancestor above '{PathOf(_root)}', schemas parented to '{PathOf(top)}'");
        }

        GameObject go = new GameObject(SchemaRootName);
        go.transform.SetParent(host, false);
        go.transform.localScale = Vector3.one;
        _schemaRoot = go.transform;
        Plugin.Log.LogInfo($"[SCHEMA] schema root parent='{PathOf(_schemaRoot)}'" +
            $" fadeAncestors={FadeAncestors(_schemaRoot)}");
        return _schemaRoot;
    }

    /// <summary>Destroys the neutral schema root, so nothing of ours outlives the session.</summary>
    private static void DestroySchemaRoot()
    {
        if (_schemaRoot == null)
        {
            _schemaRoot = null;
            return;
        }

        GameObject root = _schemaRoot.gameObject;
        root.SetActive(false);
        UnityEngine.Object.Destroy(root);
        Plugin.Log.LogInfo($"[SCHEMA] schema root retired live={LiveContainers()}");
        _schemaRoot = null;
    }

    /// <summary>One <c>[SCHEMA] container</c> line: where the container sits and what it inherits.</summary>
    private static void LogContainer(Transform container)
    {
        Vector3 scale = container.lossyScale;
        Plugin.Log.LogInfo($"[SCHEMA] container parent='{PathOf(container)}'" +
            $" fadeAncestors={FadeAncestors(container)}" +
            $" lossyScale=({scale.x:F3},{scale.y:F3},{scale.z:F3})");
    }

    /// <summary>How many fade groups the object and its ancestors carry; zero means none.</summary>
    private static int FadeAncestors(Transform target)
    {
        int count = 0;
        for (Transform? current = target; current != null; current = current.parent)
        {
            if (current.GetComponent<NestedFadeGroupBase>() != null)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Scene path of an object, for the schema logs.</summary>
    private static string PathOf(Transform target)
    {
        string path = target.name;
        for (Transform? current = target.parent; current != null; current = current.parent)
        {
            path = current.name + "/" + path;
        }

        return path;
    }

    /// <summary>Confirm group and the item picture the confirm schema is centred on.</summary>
    private static Transform? ConfirmGroup()
    {
        if (_confirmGroup != null)
        {
            return _confirmGroup;
        }

        if (_root == null)
        {
            return null;
        }

        Transform? direct = _root.Find(ConfirmGroupPath);
        if (direct == null)
        {
            foreach (Transform child in _root.GetComponentsInChildren<Transform>(true))
            {
                if (child != null && child.name == "Item Confirm Group")
                {
                    direct = child;
                    break;
                }
            }
        }

        _confirmGroup = direct;
        if (_confirmGroup != null)
        {
            Transform? picture = _confirmGroup.Find("Item Sprite");
            // The vanilla FSM draws the item icon here; our schema replaces it.
            HideObject(picture != null ? picture.gameObject : null);
            _confirmMessage = FindText(_confirmGroup, "Confirm msg");
        }

        return _confirmGroup;
    }

    /// <summary>Hides one object for the session, remembering its vanilla state once.</summary>
    private static void HideObject(GameObject? go)
    {
        if (go == null)
        {
            return;
        }

        if (!ObjectStates.ContainsKey(go))
        {
            ObjectStates[go] = go.activeSelf;
        }

        if (go.activeSelf)
        {
            go.SetActive(false);
        }
    }

    /// <summary>
    /// Adds one dot per slot as a child of the schema container. Slot positions are
    /// silhouette-local units and need no factor; the diameter is fitted afterwards.
    /// </summary>
    private static void AddDots(Schema schema, ToolCrest crest, SpriteRenderer? reference)
    {
        ToolCrest.SlotInfo[]? slots = crest.Slots;
        if (slots == null || slots.Length == 0)
        {
            Plugin.Log.LogInfo($"[SCHEMA] '{crest.name}' has no slots, schema drawn without dots.");
            return;
        }

        float lossy = Mathf.Abs(schema.Content.transform.lossyScale.y);
        for (int i = 0; i < slots.Length; i++)
        {
            ToolCrest.SlotInfo slot = slots[i];
            Sprite? dot = DotSprite(slot.Type);
            if (dot == null)
            {
                continue;
            }

            float approx = lossy > 0.0001f
                ? (schema.TargetHeight * DotSize) / (SpriteHeight(dot) * lossy)
                : 1f;
            SpriteRenderer renderer = AddRenderer(schema.Content, $"Schema Dot {i}", dot, reference, 1);
            Vector2 position = slot.Position;
            Vector3 local = new Vector3(position.x, position.y, 0f);
            renderer.transform.localPosition = local;
            renderer.transform.localScale = new Vector3(approx, approx, 1f);
            renderer.color = TypeColor(slot.Type);
            schema.Dots.Add(renderer);
            schema.BasePositions.Add(local);
            schema.Locked.Add(IsLocked(crest, i, slot));
            schema.DotDone.Add(false);
        }
    }

    /// <summary>True when the slot is locked by the crest itself or by the save data.</summary>
    private static bool IsLocked(ToolCrest crest, int index, ToolCrest.SlotInfo slot)
    {
        if (slot.IsLocked)
        {
            return true;
        }

        try
        {
            List<ToolCrestsData.SlotData>? saved = crest.SaveData.Slots;
            if (saved == null || index >= saved.Count)
            {
                return false;
            }

            return !saved[index].IsUnlocked;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Slot colour from the game palette, with a local fallback.</summary>
    private static Color TypeColor(ToolItemType type)
    {
        try
        {
            return UI.GetToolTypeColor(type);
        }
        catch (Exception)
        {
            return FallbackColors.TryGetValue(type, out Color color) ? color : Color.white;
        }
    }

    /// <summary>Adds one world-space sprite child with the window's layer and sorting.</summary>
    private static SpriteRenderer AddRenderer(GameObject parent, string name, Sprite sprite,
        SpriteRenderer? reference, int orderOffset)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.layer = 5;
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        if (reference != null)
        {
            go.layer = reference.gameObject.layer;
            renderer.sortingLayerID = reference.sortingLayerID;
            renderer.sortingOrder = reference.sortingOrder + orderOffset;
        }

        return renderer;
    }

    /// <summary>Row icon renderer, the closest neighbour sprite of the window.</summary>
    private static SpriteRenderer? ReferenceRenderer()
    {
        if (_root == null)
        {
            return null;
        }

        foreach (ShopItemStats row in _root.GetComponentsInChildren<ShopItemStats>(true))
        {
            SpriteRenderer? icon = ShopCosmetics.RowIcon(row);
            if (icon != null && icon.enabled)
            {
                return icon;
            }
        }

        return null;
    }

    /// <summary>Backboard sprite as a second sorting reference.</summary>
    private static SpriteRenderer? BackboardRenderer()
    {
        Transform? backboard = _root != null ? _root.Find("Window/Shop_Backboard") : null;
        return backboard != null ? backboard.GetComponent<SpriteRenderer>() : null;
    }

    /// <summary>
    /// Gives the confirm schema its own sorting order instead of one relative to the
    /// row icon: the vanilla item picture and the backboard both sit below the
    /// confirmation text, and the schema must not be drawn over by either. The layer
    /// stays the window's HUD layer. Z is left alone.
    /// </summary>
    /// <param name="schema">Schema that was just built.</param>
    /// <param name="fitPanel">True for the details schema, which keeps its own order.</param>
    private static void LiftConfirm(Schema schema, bool fitPanel)
    {
        if (fitPanel)
        {
            return;
        }

        SpriteRenderer? board = BackboardRenderer();
        if (board == null || schema.Container == null)
        {
            return;
        }

        int order = board.sortingOrder + ConfirmOrderOffset;
        foreach (Renderer renderer in schema.Container.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
            {
                continue;
            }

            renderer.sortingLayerID = board.sortingLayerID;
            renderer.sortingOrder = order;
        }

        if (!_confirmOrderLogged)
        {
            _confirmOrderLogged = true;
            Plugin.Log.LogInfo($"[SCHEMA] confirm order={order} (backboard={board.sortingOrder})");
        }
    }

    /// <summary>Generated circle used as a slot dot.</summary>
    private static Sprite? DotSprite()
    {
        if (_dotSprite != null)
        {
            return _dotSprite;
        }

        try
        {
            const int size = 32;
            float radius = size / 2f - 0.5f;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - radius;
                    float dy = y - radius;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(radius + 0.5f - distance);
                    pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            _dotSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SCHEMA] dot sprite creation failed: {ex.Message}");
        }

        return _dotSprite;
    }

    /// <summary>One built schema instance and its pending fit.</summary>
    private sealed class Schema
    {
        internal Schema(string label, GameObject container, GameObject content, Renderer body,
            float targetHeight)
        {
            Label = label;
            Container = container;
            Content = content;
            Body = body;
            TargetHeight = targetHeight;
        }

        internal string Label { get; }

        internal GameObject Container { get; }

        /// <summary>Child that carries the fit scale; the container itself stays at scale 1.</summary>
        internal GameObject Content { get; }

        internal Renderer Body { get; }

        /// <summary>Wanted world height of the silhouette.</summary>
        internal float TargetHeight { get; }

        /// <summary>Wanted maximum world width; zero means height only.</summary>
        internal float TargetWidth { get; set; }

        /// <summary>World size of the panel the schema was fitted to; zero when not fitted to one.</summary>
        internal Vector2 PanelSize { get; set; }

        /// <summary>True once the confirm placement numbers were logged.</summary>
        internal bool PlacedLogged { get; set; }

        internal List<SpriteRenderer> Dots { get; } = new List<SpriteRenderer>();

        /// <summary>Schema-local dot positions as built, before the offset is applied.</summary>
        internal List<Vector3> BasePositions { get; } = new List<Vector3>();

        /// <summary>Local shift the dots were moved by, zero for a clone.</summary>
        internal Vector3 Offset { get; set; }

        /// <summary>True once the dots took the <see cref="DotOffsetY"/> shift.</summary>
        internal bool OffsetApplied { get; set; }

        /// <summary>Locked flag per dot, in creation order.</summary>
        internal List<bool> Locked { get; } = new List<bool>();

        /// <summary>Fit-done flag per dot, in creation order.</summary>
        internal List<bool> DotDone { get; } = new List<bool>();

        /// <summary>True once the container reached its wanted height.</summary>
        internal bool ContainerFitted { get; set; }

        /// <summary>True once the fit numbers were logged for this schema.</summary>
        internal bool Logged { get; set; }

        /// <summary>True once the silhouette bounds were logged for this schema.</summary>
        internal bool BoundsLogged { get; set; }

        /// <summary>True when every dot reached its wanted diameter.</summary>
        internal bool AllDone
        {
            get
            {
                foreach (bool done in DotDone)
                {
                    if (!done)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }

    /// <summary>
    /// Destroys the schema objects of the current crest, details and confirm alike.
    /// Containers are switched off at once because <c>Destroy</c> only takes effect at the
    /// end of the frame: a rebuilt schema would otherwise overlap the old one and show a
    /// doubled picture on the first frame.
    /// </summary>
    private static void DestroySchema()
    {
        if (_detailsSchema != null)
        {
            Retire(_detailsSchema.Container);
            _detailsSchema = null;
        }

        if (_confirmSchema != null)
        {
            Retire(_confirmSchema.Container);
            _confirmSchema = null;
        }
    }

    /// <summary>Hides one schema container right away, then destroys it and logs how many stay.</summary>
    private static void Retire(GameObject container)
    {
        if (container == null)
        {
            return;
        }

        string path = PathOf(container.transform);
        int fade = FadeAncestors(container.transform);
        container.SetActive(false);
        UnityEngine.Object.Destroy(container);
        Plugin.Log.LogInfo($"[SCHEMA] container retired live={LiveContainers()}" +
            $" parent='{path}' fadeAncestors={fade}");
    }

    /// <summary>Schema containers currently drawn, retired ones excluded.</summary>
    private static int LiveContainers()
    {
        int count = 0;
        foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go == null || !go.scene.isLoaded || go.hideFlags != HideFlags.None ||
                !go.activeInHierarchy)
            {
                continue;
            }

            if (go.name == "Crest Schema" || go.name == "Confirm Crest Schema")
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Details panel transform by path, then by name.</summary>
    private static Transform? FindDetails(Transform root)
    {
        Transform? direct = root.Find(DetailsPath);
        if (direct != null)
        {
            return direct;
        }

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child != null && child.name == "Item Details")
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>One text child of the details panel by name.</summary>
    private static TMP_Text? FindText(Transform details, string name)
    {
        Transform? direct = details.Find(name);
        TMP_Text? found = direct != null ? direct.GetComponent<TMP_Text>() : null;
        if (found != null)
        {
            return found;
        }

        foreach (TMP_Text text in details.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text != null && text.name == name)
            {
                return text;
            }
        }

        return null;
    }
}

/// <summary>
/// Feeds <see cref="ShopCrestSchema"/> the stock row the details FSM asks about through
/// <c>ShopMenuStock.GetName</c>. The row number indexes <c>availableStock</c>, so it is
/// the authoritative link between the visible row and the crest.
/// </summary>
[HarmonyPatch(typeof(ShopMenuStock), nameof(ShopMenuStock.GetName), new[] { typeof(int) })]
internal static class CrestRowNamePatch
{
    private static void Prefix(int __0)
    {
        ShopCrestSchema.NoteDetailsIndex(__0);
    }
}

/// <summary>
/// Same link as <see cref="CrestRowNamePatch"/> for <c>ShopMenuStock.GetDesc</c>: the FSM
/// may ask the name and the description in different states, the last asked row wins.
/// </summary>
[HarmonyPatch(typeof(ShopMenuStock), nameof(ShopMenuStock.GetDesc), new[] { typeof(int) })]
internal static class CrestRowDescPatch
{
    private static void Prefix(int __0)
    {
        ShopCrestSchema.NoteDetailsIndex(__0);
    }
}
