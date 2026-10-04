using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerInventory))]
public sealed class MissionProgress : MonoBehaviour
{
    [SerializeField] private ItemDefinition basket;
    [SerializeField] private ItemDefinition cornSeeds;
    [SerializeField] private ItemDefinition bucket;
    [SerializeField] private GameObject chickenArea;
    [SerializeField] private GameObject cowArea;
    [SerializeField] private TMP_Text missionLabel;

    private PlayerInventory inventory;
    private bool processingHarvest;
    private bool processingEggCollection;
    private bool processingCornHarvest;
    private bool processingMilkCollection;
    public int WheatHarvested { get; private set; }
    public bool M01Completed { get; private set; }
    public int EggsCollected { get; private set; }
    public bool M02Completed { get; private set; }
    public int CornHarvested { get; private set; }
    public bool M03Completed { get; private set; }
    public int MilkCollected { get; private set; }
    public bool PrologueCompleted { get; private set; }
    public string CurrentMissionText => PrologueCompleted ? "프롤로그 완료" :
        M03Completed ? $"현재 미션: 우유 수집 {MilkCollected} / 4" :
        M02Completed ? $"현재 미션: 옥수수 수확 {CornHarvested} / 9" : M01Completed
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
                CompleteM01();
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
                CompleteM02();
            }
            Refresh();
        }
        finally { processingEggCollection = false; }
    }

    public void RecordCornHarvest(int amount)
    {
        if (!M02Completed || M03Completed || amount <= 0 || processingCornHarvest) return;
        processingCornHarvest = true;
        try
        {
            CornHarvested = (int)System.Math.Min(9L, (long)CornHarvested + amount);
            if (CornHarvested == 9)
            {
                CompleteM03();
            }
            Refresh();
        }
        finally { processingCornHarvest = false; }
    }

    // Only successful cow collection counts; inventory test additions do not count.
    public void RecordMilkCollection(int amount)
    {
        if (!M03Completed || PrologueCompleted || amount <= 0 || processingMilkCollection) return;
        processingMilkCollection = true;
        try
        {
            MilkCollected = (int)System.Math.Min(4L, (long)MilkCollected + amount);
            if (MilkCollected == 4) CompletePrologue();
            Refresh();
        }
        finally { processingMilkCollection = false; }
    }

    private void CompletePrologue()
    {
        if (!M03Completed || PrologueCompleted) return;
        PrologueCompleted = true;
        Debug.Log("프롤로그 완료! 보유 우유는 소비하지 않습니다.", this);
    }

    // Shared by real progress and development-only skipping. Completed rewards are never repeated.
    private void CompleteM01()
    {
        if (M01Completed) return;
        if (inventory == null || basket == null || chickenArea == null || !inventory.AddItem(basket, 1))
        {
            Debug.LogWarning("M01 보상 지급 불가: 연결 또는 인벤토리 수량 한도를 확인해 주세요.", this);
            return;
        }
        M01Completed = true;
        chickenArea.SetActive(true);
        Debug.Log("M01 완료: 닭 목장 해금, 닭 2마리 활성화, 바구니 1개 지급. 다음 미션: 달걀 수집 0 / 4", this);
    }

    private void CompleteM02()
    {
        if (!M01Completed || M02Completed) return;
        if (inventory == null || cornSeeds == null || !inventory.AddItem(cornSeeds, 3))
        {
            Debug.LogWarning("M02 보상 지급 불가: 옥수수 씨앗 연결 또는 인벤토리 수량 한도를 확인해 주세요.", this);
            return;
        }
        M02Completed = true;
        Debug.Log("M02 완료: 옥수수 씨앗 3개 지급. 달걀은 소비하지 않습니다. 다음 미션: 옥수수 수확 0 / 9", this);
    }

    private void CompleteM03()
    {
        if (!M02Completed || M03Completed) return;
        if (inventory == null || bucket == null || cowArea == null || !inventory.AddItem(bucket, 1))
        {
            Debug.LogWarning("M03 보상 지급 불가: 양동이/소 목장 연결 또는 수량 한도를 확인해 주세요.", this);
            return;
        }
        M03Completed = true;
        cowArea.SetActive(true);
        Debug.Log("M03 완료: 소 목장과 소 2마리 해금, 양동이 1개 지급. 옥수수는 소비하지 않습니다.", this);
    }

    public void DebugAdvanceMission()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!Application.isPlaying || !isActiveAndEnabled ||
            processingHarvest || processingEggCollection || processingCornHarvest || processingMilkCollection) return;
        if (PrologueCompleted)
        {
            Debug.Log("[개발용] 프롤로그가 완료되었습니다. 넘길 다음 미션이 없습니다.", this);
            return;
        }
        // Block re-entry from inventory callbacks without fabricating harvest/collection counts.
        processingHarvest = processingEggCollection = processingCornHarvest = processingMilkCollection = true;
        try
        {
            if (!M01Completed) CompleteM01();
            else if (!M02Completed) CompleteM02();
            else if (!M03Completed) CompleteM03();
            else CompletePrologue();
            Refresh();
            Debug.Log($"[개발용 미션 전환] {CurrentMissionText}", this);
        }
        finally { processingHarvest = processingEggCollection = processingCornHarvest = processingMilkCollection = false; }
#endif
    }

    private void Refresh()
    {
        if (missionLabel != null) missionLabel.text = CurrentMissionText;
    }
}
