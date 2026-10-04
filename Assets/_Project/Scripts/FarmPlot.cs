using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class FarmPlot : Interactable
{
    public enum CropState { Empty, Growing, ReadyToHarvest }
    public enum CropKind { None, Wheat, Corn }

    [SerializeField] private GameDay gameDay;
    [SerializeField] private ItemDefinition wheatSeeds;
    [SerializeField] private ItemDefinition wheat;
    [SerializeField] private ItemDefinition cornSeeds;
    [SerializeField] private ItemDefinition corn;
    [SerializeField] private ItemDefinition wateringCan;
    [SerializeField] private Renderer plotRenderer;
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private string plotName = "밭";

    private CropState state;
    private int wateredDay = -1;
    private bool interacting;
    private MaterialPropertyBlock colors;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");

    public CropState State => state;
    public CropKind CurrentCrop { get; private set; }
    public int GrowthDays { get; private set; }
    public int RequiredGrowthDays => CurrentCrop == CropKind.Corn ? 1 : CurrentCrop == CropKind.Wheat ? 1 : 0;
    private string CropName => CurrentCrop == CropKind.Corn ? "옥수수" : "밀";
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
                ItemDefinition produce = CurrentCrop == CropKind.Corn ? corn : wheat;
                ItemDefinition seeds = CurrentCrop == CropKind.Corn ? cornSeeds : wheatSeeds;
                CropKind harvestedCrop = CurrentCrop;
                string harvestedName = CropName;
                // Check both rewards before granting either one.
                if (produce == null || seeds == null || inventory.GetQuantity(produce) > int.MaxValue - 3 ||
                    inventory.GetQuantity(seeds) == int.MaxValue)
                {
                    Log("인벤토리 수량 한도로 수확할 수 없습니다.");
                    return;
                }
                state = CropState.Empty;
                wateredDay = -1;
                CurrentCrop = CropKind.None;
                GrowthDays = 0;
                inventory.AddItem(produce, 3);
                inventory.AddItem(seeds, 1);
                MissionProgress mission = interactor.GetComponent<MissionProgress>();
                if (mission != null)
                {
                    if (harvestedCrop == CropKind.Corn) mission.RecordCornHarvest(3);
                    else mission.RecordWheatHarvest(3);
                }
                RefreshAppearance();
                Log($"수확 완료: {harvestedName} 3개 + {harvestedName} 씨앗 1개. 빈 밭으로 돌아갑니다.");
                return;
            }

            PlayerQuickbar quickbar = interactor.GetComponent<PlayerQuickbar>();
            ItemDefinition selected = quickbar != null ? quickbar.SelectedItem : null;
            if (state == CropState.Empty)
            {
                bool plantingCorn = cornSeeds != null && selected == cornSeeds;
                if (selected != wheatSeeds && !plantingCorn)
                {
                    Log("빈 밭: 밀 씨앗 또는 해금된 옥수수 씨앗을 선택하고 G를 눌러 주세요.");
                    return;
                }
                if (plantingCorn)
                {
                    MissionProgress mission = interactor.GetComponent<MissionProgress>();
                    if (mission == null || !mission.M02Completed || corn == null)
                    {
                        Log("M02 달걀 수집 미션을 완료해야 옥수수를 심을 수 있습니다.");
                        return;
                    }
                }
                if (!inventory.TryConsume(selected, 1))
                {
                    Log("선택한 씨앗이 부족합니다.");
                    return;
                }
                state = CropState.Growing;
                CurrentCrop = plantingCorn ? CropKind.Corn : CropKind.Wheat;
                GrowthDays = 0;
                wateredDay = -1;
                RefreshAppearance();
                Log($"{CropName} 파종 완료: 씨앗 1개 소비. 성장에 물 준 날 {RequiredGrowthDays}일이 필요합니다.");
                return;
            }

            if (selected != wateringCan)
            {
                Log($"{CropName} 성장 중 ({GrowthDays}/{RequiredGrowthDays}). 다시 심거나 수확할 수 없습니다. 물뿌리개를 선택해 주세요.");
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
            Log($"{wateredDay}일차 물 주기 완료. 취침하면 {CropName} 성장량이 1 증가합니다.");
        }
        finally { interacting = false; }
    }

    private void OnDayChanged(int previousDay, int newDay)
    {
        if (state == CropState.Growing)
        {
            if (wateredDay == previousDay)
            {
                GrowthDays = Mathf.Min(GrowthDays + 1, RequiredGrowthDays);
                if (GrowthDays == RequiredGrowthDays)
                {
                    state = CropState.ReadyToHarvest;
                    Log($"{newDay}일차: {CropName} 성장 완료! G로 수확할 수 있습니다.");
                }
                else Log($"{newDay}일차: {CropName} 성장 {GrowthDays}/{RequiredGrowthDays}.");
            }
            else Log($"{newDay}일차: 물을 주지 않아 성장 중 상태를 유지합니다.");
        }
        wateredDay = -1;
        RefreshAppearance();
    }

    private void RefreshAppearance()
    {
        if (statusLabel != null)
            statusLabel.text = state == CropState.Empty ? $"{plotName}: 빈 밭" :
                state == CropState.ReadyToHarvest ? $"{plotName}: {CropName} 수확 가능" :
                $"{plotName}: {CropName} {GrowthDays}/{RequiredGrowthDays} · {(WateredToday ? "물 줌" : "물 필요")}";
        if (plotRenderer == null) return;
        // Only tint this plot; never edit the shared material or create crop models.
        Color tint = state == CropState.Empty ? new Color(0.42f, 0.22f, 0.08f) :
            state == CropState.ReadyToHarvest ? new Color(1f, 0.72f, 0.08f) :
            WateredToday ? new Color(0.05f, 0.55f, 0.85f) : new Color(0.25f, 0.75f, 0.18f);
        if (CurrentCrop == CropKind.Corn)
            tint = state == CropState.ReadyToHarvest ? new Color(1f, 0.35f, 0.05f) :
                WateredToday ? new Color(0.65f, 0.5f, 0.9f) : new Color(0.5f, 0.2f, 0.65f);
        if (colors == null) colors = new MaterialPropertyBlock();
        plotRenderer.GetPropertyBlock(colors);
        colors.SetColor(BaseColor, tint);
        colors.SetColor(ColorProperty, tint);
        plotRenderer.SetPropertyBlock(colors);
    }

    private void Log(string message) => Debug.Log($"{name}: {message}", this);
}
