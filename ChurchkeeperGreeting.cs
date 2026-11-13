using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using CrestPicker.Pure;
using HutongGames.PlayMaker;
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

    /// <summary>FSM on the maid whose state changes are logged during the wave window.</summary>
    private const string FsmName = "Conversation";

    /// <summary>Seconds between two scene searches for the maid.</summary>
    private const float FindInterval = 1f;

    /// <summary>Last greet time used before the first greeting, so the cooldown never blocks it.</summary>
    private const float NeverGreeted = float.NegativeInfinity;

    /// <summary>Longest wait for the look animator's own turn clip before the wave starts.</summary>
    private const float TurnSettle = 0.15f;

    /// <summary>Extra time after a clip ends during which the animator and the FSM are watched.</summary>
    private const float ClipTail = 1f;

    /// <summary>How many times a clip may be restarted if something overrides it.</summary>
    private const int MaxRestarts = 2;

    private static float _nextFind;

    private tk2dSpriteAnimator? _animator;
    private tk2dLookAnimNPC? _look;
    private RandomAudioClipTable? _voice;
    private PlayMakerNPC? _npc;
    private PlayMakerFSM? _fsm;
    private Action<FsmState>? _fsmHandler;
    private string? _fsmState;
    private bool _fsmHooked;
    private string? _lastClip;
    private float _diagStart;
    private float _diagUntil = float.NegativeInfinity;
    private float _lastGreetTime = NeverGreeted;
    private bool _cycleRunning;
    private int _cycleIndex;
    private bool _lookOff;
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
        FindFsm();
        DumpActors();
        DumpClipEvents();
    }

    private void OnDestroy()
    {
        if (_fsm != null && _fsm.Fsm != null && _fsmHandler != null)
        {
            _fsm.Fsm.StateChanged -= _fsmHandler;
        }
    }

    /// <summary>Remembers the conversation FSM so its state switches can be logged.</summary>
    private void FindFsm()
    {
        if (_fsm != null)
        {
            return;
        }

        foreach (PlayMakerFSM fsm in GetComponents<PlayMakerFSM>())
        {
            if (fsm != null && fsm.FsmName == FsmName)
            {
                _fsm = fsm;
                return;
            }
        }
    }

    /// <summary>Logs once which components on the maid can touch the sprite animation.</summary>
    private void DumpActors()
    {
        StringBuilder line = new StringBuilder("[GREET] actors:");
        line.Append($" tk2dLookAnimNPC(enabled={_look != null && _look.enabled} state={(_look != null ? _look.State.ToString() : "none")})");
        line.Append($" tk2dSpriteAnimator(playing={_animator != null && _animator.Playing} clip='{_animator?.CurrentClip?.name ?? "none"}')");

        PlayMakerFSM[] fsms = GetComponents<PlayMakerFSM>();
        line.Append($" fsms={fsms.Length}");
        foreach (PlayMakerFSM fsm in fsms)
        {
            if (fsm != null)
            {
                line.Append($" '{fsm.FsmName}'='{fsm.ActiveStateName}'");
            }
        }

        tk2dAnimationTriggerResponder? responder = GetComponent<tk2dAnimationTriggerResponder>();
        line.Append(responder != null
            ? $" tk2dAnimationTriggerResponder(enabled={responder.enabled})"
            : " tk2dAnimationTriggerResponder=none");
        Plugin.Log.LogInfo(line.ToString());
    }

    /// <summary>Logs every animator clip with its duration and the frames that carry events, once on attach.</summary>
    private void DumpClipEvents()
    {
        tk2dSpriteAnimation? library = _animator != null ? _animator.Library : null;
        if (library == null || library.clips == null)
        {
            Plugin.Log.LogInfo("[GREET] clips unavailable");
            return;
        }

        foreach (tk2dSpriteAnimationClip clip in library.clips)
        {
            if (clip == null || clip.frames == null)
            {
                continue;
            }

            StringBuilder line = new StringBuilder();
            int events = 0;
            for (int i = 0; i < clip.frames.Length; i++)
            {
                tk2dSpriteAnimationFrame? frame = clip.frames[i];
                if (frame == null)
                {
                    continue;
                }

                string info = frame.eventInfo ?? string.Empty;
                if (info.Length == 0 && frame.eventInt == 0 && frame.eventFloat == 0f)
                {
                    continue;
                }

                events++;
                line.Append($" frame={i} info='{info}' int={frame.eventInt} float={frame.eventFloat}");
            }

            string header = $"[GREET] clip '{clip.name}' dur={clip.Duration:F2}";
            Plugin.Log.LogInfo(events == 0 ? $"{header} events: none" : $"{header} events: {events}{line}");
        }
    }

    private void Update()
    {
        try
        {
            HookFsm();
            Watch();
            Step();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GREET] tick failed: {ex.Message}");
        }
    }

    /// <summary>Subscribes to the conversation FSM once it exists; it is not built in Awake.</summary>
    private void HookFsm()
    {
        if (_fsmHooked || _fsm == null || _fsm.Fsm == null)
        {
            return;
        }

        _fsmHooked = true;
        _fsmState = _fsm.Fsm.ActiveStateName;
        _fsmHandler = OnFsmStateChanged;
        _fsm.Fsm.StateChanged += _fsmHandler;
    }

    private void OnFsmStateChanged(FsmState state)
    {
        string now = state != null ? state.Name : "none";
        if (Time.unscaledTime <= _diagUntil)
        {
            Plugin.Log.LogInfo($"[GREET] fsm '{FsmName}' state '{_fsmState ?? "none"}' -> '{now}'");
        }

        _fsmState = now;
    }

    /// <summary>Inside the wave window: logs every animator clip switch with its time offset.</summary>
    private void Watch()
    {
        if (Time.unscaledTime > _diagUntil)
        {
            return;
        }

        string? name = _animator != null && _animator.CurrentClip != null ? _animator.CurrentClip.name : null;
        if (name == _lastClip)
        {
            return;
        }

        bool playing = _animator != null && _animator.Playing;
        Plugin.Log.LogInfo(
            $"[GREET] clip changed '{_lastClip ?? "none"}' -> '{name ?? "none"}'" +
            $" playing={playing} t={Time.unscaledTime - _diagStart:F2}");
        _lastClip = name;
    }

    /// <summary>Starts the diagnostic window of one wave and resets the watched clip.</summary>
    private void OpenWindow(tk2dSpriteAnimationClip clip)
    {
        _diagStart = Time.unscaledTime;
        _diagUntil = _diagStart + clip.Duration + ClipTail;
        _lastClip = _animator != null && _animator.CurrentClip != null ? _animator.CurrentClip.name : null;
    }

    /// <summary>Plays a clip and names the clip it replaces, so every override is traceable.</summary>
    private void PlayLogged(tk2dSpriteAnimationClip clip)
    {
        string previous = _animator != null && _animator.CurrentClip != null ? _animator.CurrentClip.name : "none";
        Plugin.Log.LogInfo($"[GREET] play '{clip.name}' (previous='{previous}')");
        _animator?.Play(clip);
    }

    /// <summary>One frame: leaves the greeting zone, then decides whether a cycle is due.</summary>
    private void Step()
    {
        if (!DistanceToHero(out float distance, out float dx))
        {
            return;
        }

        float radius = Plugin.GreetRadius.Value;
        float exitRadius = Mathf.Max(radius, Plugin.GreetExitRadius.Value);

        if (!Plugin.GreetEnabled.Value || distance > exitRadius)
        {
            _lastGreetTime = NeverGreeted;
            _skipLogged = false;
            return;
        }

        if (_cycleRunning)
        {
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
        if (data == null || !data.churchKeeperIntro)
        {
            Skip("intro not done");
            return;
        }

        if (_npc != null && _npc.IsRunningDialogue)
        {
            Skip("dialogue running");
            return;
        }

        float elapsed = Time.unscaledTime - _lastGreetTime;
        float cooldown = Plugin.GreetCooldown.Value;
        if (!GreetingPolicy.ShouldRepeat(distance, radius, elapsed, cooldown))
        {
            Skip($"cooldown {cooldown - elapsed:F1}s (elapsed={elapsed:F1}s, need={cooldown:F1}s)");
            return;
        }

        StartCycle(dx, distance);
    }

    /// <summary>Turns to the hero, then plays the wave and its tail clip in the background.</summary>
    private void StartCycle(float dx, float distance)
    {
        _lastGreetTime = Time.unscaledTime;
        _skipLogged = false;
        FindFsm();

        tk2dSpriteAnimationClip? clip = FindClip(Plugin.GreetClip.Value);
        if (clip == null)
        {
            Plugin.Log.LogWarning($"[GREET] skipped: clip '{Plugin.GreetClip.Value}' not found");
            return;
        }

        tk2dSpriteAnimationClip? end = FindClip(Plugin.GreetClipEnd.Value);
        _cycleIndex++;
        Plugin.Log.LogInfo(
            $"[GREET] cycle (dist={distance:F1}, clip={clip.name} -> {end?.name ?? "none"}, n={_cycleIndex})");
        _cycleRunning = true;
        StartCoroutine(Cycle(GreetingPolicy.FacingLeft(dx), clip, end));
        DumpAudio();

        if (_voice != null)
        {
            NPCSpeakingAudio.PlayVoice(_voice, transform.position);
            Plugin.Log.LogInfo($"[GREET] voice played table='{_voice.name}'");
        }
        else if (!_voiceMissingLogged)
        {
            _voiceMissingLogged = true;
            Plugin.Log.LogWarning("[GREET] skipped: no voice table");
        }
    }

    /// <summary>
    /// Turns to the hero, lets the look animator finish its own turn clip, then holds the look
    /// animator off while the wave and its tail clip play, so nothing falls back to Idle
    /// between them. Deactivating the component alone would not stop its running coroutines,
    /// hence the public call.
    /// </summary>
    private IEnumerator Cycle(bool left, tk2dSpriteAnimationClip clip, tk2dSpriteAnimationClip? end)
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

        if (_animator == null)
        {
            _cycleRunning = false;
            yield break;
        }

        HoldIdle();

        yield return PlayAndWait(clip);
        if (end != null && end.name != clip.name)
        {
            yield return PlayAndWait(end);
        }

        _cycleRunning = false;
        IdleAfterCycle();
    }

    /// <summary>Plays one clip, restarts it if something overrides it, and waits for its end.</summary>
    private IEnumerator PlayAndWait(tk2dSpriteAnimationClip clip)
    {
        OpenWindow(clip);
        PlayLogged(clip);

        int restarts = 0;
        float until = Time.unscaledTime + clip.Duration;
        while (_animator != null && Time.unscaledTime < until)
        {
            tk2dSpriteAnimationClip? current = _animator.CurrentClip;
            if (_animator.Playing && (current == null || current.name != clip.name))
            {
                if (restarts < MaxRestarts)
                {
                    restarts++;
                    PlayLogged(clip);
                    Plugin.Log.LogInfo($"[GREET] clip restarted (was='{current?.name ?? "none"}')");
                }
            }

            yield return null;
        }
    }

    /// <summary>Distance and horizontal offset from the maid to the hero; false without a hero.</summary>
    private bool DistanceToHero(out float distance, out float dx)
    {
        HeroController? hero = HeroController.instance;
        if (hero == null)
        {
            distance = 0f;
            dx = 0f;
            return false;
        }

        Vector3 heroPos = hero.transform.position;
        distance = Vector2.Distance(heroPos, transform.position);
        dx = heroPos.x - transform.position.x;
        return true;
    }

    /// <summary>Turns the look animator off for the whole greeting cycle.</summary>
    private void HoldIdle()
    {
        if (_lookOff || _look == null)
        {
            return;
        }

        _look.Deactivate();
        _lookOff = true;
    }

    /// <summary>Turns the look animator back on once the chain is done, so the maid stands idle between cycles.</summary>
    private void IdleAfterCycle()
    {
        if (!_lookOff)
        {
            return;
        }

        _lookOff = false;
        if (_look != null)
        {
            _look.Activate();
        }

        Plugin.Log.LogInfo("[GREET] idle after cycle");
    }

    /// <summary>Logs the audio sources under the maid and the voice table once per cycle.</summary>
    private void DumpAudio()
    {
        foreach (AudioSource source in GetComponentsInChildren<AudioSource>(true))
        {
            if (source == null)
            {
                continue;
            }

            string clip = source.clip != null ? source.clip.name : "none";
            Plugin.Log.LogInfo(
                $"[GREET] audio '{Path(source.transform)}' playing={source.isPlaying}" +
                $" clip='{clip}' vol={source.volume:F2}");
        }

        Plugin.Log.LogInfo($"[GREET] voice {TableDump(_voice)}");
    }

    /// <summary>Table name and its readable fields; anything unreadable is reported as n/a.</summary>
    private static string TableDump(RandomAudioClipTable? table)
    {
        if (table == null)
        {
            return "table=none";
        }

        StringBuilder line = new StringBuilder($"table='{table.name}'");
        try
        {
            foreach (FieldInfo field in table.GetType().GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                string value;
                try
                {
                    value = Describe(field.GetValue(table));
                }
                catch (Exception)
                {
                    value = "n/a";
                }

                line.Append($" {field.Name}={value}");
            }
        }
        catch (Exception)
        {
            line.Append(" fields=n/a");
        }

        return line.ToString();
    }

    private static string Describe(object? value)
    {
        if (value == null)
        {
            return "null";
        }

        if (value is AudioClip clip)
        {
            return $"'{clip.name}'";
        }

        if (value is string text)
        {
            return $"'{text}'";
        }

        if (value is IEnumerable list)
        {
            var names = new List<string>();
            foreach (object? item in list)
            {
                names.Add(item is AudioClip itemClip ? itemClip.name : item?.ToString() ?? "null");
            }

            return $"[{string.Join(", ", names)}]";
        }

        return value.ToString() ?? "null";
    }

    /// <summary>Clip by name; an empty or blank name means there is no clip.</summary>
    private tk2dSpriteAnimationClip? FindClip(string name)
    {
        if (_animator == null || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return _animator.GetClipByName(name);
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
