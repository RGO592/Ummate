using System.Collections.Generic;
using UnityEngine;

// Every habitat the journal lists, in display order. Loaded from Resources/Habitats/HabitatCatalog so the journal
// works in any scene (the region's HabitatTracker, which judges them, may not be loaded).
[CreateAssetMenu(fileName = "HabitatCatalog", menuName = "Susupjung/Habitat Catalog")]
public sealed class HabitatCatalog : ScriptableObject
{
    public const string ResourcePath = "Habitats/HabitatCatalog";

    [SerializeField] private HabitatDefinition[] habitats = new HabitatDefinition[0];

    public IReadOnlyList<HabitatDefinition> Habitats => habitats ?? new HabitatDefinition[0];

    public static HabitatCatalog Load() => Resources.Load<HabitatCatalog>(ResourcePath);
}
