using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Optional scene-local alias for layout test scenes. Production scenes have no registration.
// Remains registered while travel suspends the source scene, and never edits the destination scene.
[DisallowMultipleComponent]
public sealed class WorkSceneReturnRoute : MonoBehaviour
{
    [SerializeField] private string destinationScene = "SpiritGridScene";
    [SerializeField] private string returnSceneAlias = "FarmScene";
    private static readonly List<WorkSceneReturnRoute> routes = new List<WorkSceneReturnRoute>();
    private static WorkSceneReturnRoute activeRoute;

    private void Awake()
    {
        if (!routes.Contains(this)) routes.Add(this);
        activeRoute = this; // Also supports restoring a saved region from this scene.
    }
    private void OnDestroy()
    {
        routes.Remove(this);
        if (activeRoute == this) activeRoute = null;
    }

    public static string Resolve(Scene source, string destination)
    {
        // Record which village actually departed. A normal FarmScene departure cancels
        // the test override even if an unrelated WorkScene happens to remain loaded.
        foreach (var route in routes)
        {
            if (route == null || !route.gameObject.scene.isLoaded) continue;
            if (route.gameObject.scene == source) { activeRoute = route; return destination; }
            if (route.returnSceneAlias == source.name || route.returnSceneAlias == source.path)
                activeRoute = null;
        }
        var selected = activeRoute;
        if (selected != null && selected.gameObject.scene.isLoaded
            && (selected.destinationScene == source.name || selected.destinationScene == source.path)
            && selected.returnSceneAlias == destination)
            return selected.gameObject.scene.path;
        return destination;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRoutes() { routes.Clear(); activeRoute = null; }
}
