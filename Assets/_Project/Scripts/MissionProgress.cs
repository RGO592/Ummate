using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerInventory))]
public sealed class MissionProgress : MonoBehaviour
{
    [SerializeField] private ItemDefinition basket;
    [SerializeField] private ItemDefinition cornSeeds;
    [SerializeField] private GameObject chickenArea;
    [SerializeField] private GameObject cowArea;
    [SerializeField] private TMP_Text missionLabel;

    private PlayerInventory inventory;
    private bool processingHarvest;
    private bool processingEggCollection;
    public int WheatHarvested { get; private set; }
    public bool M01Completed { get; private set; }
    public int EggsCollected { get; private set; }
    public bool M02Completed { get; private set; }
    public string CurrentMissionText => M02Completed ? "현재 미션: 옥수수 수확 0 / 9" : M01Completed
        ? $"현재 미션: 달걀 수집 {EggsCollected} / 4"
        : $"현재 미션: 밀 수확 {WheatHarvested} / 9";

    private void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        if (chickenArea != null) chickenArea.SetActive(false);
        if (cowArea != null) cowArea.SetActive(false);
        Refresh();
    }

    // Called only after FarmPlot has granted the actual wheat harvest.
    // Inventory starting items, test additions, and returned seeds do not call this.
    public void RecordWheatHarvest(int amount)
    {
        if (amount <= 0 || M01Completed || processingHarvest) return;
        processingHarvest = true;
        try
        {
            WheatHarvested = (int)System.Math.Min(9L, (long)WheatHarvested + amount);
            if (WheatHarvested == 9)
            {
                if (inventory == null || basket == null || chickenArea == null ||
                    !inventory.AddItem(basket, 1))
                {
                    Debug.LogWarning("M01 보상 지급 불가: 연결 또는 인벤토리 수량 한도를 확인해 주세요.", this);
                    Refresh();
                    return;
                }
                M01Completed = true;
                chickenArea.SetActive(true);
                Debug.Log("M01 완료: 닭 목장 해금, 닭 2마리 활성화, 바구니 1개 지급. 다음 미션: 달걀 수집 0 / 4", this);
            }
            Refresh();
        }
        finally { processingHarvest = false; }
    }

    // Inventory additions do not count; only a successful EggPickup calls this.
    public void RecordEggCollection(int amount)
    {
        if (!M01Completed || M02Completed || amount <= 0 || processingEggCollection) return;
        processingEggCollection = true;
        try
        {
            EggsCollected = (int)System.Math.Min(4L, (long)EggsCollected + amount);
            if (EggsCollected == 4)
            {
                if (inventory == null || cornSeeds == null || !inventory.AddItem(cornSeeds, 3))
                {
                    Debug.LogWarning("M02 보상 지급 불가: 옥수수 씨앗 연결 또는 인벤토리 수량 한도를 확인해 주세요.", this);
                    Refresh();
                    return;
                }
                M02Completed = true;
                Debug.Log("M02 완료: 옥수수 씨앗 3개 지급. 달걀은 소비하지 않습니다. 다음 미션: 옥수수 수확 0 / 9", this);
            }
            Refresh();
        }
        finally { processingEggCollection = false; }
    }

    private void Refresh()
    {
        if (missionLabel != null) missionLabel.text = CurrentMissionText;
    }
}
