using UnityEngine;

// Release only the mesh owned by this generated visual, never imported/shared assets.
public sealed class HexGeneratedMesh : MonoBehaviour
{
    public Mesh OwnedMesh { get; set; }
    private void OnDestroy() { if (OwnedMesh != null) Destroy(OwnedMesh); }
}
