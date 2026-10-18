using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Farm <-> spirit realm travel. The farm scene stays loaded (its active roots are only hidden),
// so day, inventory, missions and animals are exactly as they were when the player returns.
// Opening the spirit scene on its own falls back to loading the farm scene normally.
public static class SpiritRealmTravel
{
    private static readonly List<GameObject> hiddenOriginRoots = new List<GameObject>();
    private static Scene originScene;
    private static Scene spiritScene;
    private static bool busy;

    public static bool IsTraveling => busy;
    public static bool CameFromOrigin => originScene.IsValid() && originScene.isLoaded && spiritScene.IsValid();

    public static void Enter(string spiritSceneName)
    {
        if (busy) return;
        if (!Application.CanStreamedLevelBeLoaded(spiritSceneName))
        {
            Debug.LogError($"'{spiritSceneName}' 씬을 불러올 수 없습니다. File > Build Profiles(Build Settings)의 Scene 목록에 추가해 주세요.");
            return;
        }
        if (SceneManager.GetSceneByName(spiritSceneName).isLoaded) return;

        busy = true;
        originScene = SceneManager.GetActiveScene();
        // Hide first so two cameras, audio listeners and event systems never run together.
        hiddenOriginRoots.Clear();
        foreach (GameObject root in originScene.GetRootGameObjects())
        {
            if (!root.activeSelf) continue;
            hiddenOriginRoots.Add(root);
            root.SetActive(false);
        }

        AsyncOperation load = SceneManager.LoadSceneAsync(spiritSceneName, LoadSceneMode.Additive);
        if (load == null)
        {
            RestoreOriginRoots();
            busy = false;
            return;
        }
        load.completed += _ =>
        {
            spiritScene = SceneManager.GetSceneByName(spiritSceneName);
            if (spiritScene.IsValid()) SceneManager.SetActiveScene(spiritScene);
            busy = false;
        };
    }

    public static void Return(string fallbackFarmSceneName)
    {
        if (busy) return;
        if (!CameFromOrigin)
        {
            // Spirit scene was opened directly: start the farm scene fresh.
            SceneManager.LoadScene(fallbackFarmSceneName, LoadSceneMode.Single);
            return;
        }

        busy = true;
        Scene leaving = spiritScene;
        foreach (GameObject root in leaving.GetRootGameObjects()) root.SetActive(false);
        RestoreOriginRoots();
        SceneManager.SetActiveScene(originScene);
        spiritScene = default;
        AsyncOperation unload = SceneManager.UnloadSceneAsync(leaving);
        if (unload == null) { busy = false; return; }
        unload.completed += _ => busy = false;
    }

    private static void RestoreOriginRoots()
    {
        foreach (GameObject root in hiddenOriginRoots)
            if (root != null) root.SetActive(true);
        hiddenOriginRoots.Clear();
    }

    // Runtime-only reset for play sessions started with domain reload disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay()
    {
        hiddenOriginRoots.Clear();
        originScene = default;
        spiritScene = default;
        busy = false;
    }
}
