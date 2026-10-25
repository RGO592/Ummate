using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Keeps visited regions loaded but inactive, preserving their existing live game state.
// No save, new-game, day advancement or inventory initialization belongs in travel.
// Two ways in: Travel (a region exit → the destination's spawn point) and RestoreLocation (save load →
// the saved scene and player position, which win over every spawn point).
public sealed class SceneTransitionService : MonoBehaviour
{
    private const float RestoreFadeDuration = 0.3f;
    private static SceneTransitionService instance;
    private readonly Dictionary<int, List<GameObject>> suspendedRoots = new Dictionary<int, List<GameObject>>();
    private CanvasGroup curtain;
    private bool busy;

    public static bool IsTraveling => instance != null && instance.busy;
    // Raised once a region exit (Travel) has fully arrived in the destination scene. Save loads
    // (RestoreLocation) never raise it, so loading cannot count as entering a region.
    public static event Action<Scene> Arrived;

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
        EnsureInstance();
        instance.busy = true; // Set before the coroutine; multiple player colliders cannot start duplicate loads.
        instance.StartCoroutine(instance.Transition(source, localPlayer, destination, spawnId, Mathf.Max(0f, fadeDuration)));
    }

    // Save load, after GameSaveSystem applied the state. The active scene being the saved one only moves
    // its player (no reload); another scene is resumed or loaded like travel, with the player placed at
    // the saved position instead of a spawn point. No position (older saves) keeps a fresh scene's own
    // start position and returns a suspended one through its arrival spawn, as ordinary travel would.
    public static bool RestoreLocation(string destination, Vector3? position)
    {
        if (IsTraveling || string.IsNullOrWhiteSpace(destination)) return false;
        Scene current = SceneManager.GetActiveScene();
        Scene target = FindScene(destination);
        if (target.IsValid() && target.isLoaded && target == current)
        {
            if (!position.HasValue) return true;
            Transform player = FindPlayerRoot(target);
            if (player == null) return false;
            SceneSpawnPoint.MovePlayer(player, position.Value, player.rotation);
            ResetCameras(target);
            return true;
        }
        if (!Application.CanStreamedLevelBeLoaded(destination)) return false;
        EnsureInstance();
        instance.busy = true;
        // Black at once: the loaded state must never be shown in the scene being left.
        instance.EnsureCurtain();
        instance.curtain.alpha = 1f;
        instance.curtain.blocksRaycasts = true;
        instance.StartCoroutine(instance.Restore(current, destination, position));
        return true;
    }

    // The scene's player root: the Rigidbody object PlayerMovement drives (never its Visual child).
    public static Transform FindPlayerRoot(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        Transform found = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (PlayerMovement player in root.GetComponentsInChildren<PlayerMovement>(true))
            {
                if (found != null) { Debug.LogError($"'{scene.name}'에 플레이어(PlayerMovement)가 둘 이상 있습니다."); return null; }
                found = player.transform;
            }
        return found;
    }

    private static void EnsureInstance()
    {
        if (instance != null) return;
        GameObject runner = new GameObject("SceneTransitionService");
        instance = runner.AddComponent<SceneTransitionService>();
        DontDestroyOnLoad(runner);
    }

    private IEnumerator Restore(Scene source, string destination, Vector3? position)
    {
        Scene target = default;
        SceneSpawnPoint.InputLock targetLock = null;
        bool sourceHidden = false, completed = false;
        try
        {
            // Let the click or main-menu step that started the load finish before its scene is suspended.
            yield return null;
            if (source.IsValid() && source.isLoaded)
            {
                Suspend(source);
                sourceHidden = true;
            }
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

            Transform player = FindPlayerRoot(target);
            if (player == null)
            {
                Debug.LogError($"'{destination}'에서 플레이어를 찾을 수 없습니다. 출발 지역으로 복구합니다.");
                yield break;
            }
            SceneSpawnPoint arrival = FindPlayerSpawn(target);
            targetLock = arrival != null ? arrival.LockInput() : null;
            PlaceRestored(player, position, newlyLoaded ? null : arrival);
            SceneManager.SetActiveScene(target);
            Resume(target);
            yield return null; // Let a new scene finish Start while the screen is covered.
            targetLock?.Enforce();
            PlaceRestored(player, position, newlyLoaded ? null : arrival);
            ResetCameras(target);
            yield return null;
            yield return Fade(0f, RestoreFadeDuration);
            completed = true;
        }
        finally
        {
            targetLock?.Dispose();
            if (!completed && sourceHidden && source.IsValid() && source.isLoaded)
            {
                Debug.LogError($"저장된 지역 '{destination}'을 열지 못해 출발 지역으로 복구했습니다.");
                if (target.IsValid() && target.isLoaded && target != source) Suspend(target);
                SceneManager.SetActiveScene(source);
                Resume(source);
            }
            if (curtain != null) { curtain.alpha = 0f; curtain.blocksRaycasts = false; }
            busy = false;
        }
    }

    private static void PlaceRestored(Transform player, Vector3? position, SceneSpawnPoint fallback)
    {
        if (position.HasValue) SceneSpawnPoint.MovePlayer(player, position.Value, player.rotation);
        else if (fallback != null) fallback.PlacePlayer();
    }

    // Any spawn point of this scene's player: its input list locks the arrival, and it is the fallback
    // position for a suspended scene when the save has none.
    private static SceneSpawnPoint FindPlayerSpawn(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (SceneSpawnPoint point in root.GetComponentsInChildren<SceneSpawnPoint>(true))
                if (point.HasPlayer) return point;
        return null;
    }

    // CameraFollow resets its smoothing on enable, so a teleported player is not chased across the map.
    private static void ResetCameras(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (CameraFollow follow in root.GetComponentsInChildren<CameraFollow>(true))
                if (follow.isActiveAndEnabled) { follow.enabled = false; follow.enabled = true; }
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
            // A fresh camera may already have had one frame.
            ResetCameras(target);
            yield return null;
            yield return Fade(0f, duration);
            completed = true;
            // Listener errors must not undo a completed trip.
            try { Arrived?.Invoke(target); }
            catch (Exception exception) { Debug.LogException(exception); }
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
    private static void ResetSession()
    {
        instance = null;
        Arrived = null;
    }
}
