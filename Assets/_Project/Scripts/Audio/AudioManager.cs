using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The game's one sound player. Created automatically once per run (no scene object) and kept with
// DontDestroyOnLoad: the main menu, the village (stays loaded but suspended during travel) and the regions are
// separate scenes, so a scene-owned player would be cut off whenever its scene is suspended or unloaded.
//
// - One-shots: AudioManager.Play(GameSfx). Overlap allowed (PlayOneShot); each sound has a short re-trigger guard.
// - BGM / ambience: chosen by the active scene's name from AudioLibrary.scenes, looped, crossfaded on every
//   active-scene change (travel, save load, main menu → game). Same sound already playing = untouched.
// - Volumes: Master / BGM / SFX / Ambience (0–1; defaults 0.75 / 0.70 / 0.70 / 0.60), set by the pause menu's
//   options page and multiplied with the library's mix levels. Kept in PlayerPrefs ("Susupjung.Audio.*", the
//   same store as DisplaySettings' "Susupjung.Display.*"), so they are independent of game save slots.
// - UI click: any interactable uGUI button/toggle clicked with the mouse, unless a game sound (buy, sell, menu,
//   failure …) started in the same frame, so a button never sounds twice.
[DefaultExecutionOrder(-1000)] // Before the EventSystem: the clicked button is still there when it closes its panel.
[DisallowMultipleComponent]
public sealed class AudioManager : MonoBehaviour
{
    public enum Channel { Master, Bgm, Sfx, Ambience }

    private const string LibraryPath = "Audio/AudioLibrary";
    private static AudioManager instance;
    // Indexed by Channel. First launch (no saved value) uses the defaults.
    private static readonly float[] DefaultVolumes = { 0.75f, 0.70f, 0.70f, 0.60f };
    private static readonly string[] VolumeKeys =
    {
        SettingsKeys.Prefix + "Audio.MasterVolume", SettingsKeys.Prefix + "Audio.BgmVolume",
        SettingsKeys.Prefix + "Audio.SfxVolume", SettingsKeys.Prefix + "Audio.AmbienceVolume"
    };
    private static readonly float[] volumes = (float[])DefaultVolumes.Clone();
    private static bool volumesLoaded, volumesDirty;

    // Raised after a channel volume changes.
    public static event Action VolumesChanged;

    [SerializeField] private AudioLibrary library;

    private AudioSource sfxSource;
    private LoopTrack bgm, ambience;
    private AudioListener fallbackListener;
    private readonly Dictionary<GameSfx, float> lastPlayed = new Dictionary<GameSfx, float>();
    private readonly List<(float time, GameSfx id)> delayed = new List<(float, GameSfx)>();
    private readonly List<RaycastResult> raycastResults = new List<RaycastResult>();
    private Camera[] cameraBuffer = new Camera[8];
    private GameObject pressedClickable;
    private bool clickPending;
    private int gameSoundFrame = -1;
    private float lastQuestComplete = float.NegativeInfinity;

    public static AudioManager Instance => instance;
    public AudioLibrary Library => library;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        instance = null;
        VolumesChanged = null;
        volumesLoaded = false;
        volumesDirty = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null) return;
        EnsureVolumesLoaded();
        AudioLibrary loaded = Resources.Load<AudioLibrary>(LibraryPath);
        if (loaded == null)
        {
            Debug.LogWarning($"AudioLibrary가 없습니다 (Resources/{LibraryPath}). 사운드 없이 진행합니다.");
            return;
        }
        GameObject runner = new GameObject("AudioManager");
        DontDestroyOnLoad(runner);
        AudioManager manager = runner.AddComponent<AudioManager>();
        manager.library = loaded;
        manager.ApplyScene(SceneManager.GetActiveScene());
    }

    // ---- Public API ----

    public static void Play(GameSfx id, float delaySeconds = 0f)
    {
        if (instance == null || id == GameSfx.None) return;
        instance.Request(id, delaySeconds);
    }

    public static void PlayHex(EnvironmentTileType type) => Play(AudioLibrary.HexSound(type));

    public static float GetVolume(Channel channel)
    {
        EnsureVolumesLoaded();
        return volumes[(int)channel];
    }

    // Takes effect at once on everything playing (slider drag). Written to PlayerPrefs immediately;
    // flushed to disk once the mouse is released (not every drag frame) and on quit.
    public static void SetVolume(Channel channel, float value)
    {
        EnsureVolumesLoaded();
        value = Mathf.Clamp01(value);
        if (Mathf.Approximately(volumes[(int)channel], value)) return;
        volumes[(int)channel] = value;
        PlayerPrefs.SetFloat(VolumeKeys[(int)channel], value);
        volumesDirty = true;
        if (instance != null) instance.ApplyVolumes();
        try { VolumesChanged?.Invoke(); }
        catch (Exception exception) { Debug.LogException(exception); }
    }

    // Once per run, before anything plays; recreating the manager or changing scenes never resets them.
    private static void EnsureVolumesLoaded()
    {
        if (volumesLoaded) return;
        volumesLoaded = true;
        SettingsKeys.MigrateLegacy(); // Values saved under the former key prefix carry over once.
        for (int i = 0; i < volumes.Length; i++)
            volumes[i] = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKeys[i], DefaultVolumes[i]));
    }

    private static void FlushVolumes()
    {
        if (!volumesDirty) return;
        volumesDirty = false;
        PlayerPrefs.Save();
    }

    private void OnApplicationQuit() => FlushVolumes();

    // ---- Lifetime ----

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        sfxSource = CreateSource("SFX", loop: false);
        bgm = new LoopTrack(CreateSource("BGM A", true), CreateSource("BGM B", true));
        ambience = new LoopTrack(CreateSource("Ambience A", true), CreateSource("Ambience B", true));
        // The main menu has no AudioListener; the scene camera's own listener wins whenever one is active.
        fallbackListener = gameObject.AddComponent<AudioListener>();
        fallbackListener.enabled = false;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        HabitatRecords.FirstDiscovered += OnHabitatDiscovered;
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        HabitatRecords.FirstDiscovered -= OnHabitatDiscovered;
        instance = null;
    }

    private AudioSource CreateSource(string sourceName, bool loop)
    {
        GameObject child = new GameObject(sourceName);
        child.transform.SetParent(transform, false);
        AudioSource source = child.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.ignoreListenerPause = false;
        return source;
    }

    // Travel, save load into another region and the main menu handing over to the farm all change the active scene.
    private void OnActiveSceneChanged(Scene previous, Scene next) => ApplyScene(next);

    private void ApplyScene(Scene scene)
    {
        if (library == null) return;
        AudioLibrary.SceneAudio entry = library.FindScene(scene.name);
        bgm.Switch(entry?.bgm, library.bgmFadeSeconds);
        ambience.Switch(entry?.ambience, library.ambienceFadeSeconds);
        ApplyVolumes();
    }

    // The moment a habitat is revealed to the player (held Q06 discoveries fire only when revealed).
    private void OnHabitatDiscovered(string habitatId) => Request(GameSfx.HabitatDiscovered, 0f);

    // ---- One-shots ----

    private void Request(GameSfx id, float delaySeconds)
    {
        float now = Time.unscaledTime;
        if (id == GameSfx.QuestComplete) lastQuestComplete = now;
        else if (id == GameSfx.QuestStart && library != null)
            delaySeconds = Mathf.Max(delaySeconds, lastQuestComplete + library.questStartAfterCompleteDelay - now);
        if (delaySeconds > 0f) delayed.Add((now + delaySeconds, id));
        else PlayNow(id);
    }

    private void PlayNow(GameSfx id)
    {
        AudioLibrary.SoundGroup group = library != null ? library.Get(id) : null;
        if (group == null) return;
        // Any game sound replaces this frame's generic click (buy, sell, menu, failure …).
        if (id != GameSfx.UiClick && id != GameSfx.FootstepWalk && id != GameSfx.FootstepRun) gameSoundFrame = Time.frameCount;
        AudioClip clip = group.Pick();
        if (clip == null) return;
        float now = Time.unscaledTime;
        if (lastPlayed.TryGetValue(id, out float last) && now - last < group.minInterval) return;
        lastPlayed[id] = now;
        sfxSource.PlayOneShot(clip, group.volume);
    }

    // ---- Per frame ----

    private void Update()
    {
        DetectClick();
        float now = Time.unscaledTime;
        for (int i = delayed.Count - 1; i >= 0; i--)
        {
            if (delayed[i].time > now) continue;
            GameSfx id = delayed[i].id;
            delayed.RemoveAt(i);
            PlayNow(id);
        }
        float dt = Time.unscaledDeltaTime;
        bgm.Tick(dt);
        ambience.Tick(dt);
        ApplyVolumes();
    }

    private void LateUpdate()
    {
        if (clickPending)
        {
            clickPending = false;
            if (gameSoundFrame != Time.frameCount) PlayNow(GameSfx.UiClick);
        }
        if (volumesDirty && (Mouse.current == null || !Mouse.current.leftButton.isPressed)) FlushVolumes();
        UpdateFallbackListener();
    }

    private void ApplyVolumes()
    {
        if (library == null) return;
        float master = volumes[(int)Channel.Master];
        sfxSource.volume = master * volumes[(int)Channel.Sfx] * library.sfxLevel;
        bgm.SetBaseVolume(master * volumes[(int)Channel.Bgm] * library.bgmLevel);
        ambience.SetBaseVolume(master * volumes[(int)Channel.Ambience] * library.ambienceLevel);
    }

    // A click = press and release on the same interactable button/toggle (the uGUI click rule).
    private void DetectClick()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || EventSystem.current == null) { pressedClickable = null; return; }
        if (mouse.leftButton.wasPressedThisFrame) pressedClickable = ClickableUnderPointer(mouse);
        else if (mouse.leftButton.wasReleasedThisFrame)
        {
            if (pressedClickable != null && ClickableUnderPointer(mouse) == pressedClickable) clickPending = true;
            pressedClickable = null;
        }
    }

    private GameObject ClickableUnderPointer(Mouse mouse)
    {
        PointerEventData pointer = new PointerEventData(EventSystem.current) { position = mouse.position.ReadValue() };
        raycastResults.Clear();
        EventSystem.current.RaycastAll(pointer, raycastResults);
        if (raycastResults.Count == 0) return null;
        GameObject handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(raycastResults[0].gameObject);
        return handler != null && handler.TryGetComponent(out Selectable selectable) && selectable.IsInteractable()
            ? handler : null;
    }

    // Only while no camera carries an enabled AudioListener (main menu, or the moment between two regions).
    private void UpdateFallbackListener()
    {
        int count = Camera.allCamerasCount;
        if (cameraBuffer.Length < count) cameraBuffer = new Camera[count * 2];
        Camera.GetAllCameras(cameraBuffer);
        bool sceneListener = false;
        for (int i = 0; i < count && !sceneListener; i++)
            sceneListener = cameraBuffer[i] != null && cameraBuffer[i].TryGetComponent(out AudioListener listener)
                && listener.enabled && listener != fallbackListener;
        if (fallbackListener.enabled == sceneListener) fallbackListener.enabled = !sceneListener;
    }

    // Two sources per loop so the old sound fades out while the new one fades in.
    private sealed class LoopTrack
    {
        private readonly AudioSource[] sources;
        private readonly float[] gains = new float[2], targets = new float[2], groupVolumes = new float[2];
        private readonly AudioLibrary.SoundGroup[] groups = new AudioLibrary.SoundGroup[2];
        private int current = -1;
        private float fadeSpeed = 1f, baseVolume;

        public LoopTrack(AudioSource a, AudioSource b) => sources = new[] { a, b };

        public void Switch(AudioLibrary.SoundGroup group, float fadeSeconds)
        {
            fadeSpeed = fadeSeconds > 0f ? 1f / fadeSeconds : float.PositiveInfinity;
            if (group != null && !group.HasClip) group = null;
            if (current >= 0 && groups[current] == group && group != null && sources[current].isPlaying)
            {
                targets[current] = 1f; // Same scene sound (e.g. a failed load returning): keep it playing.
                return;
            }
            if (current >= 0) targets[current] = 0f;
            if (group == null) { current = -1; return; }
            // Reuse the quieter source; a sound still fading out there is cut where it is already faint.
            int next = current < 0 ? (gains[0] <= gains[1] ? 0 : 1) : 1 - current;
            AudioSource source = sources[next];
            source.Stop();
            source.clip = group.Pick();
            groups[next] = group;
            groupVolumes[next] = group.volume;
            gains[next] = 0f;
            targets[next] = 1f;
            current = next;
            if (source.clip == null) { current = -1; return; }
            source.volume = 0f;
            source.Play();
        }

        public void SetBaseVolume(float volume)
        {
            baseVolume = volume;
            for (int i = 0; i < 2; i++) sources[i].volume = baseVolume * groupVolumes[i] * gains[i];
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                if (!sources[i].isPlaying) continue;
                gains[i] = Mathf.MoveTowards(gains[i], targets[i], fadeSpeed * dt);
                if (gains[i] <= 0f && targets[i] <= 0f)
                {
                    sources[i].Stop();
                    sources[i].clip = null;
                    groups[i] = null;
                }
            }
        }
    }
}
