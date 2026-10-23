using System;
using System.Collections.Generic;
using UnityEngine;

// A destination and its local player, independent of save/new-game initialization.
[DisallowMultipleComponent]
public sealed class SceneSpawnPoint : MonoBehaviour
{
    [SerializeField] private string id;
    [SerializeField] private Transform playerRoot;
    [SerializeField, Tooltip("Movement, interaction and scene hotkey controllers paused during travel.")]
    private Behaviour[] inputBehaviours = new Behaviour[0];

    public string Id => id;
    public bool HasPlayer => playerRoot != null && playerRoot.GetComponent<Rigidbody>() != null;

    public bool IsPlayer(Collider other)
    {
        return HasPlayer && other != null && other.attachedRigidbody != null
            && other.attachedRigidbody.transform == playerRoot;
    }

    public InputLock LockInput() => new InputLock(this);

    public void PlacePlayer()
    {
        Rigidbody body = playerRoot.GetComponent<Rigidbody>();
        playerRoot.SetPositionAndRotation(transform.position, transform.rotation);
        body.position = transform.position;
        body.rotation = transform.rotation;
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        Physics.SyncTransforms();
    }

    public sealed class InputLock : IDisposable
    {
        private readonly List<Behaviour> enabledInputs = new List<Behaviour>();
        private readonly Rigidbody body;
        private readonly bool wasKinematic;
        private bool disposed;

        internal InputLock(SceneSpawnPoint point)
        {
            body = point.playerRoot.GetComponent<Rigidbody>();
            wasKinematic = body.isKinematic;
            foreach (Behaviour input in point.inputBehaviours)
                if (input != null && input.enabled && !enabledInputs.Contains(input)) enabledInputs.Add(input);
            Enforce();
        }

        // Reassert after a newly loaded scene's Start methods have initialized its controls.
        public void Enforce()
        {
            foreach (Behaviour input in enabledInputs) if (input != null) input.enabled = false;
            if (body == null) return;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (body != null) body.isKinematic = wasKinematic;
            foreach (Behaviour input in enabledInputs) if (input != null) input.enabled = true;
        }
    }
}
