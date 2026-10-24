using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Presentation only. FarmScene remains loaded but inactive during regional scene travel.
// Read its GameDay without creating another clock or changing save / mission state.
[DisallowMultipleComponent]
public sealed class SpiritCommonHUD : MonoBehaviour
{
    [Header("Date")]
    [SerializeField] private GameDay gameDay;
    [SerializeField] private TMP_Text dayText;
    [SerializeField, Min(1)] private int dateFontSize = 22;
    [SerializeField, Min(1), Tooltip("Preview date when this scene is opened without FarmScene.")]
    private int standaloneDay = 1;

    [Header("Gold (FarmScene player's PlayerWallet)")]
    [SerializeField] private TMP_Text goldText;

    [Header("Spirit guidance")]
    [SerializeField] private SpiritBuildModeController buildMode;
    [SerializeField] private EcoPlacementController placement;
    [SerializeField] private TMP_Text questText;
    [SerializeField, TextArea] private string questMessage = "현재 목표: 환경 칸을 배치해보세요.";
    [SerializeField] private TMP_Text tipText;
    [SerializeField, TextArea] private string exploreTip = "건설모드에서는 환경 칸을 설치할 수 있습니다.";
    [SerializeField, TextArea] private string buildTip = "아래 환경 슬롯을 드래그해 원하는 칸에 배치하세요.";
    [SerializeField, TextArea] private string moveTip = "배치된 환경물을 드래그해 다른 빈 칸으로 옮길 수 있습니다.";
    [SerializeField, TextArea] private string removeTip = "제거할 환경물을 선택하세요.";
    [SerializeField, Min(1)] private float charactersPerSecond = 35f;

    [Header("Existing Tip DEV subtext")]
    [SerializeField] private RectTransform mainTextArea;
    [SerializeField] private GameObject subTextArea;
    [SerializeField] private TMP_Text subText;
    [SerializeField] private float mainTextOffset = -7f;
    [SerializeField] private float developerMainTextOffset = 1f;

    private GameDay observedDay;
    private PlayerWallet observedWallet;
    private string displayedTip;
    private float visibleCharacters;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        EcoDevMode.Changed += RefreshDeveloperView;
        BindDay();
        BindWallet();
        if (questText != null) questText.text = questMessage;
        RefreshTip();
        RefreshDeveloperView();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        EcoDevMode.Changed -= RefreshDeveloperView;
        UnbindDay();
        UnbindWallet();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) { BindDay(); BindWallet(); }

    // Rebound on every enable, so returning from FarmScene shows the latest balance immediately.
    private void BindWallet()
    {
        UnbindWallet();
        foreach (PlayerWallet wallet in FindObjectsByType<PlayerWallet>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            observedWallet = wallet;
            break;
        }
        if (observedWallet != null) observedWallet.Changed += RefreshGold;
        RefreshGold();
    }

    private void UnbindWallet()
    {
        if (observedWallet != null) observedWallet.Changed -= RefreshGold;
        observedWallet = null;
    }

    private void RefreshGold()
    {
        if (goldText != null) goldText.text = observedWallet != null ? $"Gold: {observedWallet.Gold} G" : "Gold: - G";
    }

    private void BindDay()
    {
        UnbindDay();
        observedDay = gameDay;
        if (observedDay == null)
        {
            Scene farm = SceneManager.GetSceneByName("FarmScene");
            if (farm.IsValid() && farm.isLoaded)
                foreach (GameObject root in farm.GetRootGameObjects())
                {
                    observedDay = root.GetComponentInChildren<GameDay>(true);
                    if (observedDay != null) break;
                }
        }
        if (observedDay != null)
        {
            observedDay.DayChanged += OnDayChanged;
            observedDay.StateRestored += RefreshDate;
        }
        RefreshDate();
    }

    private void UnbindDay()
    {
        if (observedDay != null)
        {
            observedDay.DayChanged -= OnDayChanged;
            observedDay.StateRestored -= RefreshDate;
        }
        observedDay = null;
    }

    private void OnDayChanged(int previous, int current) => RefreshDate();

    private void RefreshDate()
    {
        int day = observedDay != null ? observedDay.CurrentDay : standaloneDay;
        if (dayText != null) dayText.text = $"<size={dateFontSize}><b>현재 {day}일차</b></size>";
    }

    private void LateUpdate()
    {
        RefreshTip();
        if (tipText == null || displayedTip == null) return;
        visibleCharacters = Mathf.Min(displayedTip.Length, visibleCharacters + Time.unscaledDeltaTime * charactersPerSecond);
        tipText.maxVisibleCharacters = Mathf.FloorToInt(visibleCharacters);
    }

    // Read existing interaction state only; no placement, camera or modal behavior is changed.
    private string CurrentTip()
    {
        if (buildMode == null || !buildMode.IsBuildMode) return exploreTip;
        if (placement != null && placement.RemoveMode) return removeTip;
        if (placement != null && placement.HasPicked) return moveTip;
        return buildTip;
    }

    private void RefreshTip()
    {
        string message = CurrentTip();
        // Reopening and DEV toggling do not restart the same sentence.
        if (tipText == null || displayedTip == message) return;
        displayedTip = message;
        tipText.text = message;
        visibleCharacters = 0;
        tipText.maxVisibleCharacters = 0;
    }

    private void RefreshDeveloperView()
    {
        bool enabled = EcoDevMode.FreePlacement;
        if (mainTextArea != null)
            mainTextArea.anchoredPosition = new Vector2(0, enabled ? developerMainTextOffset : mainTextOffset);
        if (subTextArea != null) subTextArea.SetActive(enabled);
        if (subText != null)
        {
            subText.text = "개발용: 환경물 무료·즉시 배치";
            subText.gameObject.SetActive(enabled);
        }
    }
}
