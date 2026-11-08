// Every one-shot sound the game asks for. Saved in AudioLibrary as an int: append new values only, never reorder.
public enum GameSfx
{
    None = 0,
    // Farming
    TillField = 1, // No clip yet: stays silent until one is assigned in AudioLibrary.
    PlantSeed = 2,
    WaterCrop = 3,
    HarvestCrop = 4,
    PlantFlower = 5,
    WaterFlower = 6,
    HarvestFlower = 7,
    PlantSapling = 8,
    AxeHit = 9,
    TreeFall = 10,
    // Build / ecosystem
    PlaceFacility = 11,
    HexWheat = 12, // Retired with the Wheat Hex; value kept reserved (never reused).
    HexFlower = 13,
    HexWater = 14,
    HexRock = 15,
    HexTree = 16,
    HabitatDiscovered = 17,
    // UI
    QuestStart = 18,
    QuestComplete = 19,
    Sell = 20,
    Buy = 21,
    ActionFail = 22,
    UiClick = 23,
    MenuToggle = 24, // One sound for opening and closing.
    ItemGet = 25,
    // Day
    Sleep = 26,
    Morning = 27,
    // Player
    FootstepWalk = 28,
    FootstepRun = 29,
    // Build / ecosystem (added later)
    HexMushroom = 30
}
