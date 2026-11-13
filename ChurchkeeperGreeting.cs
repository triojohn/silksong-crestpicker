using System;
using System.Collections;
using CrestPicker.Pure;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CrestPicker;

/// <summary>
/// Waves and calls out when the hero walks up to the chapel maid after the first talk,
/// but only while <see cref="OfferGate"/> finds more crests in the saves than unlocked here.
/// The component lives on the maid object and is attached by <see cref="Tick"/> once the
/// Bonetown scene is loaded. Nothing is sent to any PlayMaker FSM.
/// </summary>
internal sealed class ChurchkeeperGreeting : MonoBehaviour
{
    private const string SceneName = "Bonetown";
    private const string ObjectName = "Churchkeeper";

    /// <summary>Seconds between two scene searches for the maid.</summary>
    private const float FindInterval = 1f;

    /// <summary>Last greet time used before the first greeting, so the cooldown never blocks it.</summary>
    private const float NeverGreeted = -9999f;

    /// <summary>Longest wait for the look animator's own turn clip before the wave starts.</summary>
    private const float TurnSettle = 0.15f;

    /// <summary>Extra time the look animator stays off after the wave clip ends.</summary>
    private const float ClipTail = 0.1f;

    private static float _nextFind;

    private tk2dSpriteAnimator? _animator;
    private tk2dLookAnimNPC? _look;
    private RandomAudioClipTable? _voice;
    private PlayMakerNPC? _npc;
    private bool _armed = true;
    private float _lastGreet = NeverGreeted;
    private Coroutine? _wave;
    private bool _skipLogged;
    private bool _voiceMissingLogged;

    /// <summary>Finds the maid in Bonetown and attaches the greeting once. Never throws.</summary>
    internal static void Tick()
    {
        if (Time.unscaledTime < _nextFind)
        {
            return;
        }

        _nextFind = Time.unscaledTime + FindInterval;
        try
        {
            Attach();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GREET] attach failed: {ex.Message}");
        }
    }

    private static void Attach()
    {
        if (SceneManager.GetActiveScene().name != SceneName)
        {
            return;
        }

        foreach (PlayMakerNPC npc in Resources.FindObjectsOfTypeAll<PlayMakerNPC>())
        {
            if (npc == null)
            {
                continue;
            }

            GameObject go = npc.gameObject;
            if (!go.scene.IsValid() || go.scene.name != SceneName || go.name != ObjectName)
            {
                continue;
            }

            if (go.GetComponent<ChurchkeeperGreeting>() != null)
            {
                return;
            }

            go.AddComponent<ChurchkeeperGreeting>();
            Plugin.Log.LogInfo($"[GREET] attached to '{Path(go.transform)}'");
            return;
        }
    }

    private void Awake()
    {
        _npc = GetComponent<PlayMakerNPC>();
        _look = GetComponent<tk2dLookAnimNPC>();
        _animator = GetComponent<tk2dSpriteAnimator>();
        NPCSpeakingAudio? audio = GetComponent<NPCSpeakingAudio>();
        _voice = audio != null ? audio.Table : null;
    }

    private void Update()
    {
        try
        {
            Step();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GREET] tick failed: {ex.Message}");
        }
    }

    /// <summary>One frame: distance edge, gates, then the greeting itself.</summary>
    private void Step()
    {
        if (!Plugin.GreetEnabled.Value)
        {
            return;
        }

        HeroController? hero = HeroController.instance;
        if (hero == null)
        {
            _armed = true;
            return;
        }

        Vector3 heroPos = hero.transform.position;
        float dx = heroPos.x - transform.position.x;
        float distance = Vector2.Distance(heroPos, transform.position);
        float enterRadius = Plugin.GreetRadius.Value;
        bool armed = _armed;
        _armed = GreetingPolicy.ShouldArm(armed, distance, enterRadius, Plugin.GreetExitRadius.Value);

        if (!armed)
        {
            _skipLogged = false;
            return;
        }

        if (!OfferGateRuntime.ShouldRunChain())
        {
            return;
        }

        if (!Plugin.EnableSnailChoice)
        {
            Skip("snail choice off");
            return;
        }

        if (CrestSelectMenu.IsOpen)
        {
            Skip("menu open");
            return;
        }

        PlayerData? data = PlayerData.instance;
        bool introDone = data != null && data.churchKeeperIntro;
        bool talking = _npc != null && _npc.IsRunningDialogue;
        float since = Time.unscaledTime - _lastGreet;

        if (!GreetingPolicy.ShouldGreet(armed, distance, enterRadius, introDone, talking, since,
                Plugin.GreetCooldown.Value))
        {
            Skip(!introDone ? "intro not done"
                : talking ? "dialogue running"
                : $"cooldown {since:F1}s");
            return;
        }

        Greet(dx, distance);
    }

    /// <summary>Turns to the hero, plays the wave clip and the voice.</summary>
    private void Greet(float dx, float distance)
    {
        if (_wave != null)
        {
            Plugin.Log.LogInfo("[GREET] skipped: wave in progress");
            return;
        }

        _lastGreet = Time.unscaledTime;
        _skipLogged = false;

        string clipName = Plugin.GreetClip.Value;
        tk2dSpriteAnimationClip? clip = _animator != null ? _animator.GetClipByName(clipName) : null;
        if (clip == null)
        {
            Plugin.Log.LogWarning($"[GREET] skipped: clip '{clipName}' not found");
        }
        else if (IsPlaying(clip))
        {
            Plugin.Log.LogInfo($"[GREET] wave (dist={distance:F1}, clip={clip.name}, already playing)");
        }
        else
        {
            _wave = StartCoroutine(Wave(GreetingPolicy.FacingLeft(dx), clip, distance));
        }

        if (_voice != null)
        {
            NPCSpeakingAudio.PlayVoice(_voice, transform.position);
        }
        else if (!_voiceMissingLogged)
        {
            _voiceMissingLogged = true;
            Plugin.Log.LogWarning("[GREET] skipped: no voice table");
        }
    }

    /// <summary>
    /// Turns to the hero, lets the look animator finish its own turn clip, then plays the wave
    /// with the look animator disabled so nothing overrides the clip.
    /// </summary>
    private IEnumerator Wave(bool left, tk2dSpriteAnimationClip clip, float distance)
    {
        if (_look != null)
        {
            _look.ForceTurn(left);
            _look.UnlockTurn();
        }

        float settle = Time.unscaledTime + TurnSettle;
        while (_animator != null && _animator.Playing && Time.unscaledTime < settle)
        {
            yield return null;
        }

        if (_animator == null || IsPlaying(clip))
        {
            _wave = null;
            yield break;
        }

        if (_look != null)
        {
            _look.enabled = false;
        }

        _animator.Play(clip);
        Plugin.Log.LogInfo($"[GREET] wave (dist={distance:F1}, clip={clip.name})");

        float until = Time.unscaledTime + clip.Duration + ClipTail;
        while (_animator != null && _animator.Playing && IsPlaying(clip) && Time.unscaledTime < until)
        {
            yield return null;
        }

        if (_look != null)
        {
            _look.enabled = true;
        }

        _wave = null;
    }

    /// <summary>True when the animator runs exactly this clip right now.</summary>
    private bool IsPlaying(tk2dSpriteAnimationClip clip)
    {
        return _animator != null && _animator.Playing && _animator.CurrentClip != null
            && _animator.CurrentClip.name == clip.name;
    }

    /// <summary>Logs a skip reason once per approach.</summary>
    private void Skip(string reason)
    {
        if (_skipLogged)
        {
            return;
        }

        _skipLogged = true;
        Plugin.Log.LogInfo($"[GREET] skipped: {reason}");
    }

    private static string Path(Transform node)
    {
        string path = node.name;
        for (Transform? parent = node.parent; parent != null; parent = parent.parent)
        {
            path = parent.name + "/" + path;
        }

        return path;
    }
}
