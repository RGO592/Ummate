using System;
using System.Collections.Generic;
using UnityEngine;

// Runs a quest step on the next frame from an object that belongs to no scene. QuestLog lives on the village
// player, and the village is suspended (inactive) while a region is visited, so its own Update or coroutines
// would not run there (Q06 is completed in SpiritGridScene).
public sealed class QuestDeferredStep : MonoBehaviour
{
    private static QuestDeferredStep instance;
    private readonly List<(int frame, Action action)> queued = new List<(int, Action)>();
    private readonly List<Action> due = new List<Action>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => instance = null;

    // The action runs on a later frame than the one it was scheduled in.
    public static void NextFrame(Action action)
    {
        if (action == null) return;
        if (instance == null)
        {
            GameObject runner = new GameObject(nameof(QuestDeferredStep));
            DontDestroyOnLoad(runner);
            instance = runner.AddComponent<QuestDeferredStep>();
        }
        instance.queued.Add((Time.frameCount, action));
    }

    private void Update()
    {
        int frame = Time.frameCount;
        for (int i = 0; i < queued.Count; i++)
            if (queued[i].frame < frame) { due.Add(queued[i].action); queued.RemoveAt(i--); }
        foreach (Action action in due)
        {
            // A failing quest step must not stop the others.
            try { action(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
        due.Clear();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
