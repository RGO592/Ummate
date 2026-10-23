using UnityEngine;

[CreateAssetMenu(menuName = "Ummate/Item", fileName = "NewItem")]
public sealed class ItemDefinition : ScriptableObject
{
    [SerializeField] private string displayName;
    [Header("Trade (임시 테스트 값 — 기획 확정 가격 아님)")]
    [SerializeField] private bool canSell;
    [SerializeField, Min(0), Tooltip("교역소 기본 판매가(G). 임시 테스트 값입니다.")]
    private int sellPrice;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public bool CanSell => canSell && sellPrice > 0;
    // Base price only. A later quality multiplier belongs in TradeService, not here.
    public int BaseSellPrice => Mathf.Max(0, sellPrice);
}
