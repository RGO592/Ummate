using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

// Keeps exactly one active EventSystem while scenes overlap. Every gameplay scene and the main menu carry their own
// EventSystem (with its InputSystemUIInputModule), and scenes are loaded additively: the main menu stays loaded
// until the village it opened is ready, and regions load next to the suspended village. The overlap would give two
// active EventSystems. A scene that loads while another EventSystem is already active parks its own (module and
// system disabled, nothing destroyed); once the other scene is gone, the parked one takes over at once.
// Event driven only (scene load / unload / active change): no per-frame search.
public static class EventSystemGuard
{
    private static readonly List<EventSystem> parked = new List<EventSystem>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Install()
    {
        parked.Clear();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    // Runs after the new scene's Awake/OnEnable and before its first Update, so its EventSystem never runs alongside.
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (EventSystem system in root.GetComponentsInChildren<EventSystem>(true))
                if (system.isActiveAndEnabled && HasOtherActive(system)) Park(system);
    }

    private static void OnSceneUnloaded(Scene scene) => Reconcile();
    private static void OnActiveSceneChanged(Scene previous, Scene next) => Reconcile();

    // Brings parked EventSystems back without ever making a second active one. A parked one in a suspended scene
    // (inactive objects) is re-enabled too: it only becomes active when its scene resumes, after the other one's
    // scene was suspended.
    private static void Reconcile()
    {
        parked.RemoveAll(system => system == null);
        for (int i = parked.Count - 1; i >= 0; i--)
        {
            EventSystem system = parked[i];
            if (system.gameObject.activeInHierarchy && HasOtherActive(system)) continue;
            Unpark(system);
            parked.RemoveAt(i);
        }
    }

    private static bool HasOtherActive(EventSystem system)
    {
        foreach (EventSystem other in Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (other != system && other.isActiveAndEnabled) return true;
        return false;
    }

    private static void Park(EventSystem system)
    {
        system.enabled = false;
        foreach (BaseInputModule module in system.GetComponents<BaseInputModule>()) module.enabled = false;
        if (!parked.Contains(system)) parked.Add(system);
    }

    private static void Unpark(EventSystem system)
    {
        // Modules first: the EventSystem collects its enabled modules when it is enabled.
        foreach (BaseInputModule module in system.GetComponents<BaseInputModule>()) module.enabled = true;
        system.enabled = true;
    }
}
