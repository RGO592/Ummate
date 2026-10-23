using System;
using UnityEngine;

// Lives on the FarmScene player next to PlayerInventory. FarmScene stays loaded (only suspended)
// during regional travel, so the balance survives scene round trips without DontDestroyOnLoad.
[DisallowMultipleComponent]
public sealed class PlayerWallet : MonoBehaviour
{
    [SerializeField, Min(0), Tooltip("테스트용 시작 골드. 세이브를 불러오면 저장된 값으로 덮어씁니다.")]
    private int startingGold;
    [SerializeField, Min(0), Tooltip("실행 중 현재 골드 (Inspector 확인용).")]
    private int currentGold;
    private bool initialized;
    public event Action Changed;

    public int Gold { get { Initialize(); return currentGold; } }

    private void Awake() => Initialize();

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;
        currentGold = Mathf.Max(0, startingGold);
    }

    public bool HasGold(int amount) => amount >= 0 && Gold >= amount;

    public bool AddGold(int amount)
    {
        if (amount <= 0 || amount > int.MaxValue - Gold) return false;
        currentGold += amount;
        Changed?.Invoke();
        return true;
    }

    // Atomic: an insufficient balance leaves the wallet unchanged.
    public bool TrySpendGold(int amount)
    {
        if (amount <= 0 || !HasGold(amount)) return false;
        currentGold -= amount;
        Changed?.Invoke();
        return true;
    }

    // Save/load only. Gameplay code uses AddGold / TrySpendGold.
    public void RestoreGold(int gold)
    {
        initialized = true;
        currentGold = Mathf.Max(0, gold);
        Changed?.Invoke();
    }
}
