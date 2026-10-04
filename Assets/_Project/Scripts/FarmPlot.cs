using UnityEngine;

[DisallowMultipleComponent]
public sealed class FarmPlot : Interactable
{
    public enum CropState { Empty, Growing, ReadyToHarvest }

    [SerializeField] private GameDay gameDay;
    [SerializeField] private ItemDefinition wheatSeeds;
    [SerializeField] private ItemDefinition wheat;
    [SerializeField] private ItemDefinition wateringCan;
    [SerializeField] private Renderer plotRenderer;

    private CropState state;
    private int wateredDay = -1;
    private bool interacting;
    private MaterialPropertyBlock colors;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");

    public CropState State => state;
    public bool WateredToday => gameDay != null && wateredDay == gameDay.CurrentDay;

    private void Awake()
    {
        if (plotRenderer == null) plotRenderer = GetComponent<Renderer>();
        RefreshAppearance();
    }

    private void OnEnable()
    {
        if (gameDay != null) gameDay.DayChanged += OnDayChanged;
    }

    private void OnDisable()
    {
        if (gameDay != null) gameDay.DayChanged -= OnDayChanged;
    }

    public override void Interact(GameObject interactor)
    {
        if (interacting || interactor == null) return;
        PlayerInventory inventory = interactor.GetComponent<PlayerInventory>();
        if (inventory == null) return;
        if (gameDay == null || wheatSeeds == null || wheat == null || wateringCan == null)
        {
            Debug.LogWarning($"{name}: 밭의 날짜 및 아이템 연결을 확인해 주세요.", this);
            return;
        }

        // Inventory events must not trigger a second operation during the first one.
        interacting = true;
        try
        {
            if (state == CropState.ReadyToHarvest)
            {
                // Check both rewards before granting either one.
                if (inventory.GetQuantity(wheat) > int.MaxValue - 3 ||
                    inventory.GetQuantity(wheatSeeds) == int.MaxValue)
                {
                    Log("인벤토리 수량 한도로 수확할 수 없습니다.");
                    return;
                }
                state = CropState.Empty;
                wateredDay = -1;
                inventory.AddItem(wheat, 3);
                inventory.AddItem(wheatSeeds, 1);
                RefreshAppearance();
                Log("수확 완료: 밀 3개 + 밀 씨앗 1개. 빈 밭으로 돌아갑니다.");
                return;
            }

            PlayerQuickbar quickbar = interactor.GetComponent<PlayerQuickbar>();
            ItemDefinition selected = quickbar != null ? quickbar.SelectedItem : null;
            if (state == CropState.Empty)
            {
                if (selected != wheatSeeds)
                {
                    Log("빈 밭: 밀 씨앗을 선택하고 G를 눌러 주세요.");
                    return;
                }
                if (!inventory.TryConsume(wheatSeeds, 1))
                {
                    Log("밀 씨앗이 부족합니다.");
                    return;
                }
                state = CropState.Growing;
                wateredDay = -1;
                RefreshAppearance();
                Log("파종 완료: 밀 씨앗 1개 소비. 물뿌리개로 물을 주세요.");
                return;
            }

            if (selected != wateringCan)
            {
                Log("밀이 성장 중입니다. 다시 심거나 수확할 수 없습니다. 물뿌리개를 선택해 주세요.");
                return;
            }
            if (inventory.GetQuantity(wateringCan) < 1)
            {
                Log("물뿌리개를 보유하고 있지 않습니다.");
                return;
            }
            if (WateredToday)
            {
                Log("오늘은 이미 물을 줬습니다.");
                return;
            }
            wateredDay = gameDay.CurrentDay;
            RefreshAppearance();
            Log($"{wateredDay}일차 물 주기 완료. 취침하면 성장이 완료됩니다.");
        }
        finally { interacting = false; }
    }

    private void OnDayChanged(int previousDay, int newDay)
    {
        if (state == CropState.Growing)
        {
            if (wateredDay == previousDay)
            {
                state = CropState.ReadyToHarvest;
                Log($"{newDay}일차: 밀 성장 완료! G로 수확할 수 있습니다.");
            }
            else Log($"{newDay}일차: 물을 주지 않아 성장 중 상태를 유지합니다.");
        }
        wateredDay = -1;
        RefreshAppearance();
    }

    private void RefreshAppearance()
    {
        if (plotRenderer == null) return;
        // Only tint this plot; never edit the shared material or create crop models.
        Color tint = state == CropState.Empty ? new Color(0.42f, 0.22f, 0.08f) :
            state == CropState.ReadyToHarvest ? new Color(1f, 0.72f, 0.08f) :
            WateredToday ? new Color(0.05f, 0.55f, 0.85f) : new Color(0.25f, 0.75f, 0.18f);
        if (colors == null) colors = new MaterialPropertyBlock();
        plotRenderer.GetPropertyBlock(colors);
        colors.SetColor(BaseColor, tint);
        colors.SetColor(ColorProperty, tint);
        plotRenderer.SetPropertyBlock(colors);
    }

    private void Log(string message) => Debug.Log($"{name}: {message}", this);
}
