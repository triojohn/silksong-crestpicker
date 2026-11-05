using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Render-side diagnostic of the live shop window: cameras, layer masks, screen size,
/// panel projection and the renderers of the shop tree. The only state it changes is
/// the viewport recentre, which moves the window like <see cref="ShopCrestMenu"/> does.
/// Never throws.
/// </summary>
internal static class ShopRenderDiag
{
    /// <summary>Upper bound on renderers dumped per capture, so the log stays readable.</summary>
    private const int MaxRenderers = 40;

    /// <summary>
    /// Logs cameras, screen, layers, renderers and the panel projection, then applies
    /// the HUD-viewport recentre and logs it again.
    /// </summary>
    /// <param name="reason">What triggered the capture, for the log lines.</param>
    /// <param name="root">Shop window root, resolved from the scene when null.</param>
    internal static void Capture(string reason, Transform? root)
    {
        try
        {
            Transform? shopRoot = root != null ? root : LiveRoot();
            Plugin.Log.LogInfo($"[RENDER] === capture ({reason}) root='{(shopRoot != null ? Path(shopRoot) : "<none>")}' ===");
            LogScreen();
            LogCameras(reason, shopRoot);
            LogRenderers(reason, shopRoot);
            LogLayerCoverage(reason, shopRoot);

            if (ShopCrestMenu.RecenterIntoViewport())
            {
                LogPanel(reason + " after recentre", shopRoot);
                LogVisibleSummary(reason + " after recentre", shopRoot);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[RENDER] capture failed: {ex.Message}");
        }
    }

    /// <summary>Logs screen size and the two layers the shop could sit on.</summary>
    private static void LogScreen()
    {
        Plugin.Log.LogInfo(
            $"[RENDER] screen={Screen.width}x{Screen.height}" +
            $" layer5='{LayerMask.LayerToName(5)}' layer13='{LayerMask.LayerToName(13)}'");
    }

    /// <summary>Logs every candidate camera with its mask and the panel projection.</summary>
    private static void LogCameras(string reason, Transform? shopRoot)
    {
        Transform? panel = Panel(shopRoot);
        Vector3 centre = panel != null ? panel.position : Vector3.zero;
        GameCameras? cams = GameCameras.SilentInstance;
        Camera? main = Camera.main;
        Camera? hud = cams != null ? cams.hudCamera : null;
        Camera? world = cams != null ? cams.mainCamera : null;

        LogCamera("Camera.main", main, centre, panel != null);
        LogCamera("hudCamera", hud, centre, panel != null);
        LogCamera("mainCamera", world, centre, panel != null);

        if (cams == null)
        {
            Plugin.Log.LogWarning("[RENDER] GameCameras.SilentInstance is null: hudCamera and mainCamera unavailable.");
        }
        else if (hud == null)
        {
            Plugin.Log.LogWarning("[RENDER] GameCameras.hudCamera is null: viewport recentre falls back to Camera.main.");
        }

        if (panel == null)
        {
            Plugin.Log.LogInfo($"[RENDER] {reason}: no panel transform, projection skipped.");
        }
    }

    /// <summary>Logs one camera: identity, projection, mask as number and layer names.</summary>
    private static void LogCamera(string label, Camera? cam, Vector3 centre, bool hasPanel)
    {
        if (cam == null)
        {
            Plugin.Log.LogInfo($"[RENDER] {label} = <null>");
            return;
        }

        Plugin.Log.LogInfo(
            $"[RENDER] {label} '{cam.name}' enabled={cam.enabled}" +
            $" active={cam.gameObject.activeInHierarchy} ortho={cam.orthographic}" +
            $" size={cam.orthographicSize:F2} depth={cam.depth:F1}" +
            $" pos={Vec(cam.transform.position)} mask={cam.cullingMask}" +
            $" layers=[{LayerNames(cam.cullingMask)}]");

        if (!hasPanel)
        {
            return;
        }

        Vector3 viewport = cam.WorldToViewportPoint(centre);
        Vector3 screen = cam.WorldToScreenPoint(centre);
        Plugin.Log.LogInfo(
            $"[RENDER] {label} panel vp={Vec(viewport)} sp={Vec(screen)} {Inside(viewport)}");
    }

    /// <summary>Logs the panel projection with each camera, after the recentre.</summary>
    private static void LogPanel(string reason, Transform? shopRoot)
    {
        Transform? panel = Panel(shopRoot);
        if (panel == null)
        {
            return;
        }

        GameCameras? cams = GameCameras.SilentInstance;
        Camera? hud = cams != null ? cams.hudCamera : null;
        Camera? main = Camera.main;
        LogCamera("Camera.main", main, panel.position, true);
        LogCamera("hudCamera", hud, panel.position, true);
        Plugin.Log.LogInfo($"[RENDER] {reason}: panel world={Vec(panel.position)}");
    }

    /// <summary>Logs each renderer: flags, sorting, world bounds, sprite and alpha.</summary>
    private static void LogRenderers(string reason, Transform? shopRoot)
    {
        if (shopRoot == null)
        {
            Plugin.Log.LogInfo($"[RENDER] {reason}: no shop root, renderers skipped.");
            return;
        }

        Renderer[] renderers = shopRoot.GetComponentsInChildren<Renderer>(true);
        int shown = Math.Min(renderers.Length, MaxRenderers);
        Plugin.Log.LogInfo($"[RENDER] {reason}: renderers={renderers.Length}, showing {shown}");
        for (int i = 0; i < shown; i++)
        {
            LogRenderer(i, renderers[i]);
        }
    }

    /// <summary>Logs one renderer, with sprite name and alpha when it is a sprite.</summary>
    private static void LogRenderer(int index, Renderer? renderer)
    {
        if (renderer == null)
        {
            return;
        }

        Bounds bounds = renderer.bounds;
        string sprite = "-";
        string alpha = "-";
        if (renderer is SpriteRenderer spriteRenderer)
        {
            sprite = spriteRenderer.sprite != null ? spriteRenderer.sprite.name : "<null>";
            alpha = $"{spriteRenderer.color.a:F2}";
        }

        Plugin.Log.LogInfo(
            $"[RENDER]   r{index} '{Path(renderer.transform)}' enabled={renderer.enabled}" +
            $" visible={renderer.isVisible} layer={renderer.gameObject.layer}" +
            $" sortLayer='{renderer.sortingLayerName}' order={renderer.sortingOrder}" +
            $" bounds={Vec(bounds.center)} size={Vec(bounds.size)}" +
            $" sprite='{sprite}' a={alpha}");
    }

    /// <summary>Count of renderers that are enabled and reported visible, per capture.</summary>
    private static void LogVisibleSummary(string reason, Transform? shopRoot)
    {
        if (shopRoot == null)
        {
            return;
        }

        int enabled = 0;
        int visible = 0;
        List<string> names = new List<string>();
        foreach (Renderer renderer in shopRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            enabled++;
            if (renderer.isVisible)
            {
                visible++;
                if (names.Count < 6)
                {
                    names.Add(renderer.name);
                }
            }
        }

        Plugin.Log.LogInfo(
            $"[RENDER] {reason}: enabled={enabled} visible={visible}" +
            $" first=[{string.Join(",", names)}] (isVisible is from the last render pass)");
    }

    /// <summary>For every layer used by the shop, whether each camera mask includes it.</summary>
    private static void LogLayerCoverage(string reason, Transform? shopRoot)
    {
        if (shopRoot == null)
        {
            return;
        }

        GameCameras? cams = GameCameras.SilentInstance;
        int hudMask = cams != null && cams.hudCamera != null ? cams.hudCamera.cullingMask : 0;
        int mainMask = cams != null && cams.mainCamera != null ? cams.mainCamera.cullingMask : 0;
        HashSet<int> seen = new HashSet<int>();
        foreach (Renderer renderer in shopRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            int layer = renderer.gameObject.layer;
            if (!seen.Add(layer))
            {
                continue;
            }

            Plugin.Log.LogInfo(
                $"[RENDER] {reason}: layer {layer} '{LayerMask.LayerToName(layer)}'" +
                $" hudCam={Has(hudMask, layer)} mainCam={Has(mainMask, layer)}");
        }
    }

    /// <summary>True when a culling mask contains the layer bit.</summary>
    private static bool Has(int mask, int layer)
    {
        return layer >= 0 && layer < 32 && (mask & (1 << layer)) != 0;
    }

    /// <summary>Names of every layer bit set in a culling mask.</summary>
    private static string LayerNames(int mask)
    {
        List<string> names = new List<string>();
        for (int layer = 0; layer < 32; layer++)
        {
            if (Has(mask, layer))
            {
                string name = LayerMask.LayerToName(layer);
                names.Add(name.Length > 0 ? name : layer.ToString());
            }
        }

        return names.Count == 0 ? "<none>" : string.Join(",", names);
    }

    /// <summary>Whether a viewport point is on screen and in front of the camera.</summary>
    private static string Inside(Vector3 viewport)
    {
        bool onScreen = viewport.x >= 0f && viewport.x <= 1f
            && viewport.y >= 0f && viewport.y <= 1f
            && viewport.z > 0f;
        return onScreen ? "inside" : "OUTSIDE";
    }

    /// <summary>Panel centre transform of the window.</summary>
    private static Transform? Panel(Transform? shopRoot)
    {
        if (shopRoot == null)
        {
            return null;
        }

        return shopRoot.Find("Window/Shop_Backboard") ?? shopRoot.Find("Window") ?? shopRoot;
    }

    /// <summary>Shop window of the scene, even while it is hidden.</summary>
    private static Transform? LiveRoot()
    {
        try
        {
            foreach (ShopOwnerBase owner in Resources.FindObjectsOfTypeAll<ShopOwnerBase>())
            {
                if (owner != null && owner.gameObject.scene.IsValid() && owner.gameObject.activeInHierarchy)
                {
                    GameObject? shop = owner.ShopObject;
                    return shop != null ? shop.transform : null;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[RENDER] live shop lookup failed: {ex.Message}");
        }

        return null;
    }

    private static string Path(Transform transform)
    {
        StringBuilder builder = new StringBuilder(transform.name);
        Transform current = transform.parent;
        while (current != null && builder.Length < 160)
        {
            builder.Insert(0, current.name + "/");
            current = current.parent;
        }

        return builder.ToString();
    }

    private static string Vec(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }
}
