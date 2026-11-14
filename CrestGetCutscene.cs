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

    /// <summary>Probe that plays the whole ability-get chain next to the hero.</summary>
    Ability,
}

/// <summary>
/// Probe for a vanilla clip chain loaded straight from StreamingAssets. The scene itself
/// is not rebuilt yet: this only proves the clips load and animate. Each set is a data
/// row, so the crest shrine and the ability-get probes share one code path.
/// </summary>
internal sealed class CrestGetCutscene : MonoBehaviour
{
    /// <summary>Crest shrine: collection bundle that holds the shrine sprite collection.</summary>
    private const string ShrineCollections = "tk2dcollections_assets_crestshrine";

    /// <summary>Crest shrine: animation bundle that holds the shrine clip library.</summary>
    private const string ShrineAnimations = "tk2danimations_assets_crestshrine";

    /// <summary>Crest shrine: collection dependency of the shrine clips.</summary>
    private const string ShrineWeaverCollections = "tk2dcollections_assets_areaweaver";

    /// <summary>Ability get: collection bundle that holds the ability sprite collection.</summary>
    private const string AbilityCollections = "tk2dcollections_assets_silkshared";

    /// <summary>Ability get: animation bundle that holds the ability clip library.</summary>
    private const string AbilityAnimations = "tk2danimations_assets_silkshared";

    /// <summary>Hero collection dependency shared by both sets.</summary>
    private const string HeroCollections = "herocollections_assets_shared";

    /// <summary>Crest shrine: GameObject that carries the shrine sprite collection.</summary>
    private const string ShrineCollectionAsset = "Crest Get Cln";

    /// <summary>Crest shrine: GameObject that carries the shrine clip library.</summary>
    private const string ShrineAnimationAsset = "Crest Get Anim";

    /// <summary>Ability get: asset that carries the ability sprite collection.</summary>
    private const string AbilityCollectionAsset = "Ability Get Thread Cln";

    /// <summary>Ability get: asset that carries the ability clip library.</summary>
    private const string AbilityAnimationAsset = "Ability Get Thread Anim";

    /// <summary>Crest shrine probe: one clip, as before.</summary>
    private static readonly ProbeSet ShrineSet = new ProbeSet
    {
        CollectionBundle = ShrineCollections,
        ExtraBundles = new[] { ShrineWeaverCollections, HeroCollections },
        AnimationBundle = ShrineAnimations,
        CollectionAsset = ShrineCollectionAsset,
        AnimationAsset = ShrineAnimationAsset,
        Clips = new[] { "Stab Antic" },
    };

    /// <summary>Ability get probe: the vanilla chain, played in order.</summary>
    private static readonly ProbeSet AbilitySet = new ProbeSet
    {
        CollectionBundle = AbilityCollections,
        ExtraBundles = new[] { HeroCollections },
        AnimationBundle = AbilityAnimations,
        CollectionAsset = AbilityCollectionAsset,
        AnimationAsset = AbilityAnimationAsset,
        Clips = new[]
        {
            "Init", "Full", "First Weaver Bind", "Final Burst", "Shrink",
        },
    };

    private readonly List<AssetBundle> _bundles = new List<AssetBundle>();

    /// <summary>Set the running probe plays; null until <see cref="Run"/> picks one.</summary>
    private ProbeSet _set = ShrineSet;

    /// <summary>
    /// Starts the probe on the hero object for the mode's set. A second call while one is
    /// running is ignored, so a repeated key press cannot stack sprites.
    /// </summary>
    internal static void Run(TestKeyMode mode)
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
            probe._set = mode == TestKeyMode.Ability ? AbilitySet : ShrineSet;
            probe.StartCoroutine(probe.GuardedPlay());
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[CUT] failed at 'start': {ex.Message}");
        }
    }

    /// <summary>Loads the bundles, plays every clip in turn, then cleans up on every path.</summary>
    private IEnumerator Play()
    {
        GameObject? probe = null;
        try
        {
            string dir = Path.Combine(Application.dataPath, "StreamingAssets", "aa",
                "StandaloneWindows64");
            Plugin.Log.LogInfo($"[CUT] bundle dir '{dir}'");

            AssetBundle? collections = Load(dir, _set.CollectionBundle);
            if (collections == null)
            {
                Plugin.Log.LogWarning("[CUT] failed at 'load collection bundle': bundle unavailable");
                yield break;
            }

            foreach (string extra in _set.ExtraBundles)
            {
                Load(dir, extra);
            }

            AssetBundle? animations = Load(dir, _set.AnimationBundle);
            if (animations == null)
            {
                Plugin.Log.LogWarning("[CUT] failed at 'load animation bundle': bundle unavailable");
                yield break;
            }

            tk2dSpriteCollectionData? collection = Find<tk2dSpriteCollectionData>(
                collections, _set.CollectionAsset);
            if (collection == null)
            {
                Plugin.Log.LogWarning(
                    $"[CUT] failed at 'collection': '{_set.CollectionAsset}' not found");
                yield break;
            }

            tk2dSpriteAnimation? library = Find<tk2dSpriteAnimation>(
                animations, _set.AnimationAsset);
            if (library == null)
            {
                Plugin.Log.LogWarning(
                    $"[CUT] failed at 'animation': '{_set.AnimationAsset}' not found");
                yield break;
            }

            probe = CreateProbe(collection, library);
            tk2dSpriteAnimator? animator = probe.GetComponent<tk2dSpriteAnimator>();
            if (animator == null)
            {
                Plugin.Log.LogWarning("[CUT] failed at 'animator': component missing");
                yield break;
            }

            foreach (string name in _set.Clips)
            {
                tk2dSpriteAnimationClip? clip = animator.GetClipByName(name);
                if (clip == null)
                {
                    Plugin.Log.LogWarning(
                        $"[CUT] clip '{name}' missing in '{library.name}'");
                    yield break;
                }

                Plugin.Log.LogInfo($"[CUT] clip '{clip.name}' dur={clip.Duration:0.###}");
                animator.Play(clip);
                yield return new WaitForSeconds(clip.Duration);
            }

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
    /// Creates the scratch object on the hero and wires the sprite and animator; both are
    /// absent on the source assets, so the probe adds them.
    /// </summary>
    private static GameObject CreateProbe(tk2dSpriteCollectionData collection,
        tk2dSpriteAnimation library)
    {
        GameObject probe = new GameObject("CrestGetProbe");
        HeroController? hero = HeroController.instance;
        probe.transform.position = hero != null ? hero.transform.position : Vector3.zero;

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
    /// after the wanted GameObject, falling back to a typed asset with that name, then to
    /// the only typed asset of the bundle.
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

        T? only = null;
        int count = 0;
        foreach (T component in components)
        {
            if (component == null)
            {
                continue;
            }

            if (component.name == objectName)
            {
                return component;
            }

            count++;
            only = component;
        }

        if (count == 1)
        {
            Plugin.Log.LogInfo(
                $"[CUT] fallback: using the only {typeof(T).Name} '{only!.name}'");
            return only;
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

    /// <summary>Bundles and asset names one probe run needs.</summary>
    private sealed class ProbeSet
    {
        /// <summary>Bundle that holds the sprite collection.</summary>
        internal string CollectionBundle = "";

        /// <summary>Dependency bundles loaded right after the collection bundle.</summary>
        internal string[] ExtraBundles = Array.Empty<string>();

        /// <summary>Bundle that holds the clip library.</summary>
        internal string AnimationBundle = "";

        /// <summary>Asset name of the sprite collection.</summary>
        internal string CollectionAsset = "";

        /// <summary>Asset name of the clip library.</summary>
        internal string AnimationAsset = "";

        /// <summary>Clips to play, in order.</summary>
        internal string[] Clips = Array.Empty<string>();
    }
}
