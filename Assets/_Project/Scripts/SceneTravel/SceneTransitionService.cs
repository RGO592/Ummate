using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Keeps visited regions loaded but inactive, preserving their existing live game state.
// No save, new-game, day advancement or inventory initialization belongs in travel.
public sealed class SceneTransitionService : MonoBehaviour
{
    private static SceneTransitionService instance;
    private readonly Dictionary<int, List<GameObject>> suspendedRoots = new Dictionary<int, List<GameObject>>();
    private CanvasGroup curtain;
    private bool busy;

    public static bool IsTraveling => instance != null && instance.busy;

    public static void Travel(Scene source, SceneSpawnPoint localPlayer, string destination,
        string spawnId, float fadeDuration)
    {
        if (IsTraveling || !source.IsValid() || source != SceneManager.GetActiveScene()) return;
        if (localPlayer == null || !localPlayer.HasPlayer || string.IsNullOrWhiteSpace(spawnId)
            || string.IsNullOrWhiteSpace(destination) || !Application.CanStreamedLevelBeLoaded(destination))
        {
            Debug.LogError($"지역 이동 설정을 확인하세요: {destination} / {spawnId}");
            return;
        }
        if (destination == source.name || destination == source.path) return;
        if (instance == null)
        {
            GameObject runner = new GameObject("SceneTransitionService");
            instance = runner.AddComponent<SceneTransitionService>();
            DontDestroyOnLoad(runner);
        }
        instance.busy = true; // Set before the coroutine; multiple player colliders cannot start duplicate loads.
        instance.StartCoroutine(instance.Transition(source, localPlayer, destination, spawnId, Mathf.Max(0f, fadeDuration)));
    }

    private IEnumerator Transition(Scene source, SceneSpawnPoint localPlayer, string destination,
        string spawnId, float duration)
    {
        Scene target = default;
        SceneSpawnPoint.InputLock sourceLock = null, targetLock = null;
        bool sourceHidden = false, completed = false;
        try
        {
            EnsureCurtain();
            curtain.blocksRaycasts = true;
            sourceLock = localPlayer.LockInput();
            yield return Fade(1f, duration);
            Suspend(source);
            sourceHidden = true;
            sourceLock.Dispose();
            sourceLock = null;

            target = FindScene(destination);
            bool newlyLoaded = !target.IsValid() || !target.isLoaded;
            if (newlyLoaded)
            {
                AsyncOperation loading = BeginLoad(destination);
                if (loading == null) yield break;
                yield return loading;
                target = FindScene(destination);
            }
            if (!target.IsValid() || !target.isLoaded) yield break;

            SceneSpawnPoint arrival = FindSpawn(target, spawnId);
            if (arrival == null || !arrival.HasPlayer)
            {
                Debug.LogError($"'{destination}'에 유효한 Spawn Point '{spawnId}'가 없습니다. 출발 지역으로 복구합니다.");
                yield break;
            }
            targetLock = arrival.LockInput();
            arrival.PlacePlayer(); // Cached players are repositioned before their cameras are re-enabled.
            SceneManager.SetActiveScene(target);
            Resume(target);
            yield return null; // Let a new scene finish Start while the screen is covered.
            targetLock.Enforce();
            arrival.PlacePlayer();
            // CameraFollow resets its smoothing on enable; a fresh camera may already have had one frame.
            foreach (GameObject root in target.GetRootGameObjects())
                foreach (CameraFollow follow in root.GetComponentsInChildren<CameraFollow>(true))
                    if (follow.isActiveAndEnabled) { follow.enabled = false; follow.enabled = true; }
            yield return null;
            yield return Fade(0f, duration);
            completed = true;
        }
        finally
        {
            targetLock?.Dispose();
            sourceLock?.Dispose();
            if (!completed && sourceHidden && source.IsValid() && source.isLoaded)
            {
                if (target.IsValid() && target.isLoaded) Suspend(target);
                SceneManager.SetActiveScene(source);
                Resume(source);
            }
            if (curtain != null) { curtain.alpha = 0f; curtain.blocksRaycasts = false; }
            busy = false;
        }
    }

    private static AsyncOperation BeginLoad(string destination)
    {
        try { return SceneManager.LoadSceneAsync(destination, LoadSceneMode.Additive); }
        catch (Exception exception) { Debug.LogException(exception); return null; }
    }

    private static Scene FindScene(string destination)
    {
        Scene scene = SceneManager.GetSceneByPath(destination);
        return scene.IsValid() ? scene : SceneManager.GetSceneByName(destination);
    }

    private static SceneSpawnPoint FindSpawn(Scene scene, string id)
    {
        SceneSpawnPoint found = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (SceneSpawnPoint point in root.GetComponentsInChildren<SceneSpawnPoint>(true))
            {
                if (point.Id != id) continue;
                if (found != null) { Debug.LogError($"중복 Spawn Point ID: {id}"); return null; }
                found = point;
            }
        return found;
    }

    private void Suspend(Scene scene)
    {
        if (suspendedRoots.ContainsKey(scene.handle)) return;
        List<GameObject> roots = new List<GameObject>();
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.activeSelf) { roots.Add(root); root.SetActive(false); }
        suspendedRoots.Add(scene.handle, roots);
    }

    private void Resume(Scene scene)
    {
        if (!suspendedRoots.TryGetValue(scene.handle, out List<GameObject> roots)) return;
        suspendedRoots.Remove(scene.handle);
        foreach (GameObject root in roots) if (root != null) root.SetActive(true);
    }

    private IEnumerator Fade(float alpha, float duration)
    {
        float from = curtain.alpha;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            curtain.alpha = Mathf.Lerp(from, alpha, Mathf.SmoothStep(0f, 1f, elapsed / duration));
            yield return null;
        }
        curtain.alpha = alpha;
    }

    private void EnsureCurtain()
    {
        if (curtain != null) return;
        GameObject canvasObject = new GameObject("RegionTransitionFade", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasGroup), typeof(UnityEngine.UI.GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;
        curtain = canvasObject.GetComponent<CanvasGroup>();
        curtain.alpha = 0f;
        GameObject imageObject = new GameObject("Black", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        imageObject.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = (RectTransform)imageObject.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        imageObject.GetComponent<UnityEngine.UI.Image>().color = Color.black;
    }

    private void Awake() => SceneManager.sceneUnloaded += ForgetScene;
    private void ForgetScene(Scene scene) => suspendedRoots.Remove(scene.handle);
    private void OnDestroy()
    {
        SceneManager.sceneUnloaded -= ForgetScene;
        if (instance == this) instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => instance = null;
}
