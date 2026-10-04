using UnityEngine;

[DisallowMultipleComponent]
public sealed class CowFeeding : Interactable
{
    [SerializeField] private GameDay gameDay;
    [SerializeField] private ItemDefinition corn;
    [SerializeField] private ItemDefinition bucket;
    [SerializeField] private ItemDefinition milk;

    private int fedDay = -1;
    private bool interacting;
    public bool FedToday => gameDay != null && fedDay == gameDay.CurrentDay;
    public bool MilkReady { get; private set; }

    public bool SaveReferencesValid => gameDay != null;
    public GameSaveData.CowState CaptureState(string id) => new GameSaveData.CowState
    {
        id = id, fedToday = FedToday, milkReady = MilkReady
    };

    public void RestoreState(GameSaveData.CowState saved)
    {
        fedDay = saved.fedToday ? gameDay.CurrentDay : -1;
        MilkReady = saved.milkReady;
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
        if (!isActiveAndEnabled || interacting || interactor == null) return;
        if (gameDay == null || corn == null || bucket == null || milk == null)
        {
            Debug.LogWarning($"{name}: 날짜, 옥수수, 양동이, 우유 연결을 확인해 주세요.", this);
            return;
        }
        PlayerInventory inventory = interactor.GetComponent<PlayerInventory>();
        PlayerQuickbar quickbar = interactor.GetComponent<PlayerQuickbar>();
        if (inventory == null || quickbar == null) return;
        interacting = true;
        try
        {
            if (quickbar.SelectedItem == bucket)
            {
                if (inventory.GetQuantity(bucket) < 1)
                {
                    Log("양동이를 보유하고 선택해야 우유를 수집할 수 있습니다.");
                    return;
                }
                if (!MilkReady)
                {
                    Log("아직 수집할 우유가 없습니다. 옥수수 2개를 먹이고 취침해 주세요.");
                    return;
                }
                if (!inventory.AddItem(milk, 1))
                {
                    Log("인벤토리 수량 한도로 우유를 수집할 수 없습니다.");
                    return;
                }
                MilkReady = false;
                MissionProgress mission = interactor.GetComponent<MissionProgress>();
                if (mission != null) mission.RecordMilkCollection(1);
                Log($"우유 1병 수집 완료. 보유 수량: {inventory.GetQuantity(milk)}");
                return;
            }
            if (quickbar.SelectedItem != corn)
            {
                Log(MilkReady ? "우유가 준비되었습니다. 양동이를 보유하고 선택한 뒤 G로 수집하세요." :
                    "옥수수를 선택하고 G를 눌러 먹이를 주세요. 한 마리당 옥수수 2개가 필요합니다.");
                return;
            }
            if (FedToday)
            {
                Log("오늘은 이미 옥수수를 먹었습니다. 추가로 소비하지 않습니다.");
                return;
            }
            if (!inventory.TryConsume(corn, 2))
            {
                Log("옥수수가 2개 이상 필요합니다. 먹이를 소비하지 않았습니다.");
                return;
            }
            fedDay = gameDay.CurrentDay;
            Log($"{fedDay}일차 급여 완료: 옥수수 2개 소비. 다음 날 아침 우유를 생산합니다.");
        }
        finally { interacting = false; }
    }

    private void OnDayChanged(int previousDay, int newDay)
    {
        bool ateYesterday = fedDay == previousDay;
        fedDay = -1;
        if (!ateYesterday) return;
        // Like the chicken prototype, retain at most one uncollected product per animal.
        if (MilkReady)
        {
            Log("미수집 우유 1병을 유지합니다. 현재는 추가로 쌓이지 않습니다.");
            return;
        }
        MilkReady = true;
        Log($"{newDay}일차: 우유 1병 생산. 양동이를 선택하고 소 근처에서 G로 수집하세요.");
    }

    private void Log(string message) => Debug.Log($"{name}: {message}", this);
}
