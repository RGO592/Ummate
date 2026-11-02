using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// A place the navigation arrow can point at. Quest data only names the id, so assets never reference
// scene objects. Only enabled targets are listed: a suspended scene or a facility being moved drops out.
[DisallowMultipleComponent]
public sealed class QuestNavigationTarget : MonoBehaviour
{
    private static readonly List<QuestNavigationTarget> Enabled = new List<QuestNavigationTarget>();

    [SerializeField, Tooltip("Matches QuestDefinition.Objective.navigationTargetId. Several targets may share one id: the nearest is used.")]
    private string targetId;
    [SerializeField, Tooltip("Optional. This target is the way out to that scene, used when an objective's target is there.")]
    private string leadsToScene;
    [SerializeField, Tooltip("Half width (X) and half depth (Z) in m. Arrival distance is measured from this rectangle's edge. 0 = from the centre.")]
    private Vector2 arrivalHalfSize;

    public void Configure(string id, Vector2 halfSize)
    {
        targetId = id;
        arrivalHalfSize = halfSize;
    }

    private void OnEnable() => Enabled.Add(this);
    private void OnDisable() => Enabled.Remove(this);

    // XZ only: height never counts as being far away.
    public float DistanceFrom(Vector3 point)
    {
        Vector3 center = transform.position;
        float x = Mathf.Max(0f, Mathf.Abs(point.x - center.x) - arrivalHalfSize.x);
        float z = Mathf.Max(0f, Mathf.Abs(point.z - center.z) - arrivalHalfSize.y);
        return Mathf.Sqrt(x * x + z * z);
    }

    public static QuestNavigationTarget FindNearest(Scene scene, string id, Vector3 from) =>
        Nearest(scene, from, target => target.targetId == id);

    public static QuestNavigationTarget FindExitTo(Scene scene, string destination, Vector3 from) =>
        Nearest(scene, from, target => target.leadsToScene == destination);

    private static QuestNavigationTarget Nearest(Scene scene, Vector3 from, Predicate<QuestNavigationTarget> matches)
    {
        QuestNavigationTarget nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (QuestNavigationTarget target in Enabled)
        {
            if (target.gameObject.scene != scene || !matches(target)) continue;
            float distance = target.DistanceFrom(from);
            if (distance >= nearestDistance) continue;
            nearest = target;
            nearestDistance = distance;
        }
        return nearest;
    }
}
