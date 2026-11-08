using System;
using System.Collections.Generic;
using UnityEngine;

// Which clips the game plays and how loud (the mix). Loaded by AudioManager from
// Resources/Audio/AudioLibrary; edit that asset to retune volumes or swap clips (no scene wiring).
// Numbered files (_01, _02 …) of one sound are the candidates of one SoundGroup: one is picked at random each play.
[CreateAssetMenu(menuName = "Susupjung/Audio Library", fileName = "AudioLibrary")]
public sealed class AudioLibrary : ScriptableObject
{
    [Serializable]
    public sealed class SoundGroup
    {
        [Tooltip("Random candidates (_01, _02 …). Empty = silent.")]
        public AudioClip[] clips = new AudioClip[0];
        [Range(0f, 1f)] public float volume = 1f;
        [Min(0f), Tooltip("The same sound is not started again within this many seconds (overlap is otherwise allowed).")]
        public float minInterval = 0.03f;

        public bool HasClip
        {
            get
            {
                if (clips != null) foreach (AudioClip clip in clips) if (clip != null) return true;
                return false;
            }
        }

        public AudioClip Pick()
        {
            if (clips == null || clips.Length == 0) return null;
            int start = UnityEngine.Random.Range(0, clips.Length);
            for (int i = 0; i < clips.Length; i++)
            {
                AudioClip clip = clips[(start + i) % clips.Length];
                if (clip != null) return clip;
            }
            return null;
        }
    }

    [Serializable]
    public sealed class SfxEntry
    {
        public GameSfx id;
        public SoundGroup sound = new SoundGroup();
    }

    // Matched against the active scene's name. A scene with no entry fades both loops out.
    [Serializable]
    public sealed class SceneAudio
    {
        public string sceneName;
        public SoundGroup bgm = new SoundGroup();
        public SoundGroup ambience = new SoundGroup();
    }

    [Header("Mix levels (designer mix; player volume settings multiply on top)")]
    [Range(0f, 1f)] public float bgmLevel = 0.45f;
    [Range(0f, 1f)] public float ambienceLevel = 0.3f;
    [Range(0f, 1f)] public float sfxLevel = 1f;

    [Header("Scene loops")]
    [Min(0f)] public float bgmFadeSeconds = 1.5f;
    [Min(0f)] public float ambienceFadeSeconds = 2f;
    public SceneAudio[] scenes = new SceneAudio[0];

    [Header("One-shot sounds")]
    public SfxEntry[] sfx = new SfxEntry[0];

    [Header("Footsteps (timer based, no animation events)")]
    [Min(0.05f)] public float walkStepInterval = 0.42f;
    [Min(0.05f)] public float runStepInterval = 0.28f;
    [Min(0f), Tooltip("Delay before the first step after starting to move, so a short tap stays quiet.")]
    public float firstStepDelay = 0.08f;
    [Min(0f), Tooltip("Horizontal speed (m/s) below which the player counts as standing (e.g. pushing into a wall).")]
    public float minStepSpeed = 0.5f;

    [Header("Sequencing")]
    [Min(0f), Tooltip("A new quest announced right after a completion waits this long so the two jingles do not overlap.")]
    public float questStartAfterCompleteDelay = 1.2f;
    [Min(0f), Tooltip("Tree fall after the last axe hit.")]
    public float treeFallDelay = 0.25f;

    private Dictionary<GameSfx, SoundGroup> lookup;

    public SoundGroup Get(GameSfx id)
    {
        if (lookup == null)
        {
            lookup = new Dictionary<GameSfx, SoundGroup>();
            if (sfx != null)
                foreach (SfxEntry entry in sfx)
                    if (entry != null && entry.sound != null && !lookup.ContainsKey(entry.id)) lookup.Add(entry.id, entry.sound);
        }
        return lookup.TryGetValue(id, out SoundGroup group) ? group : null;
    }

    public SceneAudio FindScene(string sceneName)
    {
        if (scenes == null || string.IsNullOrEmpty(sceneName)) return null;
        foreach (SceneAudio scene in scenes)
            if (scene != null && scene.sceneName == sceneName) return scene;
        return null;
    }

    public static GameSfx HexSound(EnvironmentTileType type) => type switch
    {
        EnvironmentTileType.Mushroom => GameSfx.HexMushroom,
        EnvironmentTileType.Flower => GameSfx.HexFlower,
        EnvironmentTileType.Water => GameSfx.HexWater,
        EnvironmentTileType.Rock => GameSfx.HexRock,
        EnvironmentTileType.Tree => GameSfx.HexTree,
        _ => GameSfx.None
    };

    // Inspector edits during Play rebuild the lookup.
    private void OnValidate() => lookup = null;
}
