using System;
using System.Collections;
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

    /// <summary>
    /// Extra time after the wave clip ends, during which the clip is kept alive and the
    /// animator and the FSM are watched, so late switches still land in the log.
    /// </summary>
    private const float ClipTail = 1f;

    /// <summary>How many times the wave clip may be restarted if something overrides it.</summary>
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
    private bool _armed = true;
    private float _lastGreetTime = NeverGreeted;
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
        float elapsed = Time.unscaledTime - _lastGreetTime;
        float cooldown = Plugin.GreetCooldown.Value;

        if (!GreetingPolicy.ShouldGreet(armed, distance, enterRadius, introDone, talking, elapsed, cooldown))
        {
            Skip(!introDone ? "intro not done"
                : talking ? "dialogue running"
                : $"cooldown {cooldown - elapsed:F1}s (elapsed={elapsed:F1}s, need={cooldown:F1}s)");
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

        _lastGreetTime = Time.unscaledTime;
        _skipLogged = false;
        FindFsm();

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
    /// with the look animator deactivated so nothing overrides the clip. Deactivating the
    /// component alone would not stop its running coroutines, hence the public call.
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
            _look.Deactivate();
        }

        OpenWindow(clip);
        PlayLogged(clip);
        Plugin.Log.LogInfo($"[GREET] wave (dist={distance:F1}, clip={clip.name})");

        int restarts = 0;
        float until = _diagUntil;
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

        if (_look != null)
        {
            _look.Activate();
        }

        Plugin.Log.LogInfo($"[GREET] wave done restarts={restarts}");
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
