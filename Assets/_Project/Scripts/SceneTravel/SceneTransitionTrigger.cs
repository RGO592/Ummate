using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
public sealed class SceneTransitionTrigger : MonoBehaviour
{
    [SerializeField, Tooltip("Exact scene name or Assets/.../Scene.unity path included in Build Settings.")]
    private string targetScene;
    [SerializeField] private string targetSpawnPointId;
    [SerializeField, Tooltip("Identifies this scene's player; also holds the arrival point for the return trip.")]
    private SceneSpawnPoint localSpawnPoint;
    [SerializeField] private bool useFade = true;
    [SerializeField, Min(0f)] private float fadeDuration = 0.3f;

    private void Reset() => GetComponent<BoxCollider>().isTrigger = true;

    private void OnTriggerEnter(Collider other)
    {
        if (localSpawnPoint == null || !localSpawnPoint.IsPlayer(other)) return;
        if (other.attachedRigidbody.isKinematic) return; // No travel while construction freezes the player.
        SceneTransitionService.Travel(gameObject.scene, localSpawnPoint, targetScene,
            targetSpawnPointId, useFade ? fadeDuration : 0f);
    }
}
