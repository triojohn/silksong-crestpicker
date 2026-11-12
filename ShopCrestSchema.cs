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
    Sprite = 1,

    /// <summary>Old name of <see cref="Sprite"/>; kept so an existing config still parses.</summary>
    VanillaSprites = Sprite,

    /// <summary>Draw the silhouette and generated circle dots.</summary>
    Generated = 2,
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
    /// <summary>Panel height in world units used when the details panel cannot be measured.</summary>
    private const float BaseHeight = 1.2f;

    /// <summary>Fraction of the details panel the details schema fills.</summary>
    private const float PanelFill = 0.8f;

    /// <summary>Path of the details panel inside the shop window.</summary>
    private const string DetailsPath = "Item List Group/Item Details";

    /// <summary>Confirm group whose vanilla item picture the confirm schema replaces.</summary>
    private const string ConfirmGroupPath = "Item Confirm Group";

    /// <summary>Sorting orders the confirm schema is lifted above the backboard by.</summary>
    private const int ConfirmOrderOffset = 2;

    /// <summary>Room kept above the confirmation text, as a fraction of its height.</summary>
    private const float ConfirmGap = 0.15f;

    /// <summary>Smallest confirm schema scale left when the crest name leaves no room.</summary>
    private const float ConfirmFitMin = 0.5f;

    /// <summary>Names of the two confirmation texts: the question and the purchase line.</summary>
    private const string ConfirmMessageName = "Confirm msg";
    private const string PurchaseMessageName = "Thankyou";

    /// <summary>Anchor names of the confirmation actually on screen, as written to the log.</summary>
    private const string AnchorNone = "none";
    private const string AnchorThanks = "Thankyou";
    private const string AnchorQuestion = "ConfirmMsg";

    /// <summary>Silhouette renderer inside the vanilla crest card the clone is fitted by.</summary>
    private const string SilhouetteName = "crestSilhouette";

    /// <summary>Slot scale of an unselected vanilla crest card (<c>_slotScaleOther</c>).</summary>
    private const float CloneSlotScale = 0.7f;

    /// <summary>Vanilla local scale of the silhouette child inside a crest card.</summary>
    private const float SilhouetteScale = 2f;

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
    private static TMP_Text? _confirmThanks;

    /// <summary>Schema objects created for the current crest; destroyed on rebuild.</summary>
    private static Schema? _detailsSchema;
    private static Schema? _confirmSchema;
    private static ToolCrest? _builtFor;
    private static Sprite? _dotSprite;

    /// <summary>Native silhouette height of the tallest crest, world units; zero until measured.</summary>
    private static float _referenceHeight;

    private static bool _missingLogged;
    private static bool _confirmOrderLogged;

    /// <summary>Last <c>activeSelf</c> of the confirm group that was logged; null before the first log.</summary>
    private static bool? _confirmGroupActiveLogged;

    /// <summary>Bounds the details schema drew on the last frame it was visible.</summary>
    private static Bounds _detailsBounds;

    /// <summary>True once the details schema was hidden behind the confirm screen.</summary>
    private static bool _detailsHiddenLogged;

    /// <summary>True once the details schema was shown after a build.</summary>
    private static bool _detailsShownLogged;

    /// <summary>Last logged anchor of the visible confirmation text; null before the first log.</summary>
    private static string? _confirmAnchorLogged;

    /// <summary>
    /// True from the purchase that starts the vanilla confirmation until the window is
    /// restored. Gates the purchase flow only: what is drawn follows the confirmation
    /// text on screen (see <see cref="ConfirmVisualVisible"/>), because the game asks its
    /// question before any purchase runs.
    /// </summary>
    internal static bool ConfirmActive;

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

    /// <summary>True once the hide at the close start was logged for this session.</summary>
    private static bool _closeHiddenLogged;

    /// <summary>True once the schema y offset was logged for this window.</summary>
    private static bool _offsetLogged;

    /// <summary>True once the y offset clamp was logged for this window.</summary>
    private static bool _offsetClampedLogged;

    /// <summary>Last logged confirm fit scale; negative before the first fit.</summary>
    private static float _confirmFitLogged = -1f;

    /// <summary>Flags for the private fields read off the vanilla crest widgets.</summary>
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly FieldInfo? TemplateSlotsField =
        typeof(InventoryToolCrest).GetField("templateSlots", PrivateInstance);

    private static readonly FieldInfo? SlotTypeSpriteField =
        typeof(InventoryToolCrestSlot).GetField("slotTypeSprite", PrivateInstance);

    private static readonly FieldInfo? CrestSpriteField =
        typeof(InventoryToolCrest).GetField("crestSprite", PrivateInstance);

    private static readonly FieldInfo? CrestSilhouetteField =
        typeof(InventoryToolCrest).GetField("crestSilhouette", PrivateInstance);

    private static readonly FieldInfo? NewItemOrbField =
        typeof(InventoryToolCrest).GetField("newItemOrb", PrivateInstance);

    /// <summary>Vanilla slot sprites indexed by <see cref="ToolItemType"/>; read once per session.</summary>
    private static Sprite?[]? _templateSprites;

    /// <summary>Slot types whose dot sprite source was already logged.</summary>
    private static readonly HashSet<ToolItemType> DotLoggedTypes = new HashSet<ToolItemType>();

    private static bool _templateRead;
    private static bool _inventoryFound;
    private static bool _spritesLogged;

    /// <summary>Crests whose slots were already written to the log.</summary>
    private static readonly HashSet<string> SlotLogged = new HashSet<string>();

    /// <summary>Crests whose dot diameters were already written to the log.</summary>
    private static readonly HashSet<string> DotsLogged = new HashSet<string>();

    /// <summary>True once every crest of the game was dumped for this session.</summary>
    private static bool _crestDumpLogged;
    private static readonly HashSet<string> LoggedReasons = new HashSet<string>();

    /// <summary>True to draw the crest schema at all.</summary>
    internal static bool Enabled = true;

    /// <summary>How the schema body is drawn; the default clones the unselected vanilla card.</summary>
    internal static SchemaRenderMode Render = SchemaRenderMode.Clone;

    /// <summary>Multiplier applied to the schema size.</summary>
    internal static float Scale = 1f;

    /// <summary>Vertical shift of the details schema, as a fraction of the panel height; negative goes down.</summary>
    internal static float OffsetY = -0.12f;

    /// <summary>Dot diameter for an unlocked slot, as a fraction of the reference height.</summary>
    internal static float DotSize = 0.373f;

    /// <summary>Dot diameter for a locked slot, as a fraction of the reference height.</summary>
    internal static float LockedDotSize = 0.174f;

    /// <summary>Dot drop, as a fraction of the drawn silhouette height, in world units.</summary>
    internal static float DotOffsetY = -0.02f;

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
            _confirmThanks = null;
            _builtFor = null;
            _missingLogged = false;
            _detailsHiddenLogged = false;
            _detailsShownLogged = false;
            _confirmAnchorLogged = null;
            ConfirmActive = false;
            _closed = false;
            _closeHiddenLogged = false;
            _selectedIndex = -1;
            _pickSource = string.Empty;
            _pickIndex = -1;
            _pickName = string.Empty;
            _detailsCorrectedLogged = false;
            _confirmGroupActiveLogged = null;
            _detailsBounds = default;
            _offsetLogged = false;
            _offsetClampedLogged = false;
            DestroySchemaRoot();
            DumpAllCrests();
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
    internal static void Hide()
    {
        _closed = true;
        try
        {
            bool changed = false;
            changed |= HideContainer(_detailsSchema);
            changed |= HideContainer(_confirmSchema);
            if (!_closeHiddenLogged)
            {
                _closeHiddenLogged = true;
                Plugin.Log.LogInfo("[SCHEMA] hidden at close start");
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
        _closeHiddenLogged = false;
        _root = null;
        _details = null;
        _nameLabel = null;
        _descLabel = null;
        _confirmGroup = null;
        _confirmMessage = null;
        _confirmThanks = null;
        _builtFor = null;
        _detailsHiddenLogged = false;
        _detailsShownLogged = false;
        _confirmAnchorLogged = null;
        ConfirmActive = false;
        _confirmGroupActiveLogged = null;
        _detailsBounds = default;
        _offsetLogged = false;
        _offsetClampedLogged = false;
        _confirmFitLogged = -1f;
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
    /// the confirm schema in the space above the confirmation text. Runs every frame
    /// because the panel's layout group keeps moving its children.
    /// </summary>
    private static void Place()
    {
        LogConfirmGroupState();
        FitPending();

        if (_detailsSchema == null || _details == null || !TryBounds(_details, out Bounds bounds))
        {
            return;
        }

        // A stale confirm group left active by the game must not keep the details hidden.
        if (!ConfirmActive)
        {
            if (!_detailsSchema.Container.activeSelf)
            {
                ShowDetails();
            }

            if (_detailsSchema.Container.activeSelf && !_detailsShownLogged)
            {
                _detailsShownLogged = true;
                Plugin.Log.LogInfo("[SCHEMA] details shown (confirm inactive)");
            }
        }

        float lowerCentreY = Mathf.Lerp(bounds.min.y, bounds.center.y, 0.5f);
        Vector3 target = new Vector3(bounds.center.x, lowerCentreY, bounds.center.z);
        if (_detailsSchema.Container.activeSelf)
        {
            float offsetWorld = OffsetY * bounds.size.y;
            if (!_offsetLogged && Mathf.Abs(offsetWorld) > 0.0001f)
            {
                _offsetLogged = true;
                Plugin.Log.LogInfo($"[SCHEMA] offsetY={OffsetY:F3} world={offsetWorld:F3}" +
                    $" ({Mathf.Abs(OffsetY):F2} of panel)");
            }

            PlaceAt(_detailsSchema, target, offsetWorld, bounds);
            _detailsBounds = ContainerBounds(_detailsSchema.Container.transform);
        }

        if (_confirmSchema == null)
        {
            ShowDetails();
            return;
        }

        string anchor = ConfirmVisualVisible(out Bounds anchorBounds);
        bool showing = anchor == AnchorQuestion || (anchor == AnchorThanks && ConfirmActive);
        if (_confirmSchema.Container.activeSelf != showing)
        {
            _confirmSchema.Container.SetActive(showing);
            Plugin.Log.LogInfo(showing
                ? "[SCHEMA] confirm visible"
                : $"[SCHEMA] confirm hidden ({ConfirmHiddenReason(anchor)})");
        }

        if (showing)
        {
            HideDetails();
            PlaceConfirm(_confirmSchema, anchorBounds, anchor);
            return;
        }

        ShowDetails();
        if (_detailsSchema.Container.activeSelf && !_detailsShownLogged)
        {
            _detailsShownLogged = true;
            Plugin.Log.LogInfo("[SCHEMA] details shown (no confirm visual)");
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
            Plugin.Log.LogInfo("[SCHEMA] details hidden (confirm visual)");
        }
    }

    /// <summary>
    /// Puts our details schema back to the state it had before the confirm screen, or
    /// shows it when no hidden state was recorded.
    /// </summary>
    private static void ShowDetails()
    {
        if (_detailsSchema == null)
        {
            return;
        }

        GameObject go = _detailsSchema.Container;
        bool wanted = !ObjectStates.TryGetValue(go, out bool wasActive) || wasActive;
        if (go.activeSelf != wanted)
        {
            go.SetActive(wanted);
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

    /// <summary>
    /// Moves a schema in world space so its own drawn centre lands on <paramref name="target"/>,
    /// shifted down by <paramref name="offsetY"/>; with a <paramref name="panel"/> the shifted
    /// schema is kept inside the panel edges. The shift is measured against the drawn bounds on
    /// every call, so it never accumulates.
    /// </summary>
    /// <param name="schema">Schema to move.</param>
    /// <param name="target">World position the drawn centre should take.</param>
    /// <param name="offsetY">Extra world shift along Y; negative goes down.</param>
    /// <param name="panel">Panel the schema must stay inside, when known.</param>
    private static void PlaceAt(Schema schema, Vector3 target, float offsetY = 0f, Bounds? panel = null)
    {
        Transform container = schema.Container.transform;
        Bounds bounds = ContainerBounds(container);
        Vector3 shift = target - bounds.center;
        shift.z = 0f;
        shift.y += offsetY;

        if (panel.HasValue && Mathf.Abs(offsetY) > 0.0001f)
        {
            Bounds outer = panel.Value;
            float centre = bounds.center.y + shift.y;
            float half = bounds.extents.y;
            float min = outer.min.y + half;
            float max = outer.max.y - half;
            if (min <= max)
            {
                float clamped = Mathf.Clamp(centre, min, max);
                if (Mathf.Abs(clamped - centre) > 0.0001f)
                {
                    shift.y += clamped - centre;
                    if (!_offsetClampedLogged)
                    {
                        _offsetClampedLogged = true;
                        Plugin.Log.LogInfo("[SCHEMA] offsetY clamped");
                    }
                }
            }
        }

        if (shift.sqrMagnitude > 0.0000001f)
        {
            container.position += shift;
        }
    }

    /// <summary>
    /// Puts the confirm schema in the space between the confirmation text and the item
    /// name: the schema bottom sits <see cref="ConfirmGap"/> text heights over the top of
    /// <paramref name="message"/>, its top stays under the name, and the name shrinks the
    /// schema down to <see cref="ConfirmFitMin"/> when it leaves less room than that. With
    /// no room left even then the schema hugs the name. Only X is clamped, so nothing can
    /// push the schema back down onto the text. The position comes from the drawn bounds
    /// of the container itself, so no parent scale chain is assumed.
    /// </summary>
    /// <param name="schema">Confirm schema to place.</param>
    /// <param name="message">World bounds of the visible confirmation text.</param>
    /// <param name="anchor">Name of that text, for the log line.</param>
    private static void PlaceConfirm(Schema schema, Bounds message, string anchor)
    {
        if (_detailsSchema == null)
        {
            return;
        }

        Transform container = schema.Container.transform;
        Bounds detailsBounds = DetailsSchemaBounds();
        float wanted = detailsBounds.size.y * ConfirmScale;
        float gap = ConfirmGap * message.size.y;
        float bottom = message.max.y + gap;
        float nameBottom = ConfirmNameBottom();
        float room = float.NaN;
        float fit = 1f;
        if (wanted > 0.0001f && !float.IsNaN(nameBottom))
        {
            room = nameBottom - gap - bottom;
            fit = Mathf.Clamp(room / wanted, ConfirmFitMin, 1f);
        }

        float target = wanted * fit;
        if (target > 0.0001f && !float.IsNaN(room) && room < target)
        {
            // The name leaves less room than the smallest schema: hug the name instead.
            bottom = nameBottom - gap - target;
        }

        float current = ContainerBounds(container).size.y;
        Transform content = schema.Content.transform;
        if (current > 0.0001f && target > 0.0001f && Mathf.Abs(current - target) > target * 0.001f)
        {
            float factor = target / current;
            Vector3 local = content.localScale;
            content.localScale = new Vector3(local.x * factor, local.y * factor, local.z);
        }

        if (Mathf.Abs(fit - _confirmFitLogged) > 0.0001f)
        {
            _confirmFitLogged = fit;
            Plugin.Log.LogInfo($"[SCHEMA] confirm fit scale={fit:F3} room={room:F3}");
        }

        Bounds inner = ContainerBounds(container);
        Vector3 shift = new Vector3(
            detailsBounds.center.x - inner.center.x,
            bottom + inner.extents.y - inner.center.y,
            0f);
        if (shift.sqrMagnitude > 0.0000001f)
        {
            container.position += shift;
        }

        if (_root != null)
        {
            Transform? board = _root.Find("Window/Shop_Backboard");
            if (board != null && TryBounds(board, out Bounds outer))
            {
                Bounds placed = ContainerBounds(container);
                float dx = ClampAxis(placed.center.x, placed.extents.x, outer.min.x, outer.max.x);
                if (Mathf.Abs(dx) > 0.0001f)
                {
                    container.position += new Vector3(dx, 0f, 0f);
                }
            }
        }

        if (!schema.PlacedLogged)
        {
            schema.PlacedLogged = true;
            Bounds placed = ContainerBounds(container);
            Plugin.Log.LogInfo($"[SCHEMA] confirm placed y={placed.min.y:F3} msgTop={message.max.y:F3}" +
                $" nameBottom={nameBottom:F3} scale={fit:F3} anchor={anchor}");
        }
    }

    /// <summary>World bounds of a confirmation text that is shown with a non-empty string.</summary>
    private static bool TryTextBounds(TMP_Text? text, out Bounds bounds)
    {
        bounds = default;
        if (text == null || !text.gameObject.activeInHierarchy || string.IsNullOrEmpty(SafeText(text)))
        {
            return false;
        }

        Renderer? renderer = text.GetComponent<Renderer>();
        if (renderer == null || renderer.bounds.size.sqrMagnitude <= 0.0000001f)
        {
            return false;
        }

        bounds = renderer.bounds;
        return true;
    }

    /// <summary>Lowest point of the bought item name; NaN when no name is shown.</summary>
    private static float ConfirmNameBottom()
    {
        TMP_Text? name = _confirmGroup != null ? FindText(_confirmGroup, "Item name") : null;
        if (!TryTextBounds(name, out Bounds bounds) && !TryTextBounds(_nameLabel, out bounds))
        {
            return float.NaN;
        }

        return bounds.min.y;
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
    /// Anchor name of the confirmation text really on screen: <c>Thankyou</c> after a
    /// purchase, <c>ConfirmMsg</c> while the game asks its question, <c>none</c> when the
    /// group is inactive or both texts are hidden or empty. The group flag alone is a poor
    /// guard: the game leaves it active with stale text after a window closes, while the
    /// real confirmation texts are the ones the player reads.
    /// </summary>
    /// <param name="bounds">World bounds of that text; default when nothing is visible.</param>
    private static string ConfirmVisualVisible(out Bounds bounds)
    {
        string anchor = AnchorNone;
        bounds = default;
        if (_confirmGroup != null && _confirmGroup.gameObject.activeInHierarchy)
        {
            if (TryTextBounds(_confirmThanks, out bounds))
            {
                anchor = AnchorThanks;
            }
            else if (TryTextBounds(_confirmMessage, out bounds))
            {
                anchor = AnchorQuestion;
            }
        }

        if (_confirmAnchorLogged != anchor)
        {
            _confirmAnchorLogged = anchor;
            Plugin.Log.LogInfo($"[SCHEMA] confirm visual visible anchor={anchor}");
        }

        return anchor;
    }

    /// <summary>
    /// Empty when the confirm schema belongs on screen, otherwise why it does not. The
    /// question text always counts; the purchase line only once <see cref="ConfirmActive"/>
    /// is set, because the game leaves it active with stale text after a window closes.
    /// </summary>
    /// <param name="anchor">Result of <see cref="ConfirmVisualVisible"/>.</param>
    private static string ConfirmHiddenReason(string anchor)
    {
        if (!ConfirmActive)
        {
            return "confirm inactive";
        }

        if (anchor != AnchorNone)
        {
            return string.Empty;
        }

        if (_confirmGroup == null)
        {
            return "no group";
        }

        if (!_confirmGroup.gameObject.activeInHierarchy)
        {
            return "group inactive";
        }

        return _confirmMessage == null ? "no confirm msg" : "confirm text hidden";
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

        Sprite? silhouette = CrestCardLook.UseSilhouette(false) ? crest.CrestSilhouette : crest.CrestSprite;
        if (silhouette == null)
        {
            silhouette = crest.CrestSprite;
            Plugin.Log.LogWarning($"[SCHEMA] silhouette fallback '{crest.name}'");
        }

        if (silhouette == null || _details == null || _nameLabel == null)
        {
            Plugin.Log.LogWarning($"[SCHEMA] '{crest.name}' has no silhouette or sprite, schema skipped.");
            return;
        }

        LogSpriteMetrics(crest, silhouette);
        LogSlots(crest);

        float reference = ReferenceHeight();
        float panel = TryPanelSize(out Vector2 panelSize) ? PanelFill * panelSize.y : BaseHeight;
        float scale = reference > 0.0001f ? panel * Scale / reference : Scale;
        float body = ReferenceBody();
        float cardScale = body > 0.0001f ? panel * Scale / body : Scale;
        Plugin.Log.LogInfo($"[SCHEMA] scale={scale:F4} (reference={reference:F3} panel={panel:F3})" +
            $" cardScale={cardScale:F4} (body={body:F3})");

        Color bodyColor = InventoryToolCrest.DeselectedColor;
        _detailsSchema = BuildSchema(crest, silhouette, bodyColor, scale, cardScale, true,
            "Crest Schema");
        _confirmSchema = BuildSchema(crest, silhouette, bodyColor, scale, cardScale, false,
            "Confirm Crest Schema");
        int slots = crest.Slots?.Length ?? 0;
        Plugin.Log.LogInfo($"[SCHEMA] built '{crest.name}' slots={slots}" +
            $" slotScale={CrestCardLook.SlotScale(false):F2}");
    }

    /// <summary>Logs the raw metrics of the silhouette and of <c>CrestSprite</c>, once per build.</summary>
    private static void LogSpriteMetrics(ToolCrest crest, Sprite? silhouette)
    {
        Plugin.Log.LogInfo($"[SCHEMA] sprite '{crest.name}' sil={SpriteMetrics(silhouette)}" +
            $" | spr={SpriteMetrics(crest.CrestSprite)}");
    }

    /// <summary>Name, pixel rect, PPU, unit bounds and texture rect of one sprite.</summary>
    private static string SpriteMetrics(Sprite? sprite)
    {
        if (sprite == null)
        {
            return "none";
        }

        Bounds bounds = sprite.bounds;
        return $"{sprite.name} rect=({sprite.rect.width:F0},{sprite.rect.height:F0})" +
            $" ppu={sprite.pixelsPerUnit:F2}" +
            $" bounds=({bounds.size.x:F3},{bounds.size.y:F3})" +
            $" spriteBounds=({sprite.textureRect.width:F0},{sprite.textureRect.height:F0})";
    }

    /// <summary>Logs every slot of a crest once, with the prefab parent its widget sits under.</summary>
    private static void LogSlots(ToolCrest crest)
    {
        if (!SlotLogged.Add(crest.name ?? string.Empty))
        {
            return;
        }

        try
        {
            ToolCrest.SlotInfo[]? slots = crest.Slots;
            if (slots == null || slots.Length == 0)
            {
                Plugin.Log.LogInfo($"[SCHEMA] slot crest='{crest.name}' none");
                return;
            }

            InventoryToolCrestSlot[] widgets = TemplateSlots(crest);
            for (int i = 0; i < slots.Length; i++)
            {
                ToolCrest.SlotInfo slot = slots[i];
                Vector2 position = slot.Position;
                Transform? parent = i < widgets.Length && widgets[i] != null
                    ? widgets[i].transform.parent
                    : null;
                Plugin.Log.LogInfo($"[SCHEMA] slot i={i} type={slot.Type}" +
                    $" pos=({position.x:F2},{position.y:F2}) locked={IsLocked(crest, slot)}" +
                    $" parent='{(parent != null ? PathOf(parent) : "none")}'");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SCHEMA] slot log failed for '{crest.name}': {ex.Message}");
        }
    }

    /// <summary>Vanilla slot widgets of a crest's card, empty when no card of it is loaded.</summary>
    private static InventoryToolCrestSlot[] TemplateSlots(ToolCrest crest)
    {
        InventoryToolCrest? template = CrestTemplate(crest);
        return template != null && TemplateSlotsField?.GetValue(template) is InventoryToolCrestSlot[] slots
            ? slots
            : Array.Empty<InventoryToolCrestSlot>();
    }

    /// <summary>One line per crest of the game, once per session: the sprites the schema compares.</summary>
    private static void DumpAllCrests()
    {
        if (_crestDumpLogged)
        {
            return;
        }

        _crestDumpLogged = true;
        try
        {
            foreach (ToolCrest crest in ToolItemManager.GetAllCrests())
            {
                if (crest == null)
                {
                    continue;
                }

                int slots = crest.Slots?.Length ?? 0;
                Plugin.Log.LogInfo($"[SCHEMA] crest '{crest.name}' sil={RectPpu(crest.CrestSilhouette)}" +
                    $" spr={RectPpu(crest.CrestSprite)} slots={slots}");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SCHEMA] crest dump failed: {ex.Message}");
        }
    }

    /// <summary>Pixel rect and PPU of one sprite, <c>none</c> when it is absent.</summary>
    private static string RectPpu(Sprite? sprite)
    {
        return sprite == null
            ? "none"
            : $"({sprite.rect.width:F0},{sprite.rect.height:F0})ppu={sprite.pixelsPerUnit:F2}";
    }

    /// <summary>
    /// Creates one schema instance: the sprite fallback is drawn at <paramref name="scale"/>,
    /// the cloned vanilla card at the constant <paramref name="cardScale"/>, which already
    /// covers the silhouette child scale. Neither depends on the selected crest, so a taller
    /// silhouette simply draws taller instead of being fitted per crest. The container stays
    /// at scale 1 and the content carries the scale divided by the inherited one.
    /// </summary>
    private static Schema? BuildSchema(ToolCrest crest, Sprite silhouette, Color bodyColor,
        float scale, float cardScale, bool fitDetails, string label)
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

        GameObject container = new GameObject(label);
        container.transform.SetParent(host, false);
        container.transform.localScale = Vector3.one;

        GameObject content = new GameObject(ContentName);
        content.transform.SetParent(container.transform, false);
        float inherited = Mathf.Abs(container.transform.lossyScale.y);
        float local = inherited > 0.0001f ? scale / inherited : scale;
        content.transform.localScale = new Vector3(local, local, 1f);
        LogContainer(container.transform);

        if (Render == SchemaRenderMode.Clone)
        {
            // The copied card draws the vanilla silhouette child at SilhouetteScale;
            // the card scale is the same for every crest, so only the body differs.
            float cardLocal = inherited > 0.0001f ? cardScale / inherited : cardScale;
            content.transform.localScale = new Vector3(cardLocal, cardLocal, 1f);
            Schema? clone = CloneSchema(crest, container, content, label);
            if (clone != null)
            {
                LiftConfirm(clone, fitDetails);
                LogSprites("clone");
                return clone;
            }

            // The sprite fallback still draws at the silhouette scale.
            content.transform.localScale = new Vector3(local, local, 1f);
        }

        bool vanilla = Render != SchemaRenderMode.Generated && TemplateSprites() != null;
        SpriteRenderer body = AddRenderer(content, "Schema Silhouette", silhouette, reference, 0);
        body.color = bodyColor;

        Schema schema = new Schema(label, container, content, body);
        AddDots(schema, crest, reference);
        schema.DotsTemplate = DotsFromTemplate(crest);
        Fit(schema);
        LiftConfirm(schema, fitDetails);
        LogSprites(vanilla ? "vanilla" : "generated");
        return schema;
    }

    /// <summary>
    /// Applies the dot drop once the silhouette draws a real height. The dot diameter and
    /// the container scale are fixed at build time, so nothing here is measured against
    /// the selected crest and every crest keeps the same scale.
    /// </summary>
    private static void Fit(Schema schema)
    {
        float h2 = WorldHeight(schema.Body);
        if (h2 <= 0.0001f)
        {
            return;
        }

        ApplyDotOffset(schema, h2);

        if (!schema.BoundsLogged)
        {
            schema.BoundsLogged = true;
            Bounds bounds = schema.Body.bounds;
            Transform content = schema.Content.transform;
            Plugin.Log.LogInfo($"[SCHEMA] {schema.Label} silhouette" +
                $" bounds={bounds.size.x:F3}x{bounds.size.y:F3}x{bounds.size.z:F3}" +
                $" h2={h2:F3} lossyScale={content.lossyScale.y:F3}" +
                $" contentScale={content.localScale.x:F3}");
        }
    }

    /// <summary>
    /// Drops every dot by <see cref="DotOffsetY"/> times the drawn height, once. The shift
    /// is built in world space so no container orientation can flip the direction.
    /// </summary>
    private static void ApplyDotOffset(Schema schema, float h2)
    {
        if (schema.OffsetApplied || schema.BasePositions.Count != schema.Dots.Count)
        {
            return;
        }

        schema.OffsetApplied = true;
        Vector3 world = new Vector3(0f, DotOffsetY * h2, 0f);
        Vector3 local = schema.Content.transform.InverseTransformVector(world);
        for (int i = 0; i < schema.Dots.Count; i++)
        {
            schema.Dots[i].transform.localPosition = schema.BasePositions[i] + local;
        }
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

    /// <summary>
    /// Native silhouette height of the tallest crest, measured once per plugin session:
    /// the reference the sprite-fallback scale and the dot diameter derive from.
    /// </summary>
    private static float ReferenceHeight()
    {
        if (_referenceHeight > 0.0001f)
        {
            return _referenceHeight;
        }

        float best = 0f;
        string name = string.Empty;
        try
        {
            foreach (ToolCrest crest in ToolItemManager.GetAllCrests())
            {
                if (crest == null)
                {
                    continue;
                }

                float height = NativeHeight(crest.CrestSilhouette);
                if (height > best)
                {
                    best = height;
                    name = crest.name ?? string.Empty;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SCHEMA] reference height failed: {ex.Message}");
        }

        if (best <= 0.0001f)
        {
            return 0f;
        }

        _referenceHeight = best;
        Plugin.Log.LogInfo($"[SCHEMA] reference height={best:F3} from '{name}'");
        return best;
    }

    /// <summary>
    /// Body height of the tallest crest as a cloned card draws it: its native silhouette at
    /// the vanilla <see cref="SilhouetteScale"/>. One reference for every clone, so the card
    /// scale never depends on the selected crest.
    /// </summary>
    private static float ReferenceBody()
    {
        return ReferenceHeight() * SilhouetteScale;
    }

    /// <summary>World height a sprite draws at scale 1; <c>rect/ppu</c> when bounds are empty.</summary>
    private static float NativeHeight(Sprite? sprite)
    {
        if (sprite == null)
        {
            return 0f;
        }

        float bounds = sprite.bounds.size.y;
        return bounds > 0.0001f ? bounds : SpriteHeight(sprite);
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
        if (Render != SchemaRenderMode.Generated)
        {
            Sprite?[]? sprites = TemplateSprites();
            int index = (int)type;
            if (sprites != null && index >= 0 && index < sprites.Length && sprites[index] != null)
            {
                LogDot(type, "template");
                return sprites[index];
            }
        }

        LogDot(type, "generated");
        return DotSprite();
    }

    /// <summary>Logs the sprite source of one slot type once per session.</summary>
    private static void LogDot(ToolItemType type, string sprite)
    {
        if (DotLoggedTypes.Add(type))
        {
            Plugin.Log.LogInfo($"[SCHEMA] dot type={type} sprite={sprite}");
        }
    }

    /// <summary>True when every slot type has a vanilla template sprite.</summary>
    private static bool DotsFromTemplate(ToolCrest crest)
    {
        ToolCrest.SlotInfo[]? slots = crest.Slots;
        if (slots == null || slots.Length == 0)
        {
            return true;
        }

        Sprite?[]? sprites = Render == SchemaRenderMode.Generated ? null : TemplateSprites();
        for (int i = 0; i < slots.Length; i++)
        {
            int index = (int)slots[i].Type;
            if (sprites == null || index < 0 || index >= sprites.Length || sprites[index] == null)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Copies the vanilla crest card of <paramref name="crest"/>, forced into the look of an
    /// unselected card: hidden crest sprite, shown silhouette and shrunk slot icons.
    /// Null when no crest card is in the scene.
    /// </summary>
    private static Schema? CloneSchema(ToolCrest crest, GameObject container, GameObject content,
        string label)
    {
        try
        {
            InventoryToolCrest? source = CloneTemplate(crest);
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

            InventoryToolCrest? widget = clone.GetComponent<InventoryToolCrest>();
            Transform? silhouette = widget != null &&
                CrestSilhouetteField?.GetValue(widget) is NestedFadeGroupSpriteRenderer silhouettePart
                ? silhouettePart.transform
                : null;
            List<GameObject> disabled = new List<GameObject>();
            StripEffects(clone.transform, clone.transform, silhouette, disabled);
            int extras = disabled.Count;
            if (extras > 0)
            {
                List<string> names = disabled.ConvertAll(part => part.name);
                LogOnce($"[SCHEMA] clone '{clone.name}' disabled extras='{string.Join(", ", names)}'");
            }

            // The vanilla components drive themselves from a manager the clone does not have.
            InventoryToolCrestSlot[] slots = clone.GetComponentsInChildren<InventoryToolCrestSlot>(true);
            foreach (InventoryToolCrestSlot slot in slots)
            {
                slot.enabled = false;
                slot.transform.localScale = new Vector3(CloneSlotScale, CloneSlotScale, 1f);
            }

            string silhouetteName = "none";
            string crestSpriteName = "none";
            if (widget != null)
            {
                Unselect(widget);
                silhouetteName = PartName(CrestSilhouetteField?.GetValue(widget));
                crestSpriteName = PartName(CrestSpriteField?.GetValue(widget));
                RemoveNewItem(clone, widget);
                widget.enabled = false;
                UnityEngine.Object.Destroy(widget);
            }

            clone.SetActive(true);
            Renderer? body = SilhouetteRenderer(clone.transform);
            if (body == null)
            {
                Plugin.Log.LogWarning($"[SCHEMA] '{label}' clone has no silhouette to draw, schema skipped.");
                UnityEngine.Object.Destroy(clone);
                return null;
            }

            ResetFades(clone, body.transform);
            foreach (InventoryToolCrestSlot slot in slots)
            {
                slot.ItemFlashAmount = 0f;
                UnityEngine.Object.Destroy(slot);
            }

            Schema schema = new Schema(label, container, content, body);
            Fit(schema);
            Plugin.Log.LogInfo($"[SCHEMA] clone '{crest.name}' silhouette='{silhouetteName}'" +
                $" crestSprite='{crestSpriteName}' extras={extras} slots={slots.Length}");
            return schema;
        }
        catch (Exception ex)
        {
            LogOnce($"[SCHEMA] clone failed: {ex.Message}, sprite fallback");
            return null;
        }
    }

    /// <summary>
    /// Forced unselected view of a vanilla crest card: the crest sprite object is switched
    /// off, because its fade group rewrites the sprite alpha every frame and alpha alone
    /// let the detailed sprite reappear. The silhouette is switched on at full alpha.
    /// </summary>
    private static void Unselect(InventoryToolCrest widget)
    {
        if (CrestSpriteField?.GetValue(widget) is NestedFadeGroupSpriteRenderer sprite)
        {
            sprite.gameObject.SetActive(false);
        }
        else
        {
            LogOnce("[SCHEMA] crestSprite field missing, clone sprite stays visible");
        }

        if (CrestSilhouetteField?.GetValue(widget) is NestedFadeGroupSpriteRenderer silhouette)
        {
            silhouette.gameObject.SetActive(true);
            silhouette.AlphaSelf = 1f;
            Color color = silhouette.Color;
            silhouette.Color = new Color(color.r, color.g, color.b, 1f);
        }
        else
        {
            LogOnce("[SCHEMA] crestSilhouette field missing, clone silhouette unchanged");
        }
    }

    /// <summary>
    /// Switches off the effect nodes of a cloned card, decided by
    /// <see cref="CloneFilter.Evaluate"/>: the glow and the white flash draw over the
    /// silhouette, and only the vanilla fade kept them invisible. The silhouette subtree
    /// and the slot widgets always stay. Each switched-off object is added to
    /// <paramref name="disabled"/>.
    /// </summary>
    /// <param name="parent">Subtree being walked.</param>
    /// <param name="clone">Root of the clone, the boundary of the rule.</param>
    /// <param name="silhouette">Silhouette node of the clone, when it has one.</param>
    /// <param name="disabled">Objects switched off by this pass.</param>
    private static void StripEffects(Transform parent, Transform clone, Transform? silhouette,
        List<GameObject> disabled)
    {
        foreach (Transform child in parent)
        {
            bool isSilhouette = silhouette != null &&
                (child == silhouette || child.IsChildOf(silhouette));
            if (CloneFilter.Evaluate(child.name, isSilhouette, InSlot(child, clone)) == CloneNode.Disable)
            {
                child.gameObject.SetActive(false);
                disabled.Add(child.gameObject);
                continue;
            }

            StripEffects(child, clone, silhouette, disabled);
        }
    }

    /// <summary>True when <paramref name="part"/> belongs to a slot of the card.</summary>
    private static bool InSlot(Transform part, Transform clone)
    {
        for (Transform? current = part; current != null && current != clone; current = current.parent)
        {
            if (current.GetComponent<InventoryToolCrestSlot>() != null ||
                current.name.IndexOf("Slot", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Sprite name of a crest card part for the clone log: <c>off</c> when the part was
    /// switched off, <c>none</c> when the field or the sprite is missing.
    /// </summary>
    private static string PartName(object? part)
    {
        if (part is not NestedFadeGroupSpriteRenderer renderer)
        {
            return "none";
        }

        if (!renderer.gameObject.activeSelf)
        {
            return "off";
        }

        return renderer.Sprite != null ? renderer.Sprite.name : "none";
    }

    /// <summary>Removes the "new item" orb the vanilla card shows for an unseen crest.</summary>
    private static void RemoveNewItem(GameObject clone, InventoryToolCrest widget)
    {
        object? orb = NewItemOrbField?.GetValue(widget);
        if (orb is GameObject go)
        {
            UnityEngine.Object.Destroy(go);
        }
        else if (orb is Component part)
        {
            UnityEngine.Object.Destroy(part.gameObject);
        }

        foreach (Transform child in clone.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.IndexOf("newitem", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }
    }

    /// <summary>Card of <paramref name="crest"/>; any card of the scene as a logged fallback.</summary>
    private static InventoryToolCrest? CloneTemplate(ToolCrest crest)
    {
        InventoryToolCrest? exact = CrestTemplate(crest);
        if (exact != null)
        {
            return exact;
        }

        InventoryToolCrest? any = LiveCrest();
        if (any != null)
        {
            LogOnce($"[SCHEMA] clone template fallback '{any.name}' for '{crest.name}'");
        }

        return any;
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
            if (!go.scene.isLoaded || go.hideFlags != HideFlags.None ||
                (candidate.CrestData != crest && candidate.name != crest.name))
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

    /// <summary>
    /// Raises the fade groups the silhouette sits in: the vanilla card fades as a whole, so
    /// every group between the silhouette and the clone root must be at full alpha. The
    /// other groups keep the alpha the vanilla fade left, which is what hides the effects.
    /// </summary>
    private static void ResetFades(GameObject clone, Transform silhouette)
    {
        for (Transform? current = silhouette; current != null; current = current.parent)
        {
            NestedFadeGroupBase? group = current.GetComponent<NestedFadeGroupBase>();
            if (group != null)
            {
                group.AlphaSelf = 1f;
            }

            if (current == clone.transform)
            {
                break;
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
            _confirmMessage = FindText(_confirmGroup, ConfirmMessageName);
            _confirmThanks = FindText(_confirmGroup, PurchaseMessageName);
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
    /// silhouette-local units and stay as they are; the diameter is the card slot scale
    /// times the reference height times the unlocked or locked dot fraction, converted to
    /// local scale once.
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
        float card = CrestCardLook.SlotScale(false) *
            (_referenceHeight > 0.0001f ? _referenceHeight : 1f);
        bool logDots = DotsLogged.Add(crest.name ?? string.Empty);
        for (int i = 0; i < slots.Length; i++)
        {
            ToolCrest.SlotInfo slot = slots[i];
            Sprite? dot = DotSprite(slot.Type);
            if (dot == null)
            {
                continue;
            }

            bool locked = IsLocked(crest, slot);
            Color typeColor = TypeColor(slot.Type);
            float diameter = card * (locked ? LockedDotSize : DotSize);
            float native = SpriteHeight(dot);
            if (logDots)
            {
                Plugin.Log.LogInfo($"[SCHEMA] dot diameter={diameter:F3} world" +
                    $" locked={locked} native={native:F3}");
            }

            float approx = native > 0.0001f && lossy > 0.0001f
                ? diameter / (native * lossy)
                : 1f;
            SpriteRenderer renderer = AddRenderer(schema.Content, $"Schema Dot {i}", dot, reference, 1);
            Vector2 position = slot.Position;
            Vector3 local = new Vector3(position.x, position.y, 0f);
            renderer.transform.localPosition = local;
            renderer.transform.localScale = new Vector3(approx, approx, 1f);
            renderer.color = locked ? LockedColor(typeColor) : typeColor;
            schema.Dots.Add(renderer);
            schema.BasePositions.Add(local);
        }
    }

    /// <summary>Vanilla lock rule: the slot is locked until the crest itself is unlocked.</summary>
    private static bool IsLocked(ToolCrest crest, ToolCrest.SlotInfo slot)
    {
        if (!slot.IsLocked)
        {
            return false;
        }

        try
        {
            return !crest.SaveData.IsUnlocked;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>Vanilla locked-slot tint: the type value without saturation.</summary>
    private static Color LockedColor(Color color)
    {
        Color.RGBToHSV(color, out float h, out _, out float v);
        return Color.HSVToRGB(h, 0f, v);
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
        internal Schema(string label, GameObject container, GameObject content, Renderer body)
        {
            Label = label;
            Container = container;
            Content = content;
            Body = body;
        }

        internal string Label { get; }

        internal GameObject Container { get; }

        /// <summary>Child that carries the uniform scale; the container itself stays at scale 1.</summary>
        internal GameObject Content { get; }

        internal Renderer Body { get; }

        /// <summary>True once the confirm placement numbers were logged.</summary>
        internal bool PlacedLogged { get; set; }

        internal List<SpriteRenderer> Dots { get; } = new List<SpriteRenderer>();

        /// <summary>Schema-local dot positions as built, before the offset is applied.</summary>
        internal List<Vector3> BasePositions { get; } = new List<Vector3>();

        /// <summary>True once the dots took the <see cref="DotOffsetY"/> shift.</summary>
        internal bool OffsetApplied { get; set; }

        /// <summary>True when every dot used the vanilla template sprite.</summary>
        internal bool DotsTemplate { get; set; } = true;

        /// <summary>True once the silhouette bounds were logged for this schema.</summary>
        internal bool BoundsLogged { get; set; }
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
