using UnityEngine;

// Dedicated flower bed, separate from FarmPlot crops. One flower kind for now.
// Seed -> water -> one real in-game day -> harvest -> empty again (a new seed is needed every cycle).
// Values marked "임시" are prototype test values, not final balance.
[DisallowMultipleComponent]
public sealed class FlowerBed : Interactable
{
    public enum BedState { Empty, Planted, Watered, ReadyToHarvest }

    [SerializeField] private GameDay gameDay;
    [SerializeField] private ItemDefinition flowerSeeds;
    [SerializeField] private ItemDefinition flower;
    [SerializeField, Min(1), Tooltip("심을 때 소비하는 씨앗 수 (임시 테스트 값).")]
    private int seedsPerPlanting = 1;
    [SerializeField, Min(1), Tooltip("수확할 때 얻는 꽃 수 (임시 테스트 값).")]
    private int harvestAmount = 2;
    [SerializeField, Tooltip("Temporary state tint target (the existing green plants). Final visuals come later.")]
    private Renderer plantsRenderer;

    private BedState state;
    private bool interacting;
    private MaterialPropertyBlock colors;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");

    public BedState State => state;
    public bool SaveReferencesValid => gameDay != null && flowerSeeds != null && flower != null;

    public GameSaveData.FlowerBedState CaptureState(string id) => new GameSaveData.FlowerBedState { id = id, state = state };

    // Load / new game only. Never treated as a day passing.
    public void RestoreState(GameSaveData.FlowerBedState saved)
    {
        state = saved != null ? saved.state : BedState.Empty;
        RefreshAppearance();
    }

    private void Awake() => RefreshAppearance();

    // Same pattern as FarmPlot: only GameDay.Sleep raises DayChanged. RestoreDay (F8/F9) raises
    // StateRestored instead, and scene travel raises neither, so only a real day grows flowers.
    private void OnEnable()
    {
        if (gameDay != null) gameDay.DayChanged += OnDayChanged;
    }

    private void OnDisable()
    {
        if (gameDay != null) gameDay.DayChanged -= OnDayChanged;
    }

    public override string GetInteractionPrompt(GameObject interactor) => state switch
    {
        BedState.Empty => "꽃 씨앗 심기",
        BedState.Planted => "물 주기",
        BedState.ReadyToHarvest => "꽃 수확하기",
        _ => null // Watered: growing until the next day, same as a watered FarmPlot.
    };

    public override void Interact(GameObject interactor)
    {
        if (interacting || interactor == null) return;
        PlayerInventory inventory = interactor.GetComponent<PlayerInventory>();
        if (inventory == null) return;
        if (!SaveReferencesValid)
        {
            Debug.LogWarning($"{name}: 화단의 날짜 및 꽃/꽃 씨앗 연결을 확인해 주세요.", this);
            return;
        }

        // Inventory events must not trigger a second operation during the first one.
        interacting = true;
        try
        {
            switch (state)
            {
                case BedState.Empty:
                    if (!inventory.TryConsume(flowerSeeds, seedsPerPlanting))
                    {
                        Report("꽃 씨앗이 없습니다.");
                        return;
                    }
                    SetState(BedState.Planted);
                    Report("꽃 씨앗을 심었습니다.");
                    return;
                case BedState.Planted:
                    SetState(BedState.Watered);
                    Report("꽃에 물을 주었습니다.");
                    return;
                case BedState.Watered:
                    Report("꽃이 자라는 중입니다. 하루가 지나면 수확할 수 있습니다.");
                    return;
                case BedState.ReadyToHarvest:
                    // Same overflow rule as FarmPlot: check before changing anything.
                    if (inventory.GetQuantity(flower) > int.MaxValue - harvestAmount)
                    {
                        Report("꽃을 더 받을 수 없습니다.");
                        return;
                    }
                    // Commit the bed first so synchronous inventory listeners cannot harvest twice.
                    SetState(BedState.Empty);
                    inventory.AddItem(flower, harvestAmount);
                    Report($"꽃 {harvestAmount}개를 획득했습니다.");
                    return;
            }
        }
        finally { interacting = false; }
    }

    private void OnDayChanged(int previousDay, int newDay)
    {
        // Planted but never watered stays planted.
        if (state != BedState.Watered) return;
        SetState(BedState.ReadyToHarvest);
        Debug.Log($"{name}: {newDay}일차 꽃 수확 가능", this);
    }

    private void SetState(BedState next)
    {
        state = next;
        RefreshAppearance();
    }

    private void Report(string message)
    {
        Debug.Log($"{name}: {message}", this);
        PlayerFeedback.Notify(message);
    }

    // Temporary tint only; never edits the shared material.
    private void RefreshAppearance()
    {
        if (plantsRenderer == null) return;
        Color tint = state == BedState.Empty ? new Color(0.42f, 0.3f, 0.18f) :
            state == BedState.Planted ? new Color(0.35f, 0.65f, 0.25f) :
            state == BedState.Watered ? new Color(0.15f, 0.55f, 0.8f) : new Color(0.95f, 0.45f, 0.7f);
        if (colors == null) colors = new MaterialPropertyBlock();
        plantsRenderer.GetPropertyBlock(colors);
        colors.SetColor(BaseColor, tint);
        colors.SetColor(ColorProperty, tint);
        plantsRenderer.SetPropertyBlock(colors);
    }
}
