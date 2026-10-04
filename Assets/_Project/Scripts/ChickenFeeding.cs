using UnityEngine;

[DisallowMultipleComponent]
public sealed class ChickenFeeding : Interactable
{
    [SerializeField] private GameDay gameDay;
    [SerializeField] private ItemDefinition wheat;
    [SerializeField] private EggPickup egg;

    private int fedDay = -1;
    private bool feeding;
    public bool FedToday => gameDay != null && fedDay == gameDay.CurrentDay;

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
        if (!isActiveAndEnabled || feeding || interactor == null) return;
        if (gameDay == null || wheat == null || egg == null)
        {
            Debug.LogWarning($"{name}: 날짜, 밀, 달걀 연결을 확인해 주세요.", this);
            return;
        }
        if (FedToday)
        {
            Log("오늘은 이미 밀을 먹었습니다. 추가로 소비하지 않습니다.");
            return;
        }
        PlayerInventory inventory = interactor.GetComponent<PlayerInventory>();
        PlayerQuickbar quickbar = interactor.GetComponent<PlayerQuickbar>();
        if (inventory == null || quickbar == null) return;
        if (quickbar.SelectedItem != wheat)
        {
            Log("밀을 선택하고 G를 눌러 먹이를 주세요.");
            return;
        }
        feeding = true;
        try
        {
            if (!inventory.TryConsume(wheat, 1))
            {
                Log("밀이 부족하여 먹이를 줄 수 없습니다.");
                return;
            }
            fedDay = gameDay.CurrentDay;
            Log($"{fedDay}일차 급여 완료: 밀 1개 소비. 다음 날 아침 달걀을 생산합니다.");
        }
        finally { feeding = false; }
    }

    private void OnDayChanged(int previousDay, int newDay)
    {
        bool ateYesterday = fedDay == previousDay;
        fedDay = -1;
        if (!ateYesterday || egg == null) return;
        if (egg.Produce()) Log($"{newDay}일차: 달걀 1개 생산. 바구니를 선택하고 달걀 근처에서 G로 수집하세요.");
        else Log("미수집 달걀 1개를 유지합니다. 현재는 추가로 쌓이지 않습니다.");
    }

    private void Log(string message) => Debug.Log($"{name}: {message}", this);
}
