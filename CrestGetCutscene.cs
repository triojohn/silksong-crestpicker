using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CrestPicker;

/// <summary>What the test key runs.</summary>
internal enum TestKeyMode
{
    /// <summary>Hero pickup clips plus the item-get reaction (the old path).</summary>
    Pickup,

    /// <summary>Probe that loads the crest shrine bundles and plays one clip.</summary>
    Cutscene,
}

/// <summary>
/// Probe for the vanilla crest-get shrine. Loads the shrine bundles straight from
/// StreamingAssets and plays one clip on a scratch sprite next to the hero. The scene
/// itself is not rebuilt yet: this only proves the clips load and animate.
/// </summary>
internal sealed class CrestGetCutscene : MonoBehaviour
{
    /// <summary>Clip played by the probe.</summary>
    private const string ClipName = "Stab Antic";

    /// <summary>Seconds the probe sprite stays alive.</summary>
    private const float Lifetime = 2f;

    /// <summary>Collection bundle that holds the shrine sprite collection.</summary>
    private const string BundleCollectionsShrine = "tk2dcollections_assets_crestshrine";

    /// <summary>Animation bundle that holds the shrine clip library.</summary>
    private const string BundleAnimationsShrine = "tk2danimations_assets_crestshrine";

    /// <summary>Collection dependency of the shrine clips.</summary>
    private const string BundleCollectionsWeaver = "tk2dcollections_assets_areaweaver";

    /// <summary>Hero collection dependency of the shrine clips.</summary>
    private const string BundleCollectionsHero = "herocollections_assets_shared";

    /// <summary>GameObject that carries the shrine sprite collection.</summary>
    private const string ObjectCollection = "Crest Get Cln";

    /// <summary>GameObject that carries the shrine clip library.</summary>
    private const string ObjectAnimation = "Crest Get Anim";

    private readonly List<AssetBundle> _bundles = new List<AssetBundle>();

    /// <summary>
    /// Starts the probe on the hero object. A second call while one is running is
    /// ignored, so a repeated key press cannot stack sprites.
    /// </summary>
    internal static void Run()
    {
        try
        {
            HeroController? hero = HeroController.instance;
            if (hero == null)
            {
                Plugin.Log.LogWarning("[CUT] hero unavailable");
                return;
            }

            if (hero.GetComponent<CrestGetCutscene>() != null)
            {
                Plugin.Log.LogInfo("[CUT] probe already running");
                return;
            }

            CrestGetCutscene probe = hero.gameObject.AddComponent<CrestGetCutscene>();
            probe.StartCoroutine(probe.GuardedPlay());
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[CUT] failed at 'start': {ex.Message}");
        }
    }

    /// <summary>Loads the bundles, plays the clip, then cleans up on every path.</summary>
    private IEnumerator Play()
    {
        GameObject? probe = null;
        try
        {
            string dir = Path.Combine(Application.dataPath, "StreamingAssets", "aa",
                "StandaloneWindows64");
            Plugin.Log.LogInfo($"[CUT] bundle dir '{dir}'");

            AssetBundle? collections = Load(dir, BundleCollectionsShrine);
            if (collections == null)
            {
                Plugin.Log.LogWarning("[CUT] failed at 'load collection bundle': bundle unavailable");
                yield break;
            }

            Load(dir, BundleCollectionsWeaver);
            Load(dir, BundleCollectionsHero);

            AssetBundle? animations = Load(dir, BundleAnimationsShrine);
            if (animations == null)
            {
                Plugin.Log.LogWarning("[CUT] failed at 'load animation bundle': bundle unavailable");
                yield break;
            }

            tk2dSpriteCollectionData? collection = Find<tk2dSpriteCollectionData>(
                collections, ObjectCollection);
            if (collection == null)
            {
                Plugin.Log.LogWarning(
                    $"[CUT] failed at 'collection': '{ObjectCollection}' not found");
                yield break;
            }

            tk2dSpriteAnimation? library = Find<tk2dSpriteAnimation>(
                animations, ObjectAnimation);
            if (library == null)
            {
                Plugin.Log.LogWarning(
                    $"[CUT] failed at 'animation': '{ObjectAnimation}' not found");
                yield break;
            }

            probe = CreateProbe(collection, library);
            tk2dSpriteAnimator? animator = probe.GetComponent<tk2dSpriteAnimator>();
            tk2dSpriteAnimationClip? clip = animator != null
                ? animator.GetClipByName(ClipName)
                : null;
            if (animator == null || clip == null)
            {
                Plugin.Log.LogWarning(
                    $"[CUT] failed at 'clip': '{ClipName}' not found in '{library.name}'");
                yield break;
            }

            animator.Play(clip);
            Plugin.Log.LogInfo($"[CUT] playing '{clip.name}' length={clip.Duration:0.###}s");
            yield return new WaitForSeconds(Lifetime);
            Plugin.Log.LogInfo("[CUT] done");
        }
        finally
        {
            if (probe != null)
            {
                Destroy(probe);
            }

            UnloadAll();
            Destroy(this);
        }
    }

    /// <summary>
    /// Creates the scratch object next to the hero and wires the sprite and animator;
    /// both are absent on the source assets, so the probe adds them.
    /// </summary>
    private static GameObject CreateProbe(tk2dSpriteCollectionData collection,
        tk2dSpriteAnimation library)
    {
        GameObject probe = new GameObject("CrestGetProbe");
        HeroController? hero = HeroController.instance;
        probe.transform.position = hero != null
            ? hero.transform.position + new Vector3(1.5f, 1f, 0f)
            : Vector3.zero;

        tk2dSprite sprite = probe.AddComponent<tk2dSprite>();
        sprite.Collection = collection;
        sprite.ForceBuild();
        sprite.SortingOrder = 100;

        tk2dSpriteAnimator animator = probe.AddComponent<tk2dSpriteAnimator>();
        animator.Library = library;

        Plugin.Log.LogInfo(
            $"[CUT] probe built at {probe.transform.position} with '{collection.name}'");
        return probe;
    }

    /// <summary>Loads one bundle from the streaming directory; missing file is a warning.</summary>
    private AssetBundle? Load(string dir, string name)
    {
        string path = Path.Combine(dir, name + ".bundle");
        try
        {
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning($"[CUT] bundle missing: {path}");
                return null;
            }

            AssetBundle? bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                Plugin.Log.LogWarning($"[CUT] bundle load returned null: {name}");
                return null;
            }

            _bundles.Add(bundle);
            Plugin.Log.LogInfo($"[CUT] bundle loaded: {name}");
            return bundle;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[CUT] failed at 'load {name}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Logs the objects and typed assets of one bundle and returns the component named
    /// after the wanted GameObject, falling back to a typed asset with that name.
    /// </summary>
    private static T? Find<T>(AssetBundle bundle, string objectName) where T : Component
    {
        GameObject[] objects = bundle.LoadAllAssets<GameObject>();
        Plugin.Log.LogInfo(
            $"[CUT] {bundle.name}: {objects.Length} object(s): {Names(objects)}");

        T[] components = bundle.LoadAllAssets<T>();
        Plugin.Log.LogInfo(
            $"[CUT] {bundle.name}: {components.Length} {typeof(T).Name}: {Names(components)}");

        foreach (GameObject obj in objects)
        {
            if (obj != null && obj.name == objectName)
            {
                T? found = obj.GetComponent<T>();
                if (found != null)
                {
                    return found;
                }
            }
        }

        foreach (T component in components)
        {
            if (component != null && component.name == objectName)
            {
                return component;
            }
        }

        return null;
    }

    /// <summary>Comma-separated names of the assets, for one log line.</summary>
    private static string Names<T>(T[] assets) where T : UnityEngine.Object
    {
        List<string> names = new List<string>();
        foreach (T asset in assets)
        {
            if (asset != null)
            {
                names.Add(asset.name);
            }
        }

        return string.Join(", ", names);
    }

    /// <summary>Unloads every bundle the probe opened, keeping loaded objects alive.</summary>
    private void UnloadAll()
    {
        foreach (AssetBundle bundle in _bundles)
        {
            try
            {
                if (bundle == null)
                {
                    continue;
                }

                string name = bundle.name;
                bundle.Unload(false);
                Plugin.Log.LogInfo($"[CUT] bundle unloaded: {name}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[CUT] failed at 'unload': {ex.Message}");
            }
        }

        _bundles.Clear();
    }

    /// <summary>
    /// Drives <see cref="Play"/> and turns any failure into one log line: a coroutine
    /// cannot catch around its own yields, so the catch lives in this wrapper.
    /// </summary>
    private IEnumerator GuardedPlay()
    {
        IEnumerator steps = Play();
        try
        {
            while (true)
            {
                bool moved;
                try
                {
                    moved = steps.MoveNext();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[CUT] failed at 'coroutine': {ex.Message}");
                    yield break;
                }

                if (!moved)
                {
                    yield break;
                }

                yield return steps.Current;
            }
        }
        finally
        {
            // A coroutine killed by Unity never resumes its inner iterator, so dispose it
            // here: that is what runs Play's finally and releases the bundles.
            (steps as IDisposable)?.Dispose();
        }
    }
}
