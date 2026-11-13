using System;
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

    private static float _nextFind;

    private tk2dSpriteAnimator? _animator;
    private tk2dLookAnimNPC? _look;
    private RandomAudioClipTable? _voice;
    private PlayMakerNPC? _npc;
    private bool _wasInRange;
    private float _lastGreet = NeverGreeted;
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
            _wasInRange = false;
            return;
        }

        Vector3 heroPos = hero.transform.position;
        float dx = heroPos.x - transform.position.x;
        float distance = Vector2.Distance(heroPos, transform.position);
        bool inRange = distance <= Plugin.GreetRadius.Value;
        bool wasInRange = _wasInRange;
        _wasInRange = inRange;

        if (!inRange || wasInRange)
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

        if (!GreetingPolicy.ShouldGreet(inRange, wasInRange, introDone, talking, since,
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
        _lastGreet = Time.unscaledTime;
        _skipLogged = false;

        bool left = GreetingPolicy.FacingLeft(dx);
        if (_look != null)
        {
            _look.ForceTurn(left);
            _look.UnlockTurn();
        }

        string clipName = Plugin.GreetClip.Value;
        tk2dSpriteAnimationClip? clip = _animator != null ? _animator.GetClipByName(clipName) : null;
        if (clip == null)
        {
            Plugin.Log.LogWarning($"[GREET] skipped: clip '{clipName}' not found");
        }
        else
        {
            tk2dSpriteAnimationClip? current = _animator!.CurrentClip;
            bool alreadyPlaying = _animator.Playing && current != null && current.name == clipName;
            if (!alreadyPlaying)
            {
                _animator.Play(clip);
            }

            Plugin.Log.LogInfo($"[GREET] wave (dist={distance:F1}, clip={clip.name})");
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
