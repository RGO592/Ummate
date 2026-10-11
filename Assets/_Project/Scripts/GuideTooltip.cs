using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class GuideTooltip : MonoBehaviour
{
    public enum GuideStep { PlantWheat, HarvestWheat, Water, Sleep, CollectEggs, FeedChicken,
        PlantCorn, HarvestCorn, CollectMilk, FeedCow, Complete }

    [Header("Read-only game references")]
    [SerializeField] private MissionProgress mission;
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private FarmPlot[] plots;
    [SerializeField] private ChickenFeeding[] chickens;
    [SerializeField] private EggPickup[] eggs;
    [SerializeField] private CowFeeding[] cows;
    [SerializeField] private ItemDefinition wheatSeeds, wheat, cornSeeds, corn;
    [Header("View")]
    [SerializeField] private GameObject tooltipPanel;
    [SerializeField] private TMP_Text mainText, subText;
    [SerializeField] private RectTransform mainTextArea, subTextArea;
    [SerializeField] private UnityEngine.UI.Button closeButton, openButton, developerButton;
    [SerializeField] private TMP_Text developerLabel;
    [SerializeField, Min(1)] private float charactersPerSecond = 35f;
    [SerializeField, Min(0.05f)] private float refreshInterval = 0.18f;
    private float nextRefresh, visibleCharacters;
    private string displayedMessage;
    public GuideStep CurrentStep { get; private set; }
    public bool DeveloperMode { get; private set; }

    private void Awake()
    {
        SetExpanded(true);
        UpdateDeveloperView();
    }

    private void OnEnable()
    {
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (openButton != null) openButton.onClick.AddListener(Open);
        if (developerButton != null) developerButton.onClick.AddListener(ToggleDeveloper);
        nextRefresh = 0;
    }

    private void OnDisable()
    {
        if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        if (openButton != null) openButton.onClick.RemoveListener(Open);
        if (developerButton != null) developerButton.onClick.RemoveListener(ToggleDeveloper);
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + refreshInterval;
            RefreshGuide();
        }
        if (mainText == null || displayedMessage == null) return;
        visibleCharacters = Mathf.Min(displayedMessage.Length, visibleCharacters + Time.unscaledDeltaTime * charactersPerSecond);
        mainText.maxVisibleCharacters = Mathf.FloorToInt(visibleCharacters);
    }

    private void RefreshGuide()
    {
        if (mission == null || inventory == null || mainText == null) return;
        CurrentStep = Evaluate();
        string message = Message(CurrentStep);
        // Polling, toggling DEV, and reopening must not restart the same sentence.
        if (message == displayedMessage) return;
        displayedMessage = message;
        mainText.text = message;
        visibleCharacters = 0;
        mainText.maxVisibleCharacters = 0;
    }

    private GuideStep Evaluate()
    {
        if (mission.PrologueCompleted) return GuideStep.Complete;
        if (!mission.M01Completed) return FarmGuide(FarmPlot.CropKind.Wheat, wheatSeeds);
        if (!mission.M02Completed)
        {
            foreach (var egg in eggs) if (egg != null && egg.isActiveAndEnabled && egg.IsAvailable) return GuideStep.CollectEggs;
            foreach (var chicken in chickens)
                if (chicken != null && chicken.isActiveAndEnabled && !chicken.FedToday)
                    return inventory.GetQuantity(wheat) > 0 ? GuideStep.FeedChicken : FarmGuide(FarmPlot.CropKind.Wheat, wheatSeeds);
            return GuideStep.Sleep;
        }
        if (!mission.M03Completed) return FarmGuide(FarmPlot.CropKind.Corn, cornSeeds);
        foreach (var cow in cows) if (cow != null && cow.isActiveAndEnabled && cow.MilkReady) return GuideStep.CollectMilk;
        foreach (var cow in cows)
            if (cow != null && cow.isActiveAndEnabled && !cow.FedToday)
                return inventory.GetQuantity(corn) >= 2 ? GuideStep.FeedCow : FarmGuide(FarmPlot.CropKind.Corn, cornSeeds);
        return GuideStep.Sleep;
    }

    private GuideStep FarmGuide(FarmPlot.CropKind crop, ItemDefinition seed)
    {
        bool empty = false, dry = false;
        foreach (var plot in plots)
        {
            if (plot == null || !plot.isActiveAndEnabled) continue;
            if (plot.CurrentCrop == crop && plot.State == FarmPlot.CropState.ReadyToHarvest)
                return crop == FarmPlot.CropKind.Wheat ? GuideStep.HarvestWheat : GuideStep.HarvestCorn;
            empty |= plot.State == FarmPlot.CropState.Empty;
            dry |= plot.CurrentCrop == crop && plot.State == FarmPlot.CropState.Growing && !plot.WateredToday;
        }
        if (empty && inventory.GetQuantity(seed) > 0)
            return crop == FarmPlot.CropKind.Wheat ? GuideStep.PlantWheat : GuideStep.PlantCorn;
        return dry ? GuideStep.Water : GuideStep.Sleep;
    }

    private static string Message(GuideStep step)
    {
        switch (step)
        {
            case GuideStep.PlantWheat: return "밀 씨앗을 심어보세요!";
            case GuideStep.HarvestWheat: return "밀을 수확하세요!";
            case GuideStep.Water: return "밭에 물을 주세요!";
            case GuideStep.CollectEggs: return "달걀을 모아보세요!";
            case GuideStep.FeedChicken: return "닭에게 먹이를 주세요!";
            case GuideStep.PlantCorn: return "옥수수 씨앗을 심어보세요!";
            case GuideStep.HarvestCorn: return "옥수수를 수확하세요!";
            case GuideStep.CollectMilk: return "우유를 모아보세요!";
            case GuideStep.FeedCow: return "소에게 먹이를 주세요!";
            case GuideStep.Complete: return "프롤로그 완료! 자유롭게 목장을 둘러보세요.";
            default: return "집에서 잠을 자세요.";
        }
    }

    public void Close() => SetExpanded(false);
    public void Open() { RefreshGuide(); SetExpanded(true); }
    private void SetExpanded(bool expanded)
    {
        if (tooltipPanel != null) tooltipPanel.SetActive(expanded);
        if (openButton != null) openButton.gameObject.SetActive(!expanded);
    }
    public void ToggleDeveloper() { DeveloperMode = !DeveloperMode; UpdateDeveloperView(); }
    private void UpdateDeveloperView()
    {
        if (mainTextArea != null)
            mainTextArea.anchoredPosition = new Vector2(0f, DeveloperMode ? 18f : 0f);
        if (subTextArea != null) subTextArea.gameObject.SetActive(DeveloperMode);
        if (subText != null)
        {
            subText.text = "개발용: T/Y로 선택 아이템 수량 조절";
            subText.gameObject.SetActive(DeveloperMode);
        }
        if (developerLabel != null) developerLabel.text = DeveloperMode ? "DEV\nON" : "DEV";
    }
}
