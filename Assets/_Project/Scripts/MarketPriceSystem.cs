using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

// Daily market prices for items the player sells. ItemDefinition.sellPrice stays the base price;
// today's price = round(base × today's multiplier). Each item walks independently: one real DayChanged
// (sleep) = one step of a bounded random walk. Scene travel, save loads and StateRestored never move prices.
// Lives in the village scene next to GameDay (stays loaded while suspended during regional travel).
// Buy prices (ItemDefinition.buyPrice) are not affected.
// Values marked "임시" are prototype test values, not final balance.
[DisallowMultipleComponent]
public sealed class MarketPriceSystem : MonoBehaviour
{
    // Runtime state per item. Serialized only so the Inspector shows live prices during Play.
    [Serializable]
    public sealed class Entry
    {
        public ItemDefinition item;
        public float currentMultiplier = 1f;
        public float previousMultiplier = 1f;
        [Tooltip("Oldest first; the last value is today.")]
        public List<float> history = new List<float>();
    }

    [Header("Market items (판매 아이템 · 기준가 = 각 ItemDefinition의 Sell Price)")]
    [SerializeField, Tooltip("시세가 움직이는 아이템. 판매가(Sell Price)가 0인 아이템은 무시합니다. 판매 불가 아이템도 미리 넣어 두면 판매가 열릴 때 이미 시세가 있습니다.")]
    private ItemDefinition[] items = new ItemDefinition[0];

    [Header("Random walk (임시 테스트 값)")]
    [SerializeField, Min(0.01f), Tooltip("새 게임 / 구버전 Save 시작 배율.")]
    private float baseMultiplier = 1f;
    [SerializeField, Range(0f, 1f), Tooltip("하루 변화량 최대치. 매일 -값 ~ +값 사이에서 균등 랜덤.")]
    private float maxDailyChange = 0.15f;
    [SerializeField, Min(0.01f)] private float minMultiplier = 0.7f;
    [SerializeField, Min(0.01f)] private float maxMultiplier = 1.4f;
    [SerializeField, Min(1), Tooltip("아이템별 최근 기록 개수 (오늘 포함).")]
    private int historyLength = 7;

    [Header("References (비워 두면 같은 Scene에서 찾습니다)")]
    [SerializeField, Tooltip("실제 수면(GameDay.DayChanged)에서만 시세가 갱신됩니다.")]
    private GameDay gameDay;

    [Header("Runtime (Play 중 확인용)")]
    [SerializeField] private List<Entry> entries = new List<Entry>();

    private readonly Dictionary<ItemDefinition, Entry> lookup = new Dictionary<ItemDefinition, Entry>();
    private GameDay observedDay;

    // Raised after a daily step, a DEV refresh, a reset (new game / older save) and a save restore.
    public event Action MarketChanged;

    // ---- Registry: TradeService (static) and UIs reach the village market without references ----

    private static readonly List<MarketPriceSystem> live = new List<MarketPriceSystem>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => live.Clear();

    public static MarketPriceSystem Active
    {
        get
        {
            Scene activeScene = SceneManager.GetActiveScene();
            MarketPriceSystem fallback = null;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                MarketPriceSystem market = live[i];
                if (market == null) { live.RemoveAt(i); continue; }
                if (market.gameObject.scene == activeScene) return market;
                if (fallback == null) fallback = market;
            }
            return fallback;
        }
    }

    // Today's sell price with the base price as fallback when no market exists (scene opened alone).
    public static int CurrentSellPriceOf(ItemDefinition item)
    {
        if (item == null) return 0;
        MarketPriceSystem market = Active;
        return market != null ? market.GetCurrentPrice(item) : item.BaseSellPrice;
    }

    // ---- Lifetime ----

    private void Awake()
    {
        if (!live.Contains(this)) live.Add(this);
        if (gameDay == null) gameDay = FindInScene<GameDay>();
        ResetAll(raise: false);
        // Lifetime subscription (not OnEnable): only GameDay.Sleep raises DayChanged.
        observedDay = gameDay;
        if (observedDay != null) observedDay.DayChanged += OnDayChanged;
    }

    private void OnDestroy()
    {
        if (observedDay != null) observedDay.DayChanged -= OnDayChanged;
        observedDay = null;
        live.Remove(this);
    }

    private void OnValidate()
    {
        if (maxMultiplier < minMultiplier) maxMultiplier = minMultiplier;
        baseMultiplier = Mathf.Max(0.01f, baseMultiplier);
    }

    private T FindInScene<T>() where T : Component
    {
        Scene scene = gameObject.scene;
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T found = root.GetComponentInChildren<T>(true);
            if (found != null) return found;
        }
        return null;
    }

    private void OnDayChanged(int previousDay, int newDay)
    {
        AdvanceDay();
        Debug.Log($"{newDay}일차 시세 갱신\n{Describe()}", this);
    }

    // ---- Daily random walk ----

    // One market day for every item, independently. Called by DayChanged and the DEV refresh only.
    public void AdvanceDay()
    {
        foreach (Entry entry in entries)
        {
            float next = NextMultiplier(entry);
            entry.previousMultiplier = entry.currentMultiplier;
            entry.currentMultiplier = next;
            Push(entry.history, next);
        }
        RaiseChanged();
    }

    // Bounded random walk from yesterday. Future demand / festival / oversupply / regional / quest modifiers
    // can bias this step per item without changing the stored state or any caller.
    private float NextMultiplier(Entry entry)
    {
        float change = UnityEngine.Random.Range(-maxDailyChange, maxDailyChange);
        return Quantize(Mathf.Clamp(entry.currentMultiplier + change, Min, Max));
    }

    private float Min => Mathf.Min(minMultiplier, maxMultiplier);
    private float Max => Mathf.Max(minMultiplier, maxMultiplier);
    private float StartMultiplier => Quantize(Mathf.Clamp(baseMultiplier, Min, Max));
    private int HistoryLength => Mathf.Max(1, historyLength);
    // Two decimals keep saves and logs readable (1.12, not 1.1234567).
    private static float Quantize(float value) => Mathf.Round(value * 100f) / 100f;

    private void Push(List<float> history, float value)
    {
        history.Add(value);
        while (history.Count > HistoryLength) history.RemoveAt(0);
    }

    // New game and older saves: every item starts at the base multiplier with a one-day history.
    private void ResetAll(bool raise)
    {
        entries.Clear();
        lookup.Clear();
        if (items != null)
            foreach (ItemDefinition item in items)
                if (item != null && item.BaseSellPrice > 0 && !lookup.ContainsKey(item)) AddEntry(item);
        if (raise) RaiseChanged();
    }

    private Entry AddEntry(ItemDefinition item)
    {
        var entry = new Entry { item = item, currentMultiplier = StartMultiplier, previousMultiplier = StartMultiplier };
        entry.history.Add(StartMultiplier);
        entries.Add(entry);
        lookup[item] = entry;
        return entry;
    }

    private Entry Get(ItemDefinition item) => item != null && lookup.TryGetValue(item, out Entry entry) ? entry : null;

    // ---- Prices (an item outside the market keeps its base price) ----

    public bool HasMarket(ItemDefinition item) => Get(item) != null;
    public int GetBasePrice(ItemDefinition item) => item != null ? item.BaseSellPrice : 0;
    public float GetCurrentMultiplier(ItemDefinition item) => Get(item)?.currentMultiplier ?? 1f;
    public float GetPreviousMultiplier(ItemDefinition item) => Get(item)?.previousMultiplier ?? 1f;
    public int GetCurrentPrice(ItemDefinition item) => PriceAt(item, GetCurrentMultiplier(item));
    public int GetPreviousPrice(ItemDefinition item) => PriceAt(item, GetPreviousMultiplier(item));

    // The single rounding rule: Mathf.RoundToInt (x.5 rounds to even), never below 1 G for an item that has a base price.
    public static int PriceAt(ItemDefinition item, float multiplier)
    {
        int basePrice = item != null ? item.BaseSellPrice : 0;
        if (basePrice <= 0) return 0;
        return Mathf.Max(1, Mathf.RoundToInt(basePrice * multiplier));
    }

    // Percentages compare the rounded Gold prices the player sees: 10 → 13 G is +30%.
    public float GetBaseChangePercent(ItemDefinition item) => Percent(GetCurrentPrice(item), GetBasePrice(item));
    public int GetDailyDifference(ItemDefinition item) => GetCurrentPrice(item) - GetPreviousPrice(item);
    public float GetDailyPercentDifference(ItemDefinition item) => Percent(GetCurrentPrice(item), GetPreviousPrice(item));

    private static float Percent(int now, int reference) => reference > 0 ? (now - reference) * 100f / reference : 0f;

    // Recent prices, oldest first, today last (at most History Length values).
    public int[] GetPriceHistory(ItemDefinition item)
    {
        Entry entry = Get(item);
        if (entry == null) return new[] { GetCurrentPrice(item) };
        var prices = new int[entry.history.Count];
        for (int i = 0; i < prices.Length; i++) prices[i] = PriceAt(item, entry.history[i]);
        return prices;
    }

    public float[] GetMultiplierHistory(ItemDefinition item) => Get(item)?.history.ToArray() ?? new[] { 1f };

    // What selling `amount` today would pay (owned quantity × today's price). No purchase cost is tracked.
    public long GetStackValue(ItemDefinition item, int amount) =>
        item != null && amount > 0 ? (long)GetCurrentPrice(item) * amount : 0;

    // ---- Save / load (ids come from GameSaveSystem's item bindings) ----

    public GameSaveData.MarketItemState[] CaptureState(Func<ItemDefinition, string> idOf)
    {
        var states = new List<GameSaveData.MarketItemState>();
        foreach (Entry entry in entries)
        {
            string id = idOf(entry.item);
            if (string.IsNullOrWhiteSpace(id)) continue;
            states.Add(new GameSaveData.MarketItemState
            {
                id = id,
                currentMultiplier = entry.currentMultiplier,
                previousMultiplier = entry.previousMultiplier,
                history = entry.history.ToArray()
            });
        }
        return states.ToArray();
    }

    // Copies the saved market exactly; never rolls new prices. Null/empty (new game or older saves) and items
    // missing from the save start at the base multiplier. Unknown ids are ignored. Values outside the current
    // Inspector range are clamped (the range may have been tuned since the save).
    public void RestoreState(GameSaveData.MarketItemState[] saved, Func<string, ItemDefinition> itemOf)
    {
        ResetAll(raise: false);
        if (saved != null)
            foreach (GameSaveData.MarketItemState state in saved)
            {
                Entry entry = state != null ? Get(itemOf(state.id)) : null;
                if (entry == null) continue;
                entry.currentMultiplier = Restored(state.currentMultiplier);
                entry.previousMultiplier = Restored(state.previousMultiplier);
                entry.history.Clear();
                if (state.history != null)
                    foreach (float value in state.history) Push(entry.history, Restored(value));
                if (entry.history.Count == 0 || !Mathf.Approximately(entry.history[entry.history.Count - 1], entry.currentMultiplier))
                    Push(entry.history, entry.currentMultiplier);
            }
        RaiseChanged();
    }

    private float Restored(float value) => Quantize(Mathf.Clamp(value, Min, Max));

    private void RaiseChanged()
    {
        try { MarketChanged?.Invoke(); }
        catch (Exception exception) { Debug.LogException(exception, this); }
    }

    // ---- DEV / Inspector tests (component ⋮ menu during Play; the trading post DEV button calls the same) ----

    [ContextMenu("테스트: 시세 하루 갱신 (날짜 변화 없음)")]
    public void DebugAdvance()
    {
        if (!Application.isPlaying) { Debug.LogWarning("시세 테스트는 Play 중에만 동작합니다.", this); return; }
        AdvanceDay();
        Debug.Log($"테스트: 시세 강제 갱신\n{Describe()}", this);
    }

    [ContextMenu("테스트: 오늘 시세 출력")]
    private void DebugLog() => Debug.Log($"오늘 시세\n{Describe()}", this);

    [ContextMenu("테스트: 시세 기준 배율로 초기화")]
    private void DebugReset()
    {
        if (!Application.isPlaying) { Debug.LogWarning("시세 테스트는 Play 중에만 동작합니다.", this); return; }
        ResetAll(raise: true);
        Debug.Log($"테스트: 시세 초기화\n{Describe()}", this);
    }

    public string Describe()
    {
        var text = new StringBuilder();
        foreach (Entry entry in entries)
        {
            ItemDefinition item = entry.item;
            int diff = GetDailyDifference(item);
            text.AppendLine($"{item.DisplayName}: 기준 {GetBasePrice(item)}G → 오늘 {GetCurrentPrice(item)}G " +
                $"(×{entry.currentMultiplier:0.00}, 기준 대비 {GetBaseChangePercent(item):+0;-0;0}%, " +
                $"전날 {GetPreviousPrice(item)}G {(diff > 0 ? "상승" : diff < 0 ? "하락" : "변동 없음")} {diff:+0;-0;0}G) " +
                $"기록 [{string.Join(", ", GetPriceHistory(item))}]");
        }
        return text.Length > 0 ? text.ToString() : "(시세 아이템 없음)";
    }
}
