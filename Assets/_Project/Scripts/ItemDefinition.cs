using UnityEngine;

[CreateAssetMenu(menuName = "Susupjung/Item", fileName = "NewItem")]
public sealed class ItemDefinition : ScriptableObject
{
    [SerializeField] private string displayName;
    [Header("Trade (임시 테스트 값 — 기획 확정 가격 아님)")]
    [SerializeField] private bool canSell;
    [SerializeField, Min(0), Tooltip("교역소 기본 판매가(G). 임시 테스트 값입니다.")]
    private int sellPrice;
    [Header("Shop (교역소 구매 · 임시 테스트 값)")]
    [SerializeField, Tooltip("교역소 구매 목록(TradingPostUI Buy Catalog)에 올라 있을 때 구매할 수 있습니다.")]
    private bool canBuy;
    [SerializeField, Min(0), Tooltip("교역소 구매가(G). 임시 테스트 값입니다.")]
    private int buyPrice;
    [Header("Use (퀵슬롯에서 선택 후 E · 임시 테스트 값)")]
    [SerializeField, Min(0), Tooltip("사용하면 즉시 회복하는 기운. 0이면 사용 아이템이 아닙니다.")]
    private int energyRestore;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public bool CanSell => canSell && sellPrice > 0;
    // Base price only. A later quality multiplier belongs in TradeService, not here.
    public int BaseSellPrice => Mathf.Max(0, sellPrice);
    public bool CanBuy => canBuy && buyPrice > 0;
    public int BuyPrice => Mathf.Max(0, buyPrice);
    // Recovery items. Another recovery item only needs this value on its asset (see ItemUseService).
    public int EnergyRestore => Mathf.Max(0, energyRestore);
    public bool IsUsable => EnergyRestore > 0;
}
