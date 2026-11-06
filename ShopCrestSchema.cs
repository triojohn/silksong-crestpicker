using System;
using System.Collections.Generic;
using System.Reflection;
using GlobalSettings;
using TMProOld;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Cosmetic pass over the shop details panel while our crests are shown: the crest
/// name label repeats the row label, so it is hidden, and a crest schema takes its
/// place - the monochrome silhouette plus one coloured dot per slot. Every created
/// object is destroyed and every hidden object restored by <see cref="End"/>.
/// Never throws.
/// </summary>
internal static class ShopCrestSchema
{
    /// <summary>Schema height in world units before <see cref="Scale"/> is applied.</summary>
    private const float BaseHeight = 1.2f;

    /// <summary>Dot diameter factor for a locked slot.</summary>
    private const float LockedDotFactor = 0.8f;

    /// <summary>Dot alpha for a locked slot.</summary>
    private const float LockedAlpha = 0.5f;

    /// <summary>Dot fits tried per frame before the next frame takes over.</summary>
    private const int MaxDotAttempts = 3;

    /// <summary>Relative error a dot fit accepts as done.</summary>
    private const float DotTolerance = 0.05f;

    /// <summary>Path of the details panel inside the shop window.</summary>
    private const string DetailsPath = "Item List Group/Item Details";

    /// <summary>Confirm group holding the item picture the confirm schema is centred on.</summary>
    private const string ConfirmGroupPath = "Item Confirm Group";

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

    private static Transform? _root;
    private static Transform? _details;
    private static TMP_Text? _nameLabel;
    private static Transform? _confirmGroup;
    private static Transform? _confirmAnchor;
    private static TMP_Text? _confirmMessage;

    /// <summary>Schema objects created for the current crest; destroyed on rebuild.</summary>
    private static Schema? _detailsSchema;
    private static Schema? _confirmSchema;
    private static ToolCrest? _builtFor;
    private static Sprite? _dotSprite;
    private static bool _missingLogged;

    /// <summary>True to draw the crest schema at all.</summary>
    internal static bool Enabled = true;

    /// <summary>Multiplier applied to the schema size.</summary>
    internal static float Scale = 1f;

    /// <summary>Multiplier for the slot dot diameter, as a fraction of the schema height.</summary>
    internal static float DotSize = 0.11f;

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
            _confirmGroup = null;
            _confirmAnchor = null;
            _confirmMessage = null;
            _builtFor = null;
            _missingLogged = false;
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

    /// <summary>Destroys the schema objects and puts the hidden name back.</summary>
    internal static void End()
    {
        _root = null;
        _details = null;
        _nameLabel = null;
        _confirmGroup = null;
        _confirmAnchor = null;
        _confirmMessage = null;
        _builtFor = null;
        try
        {
            DestroySchema();
            foreach (KeyValuePair<GameObject, bool> pair in ObjectStates)
            {
                if (pair.Key != null)
                {
                    pair.Key.SetActive(pair.Value);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SCHEMA] restore failed: {ex.Message}");
        }
        finally
        {
            ObjectStates.Clear();
        }
    }

    /// <summary>Hides the duplicate name and keeps the schema in step with the selection.</summary>
    private static void Apply()
    {
        if (!ResolveDetails())
        {
            return;
        }

        HideName();
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
        FitPending();

        if (_detailsSchema != null && _details != null && TryBounds(_details, out Bounds bounds))
        {
            float lowerCentreY = Mathf.Lerp(bounds.min.y, bounds.center.y, 0.5f);
            Vector3 target = new Vector3(bounds.center.x, lowerCentreY, bounds.center.z);
            Transform transform = _detailsSchema.Container.transform;
            if (transform.position != target)
            {
                transform.position = target;
            }
        }

        if (_confirmSchema == null)
        {
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

        if (showing && _confirmAnchor != null)
        {
            Vector3 target = _confirmAnchor.position;
            Transform transform = _confirmSchema.Container.transform;
            if (transform.position != target)
            {
                transform.position = target;
            }
        }
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
    /// text the game actually shows, not the bare group activity.
    /// </summary>
    private static string ConfirmHiddenReason()
    {
        if (_confirmGroup == null)
        {
            return "no group";
        }

        if (!_confirmGroup.gameObject.activeInHierarchy)
        {
            return "group inactive";
        }

        if (_confirmMessage == null)
        {
            return "no confirm msg";
        }

        if (!_confirmMessage.gameObject.activeInHierarchy)
        {
            return "confirm msg inactive";
        }

        return string.IsNullOrEmpty(SafeText(_confirmMessage)) ? "confirm msg empty" : string.Empty;
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
        if (_details == null || _nameLabel == null)
        {
            _details = null;
            _nameLabel = null;
            if (!_missingLogged)
            {
                _missingLogged = true;
                Plugin.Log.LogWarning($"[SCHEMA] details panel or 'Item name' not found under '{ShopCrestMenu.Path(_root)}'.");
            }

            return false;
        }

        return true;
    }

    /// <summary>Hides the details name label, remembering its vanilla state once.</summary>
    private static void HideName()
    {
        GameObject? go = _nameLabel != null ? _nameLabel.gameObject : null;
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
    /// Crest the details panel currently describes: the selected row's name text is
    /// compared with every crest shown in the window, description text as a fallback.
    /// </summary>
    private static ToolCrest? SelectedCrest()
    {
        if (_nameLabel == null || _root == null)
        {
            return null;
        }

        string? name = SafeText(_nameLabel);
        ToolCrest? byName = Match(name, true);
        if (byName != null)
        {
            return byName;
        }

        TMP_Text? desc = _details != null ? FindText(_details, "Item desc") : null;
        return Match(desc != null ? SafeText(desc) : null, false);
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

        _detailsSchema = BuildSchema(crest, silhouette, darkened, DetailsParent(), 1f, "Crest Schema");
        _confirmSchema = BuildSchema(crest, silhouette, darkened, ConfirmGroup(), ConfirmScale,
            "Confirm Crest Schema");
    }

    /// <summary>
    /// Creates one schema instance at container scale 1 and fits it to
    /// <see cref="BaseHeight"/> times <paramref name="sizeFactor"/> world units from the
    /// height actually drawn, so no assumption about the parent scale chain is made.
    /// The container is parented outside the layout group and placed by the caller.
    /// </summary>
    private static Schema? BuildSchema(ToolCrest crest, Sprite silhouette, bool darkened,
        Transform? parent, float sizeFactor, string label)
    {
        if (parent == null)
        {
            if (!_missingLogged)
            {
                _missingLogged = true;
                Plugin.Log.LogWarning($"[SCHEMA] '{label}' parent not found, schema skipped.");
            }

            return null;
        }

        SpriteRenderer? reference = ReferenceRenderer() ?? BackboardRenderer();
        float targetH = BaseHeight * Scale * sizeFactor;

        GameObject container = new GameObject(label);
        // A plain Transform child: layout groups only lay out RectTransform children,
        // so this container keeps its hand-placed position.
        container.transform.SetParent(parent, false);
        container.transform.localScale = Vector3.one;
        container.transform.position = parent.position;

        SpriteRenderer body = AddRenderer(container, "Schema Silhouette", silhouette, reference, 0);
        body.color = darkened ? new Color(0.3f, 0.3f, 0.3f, 1f) : Color.white;

        Schema schema = new Schema(label, container, body, targetH);
        AddDots(schema, crest, reference);
        Fit(schema);
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

        if (!schema.BoundsLogged)
        {
            schema.BoundsLogged = true;
            Bounds bounds = schema.Body.bounds;
            Transform container = schema.Container.transform;
            Plugin.Log.LogInfo($"[SCHEMA] {schema.Label} silhouette" +
                $" bounds={bounds.size.x:F3}x{bounds.size.y:F3}x{bounds.size.z:F3}" +
                $" h2={h2:F3} targetH={schema.TargetHeight:F3}" +
                $" lossyScale={container.lossyScale.y:F3} containerScale={container.localScale.x:F3}");
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
            $" desiredDotWorld={wanted:F3} {first} dots={schema.Dots.Count} attempts={attempts}");
    }

    /// <summary>
    /// Scales the container to the wanted height once, when the silhouette reports a
    /// real height, and logs the numbers the dot fit depends on.
    /// </summary>
    private static bool FitContainer(Schema schema)
    {
        if (schema.ContainerFitted)
        {
            return true;
        }

        float measuredH = WorldHeight(schema.Body);
        if (measuredH <= 0.0001f)
        {
            return false;
        }

        float scaleApplied = schema.TargetHeight / measuredH;
        Transform transform = schema.Container.transform;
        transform.localScale *= scaleApplied;
        schema.ContainerFitted = true;
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

    /// <summary>Parent for the details schema: the details panel's own parent, not the layout group itself.</summary>
    private static Transform? DetailsParent()
    {
        if (_details == null)
        {
            return null;
        }

        return _details.parent != null ? _details.parent : _root;
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
            _confirmAnchor = picture != null ? picture : _confirmGroup;
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

        Sprite? dot = DotSprite();
        if (dot == null)
        {
            return;
        }

        float lossy = Mathf.Abs(schema.Container.transform.lossyScale.y);
        float approx = lossy > 0.0001f
            ? (schema.TargetHeight * DotSize) / (SpriteHeight(dot) * lossy)
            : 1f;
        for (int i = 0; i < slots.Length; i++)
        {
            ToolCrest.SlotInfo slot = slots[i];
            SpriteRenderer renderer = AddRenderer(schema.Container, $"Schema Dot {i}", dot, reference, 1);
            Vector2 position = slot.Position;
            renderer.transform.localPosition = new Vector3(position.x, position.y, 0f);
            renderer.transform.localScale = new Vector3(approx, approx, 1f);
            renderer.color = TypeColor(slot.Type);
            schema.Dots.Add(renderer);
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
        internal Schema(string label, GameObject container, SpriteRenderer body, float targetHeight)
        {
            Label = label;
            Container = container;
            Body = body;
            TargetHeight = targetHeight;
        }

        internal string Label { get; }

        internal GameObject Container { get; }

        internal SpriteRenderer Body { get; }

        /// <summary>Wanted world height of the silhouette.</summary>
        internal float TargetHeight { get; }

        internal List<SpriteRenderer> Dots { get; } = new List<SpriteRenderer>();

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

    /// <summary>Destroys the schema objects of the current crest, details and confirm alike.</summary>
    private static void DestroySchema()
    {
        if (_detailsSchema != null)
        {
            UnityEngine.Object.Destroy(_detailsSchema.Container);
            _detailsSchema = null;
        }

        if (_confirmSchema != null)
        {
            UnityEngine.Object.Destroy(_confirmSchema.Container);
            _confirmSchema = null;
        }
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
