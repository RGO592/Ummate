using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Saved as an int nowhere; order may change freely. One entry per action that costs energy.
public enum EnergyAction
{
    PrepareField, PlantCrop, WaterCrop, HarvestCrop,
    PlantFlower, WaterFlower, HarvestFlower,
    PlantSapling, ChopHit,
    PlaceFacility, MoveFacility,
    PlaceHex, MoveHex
}

// Player energy (stamina). Lives on the village player next to PlayerWallet / PlayerInventory. The village scene
// stays loaded (only suspended) during regional travel, so the value survives scene round trips without
// DontDestroyOnLoad. Region scenes (SpiritGridScene) have no PlayerEnergy and use PlayerEnergy.Active.
// Rule for every action: check CanSpend before anything changes, Spend only after the action really succeeded.
// Values marked "임시" are prototype test values, not final balance.
[DisallowMultipleComponent]
public sealed class PlayerEnergy : MonoBehaviour
{
    [Serializable]
    public struct ActionCosts
    {
        [Header("농사 (밭)")]
        [Min(0)] public int prepareField;
        [Min(0)] public int plantCrop;
        [Min(0)] public int waterCrop;
        [Min(0)] public int harvestCrop;
        [Header("꽃 (화단)")]
        [Min(0)] public int plantFlower;
        [Min(0)] public int waterFlower;
        [Min(0)] public int harvestFlower;
        [Header("나무")]
        [Min(0)] public int plantSapling;
        [Min(0), Tooltip("벌목 1타당 소비량")] public int chopHit;
        [Header("마을 건설")]
        [Min(0)] public int placeFacility;
        [Min(0)] public int moveFacility;
        [Header("SpiritGrid 환경 Hex")]
        [Min(0)] public int placeHex;
        [Min(0)] public int moveHex;

        public static ActionCosts PrototypeDefaults => new ActionCosts
        {
            prepareField = 5, plantCrop = 2, waterCrop = 3, harvestCrop = 2,
            plantFlower = 2, waterFlower = 3, harvestFlower = 2,
            plantSapling = 3, chopHit = 4,
            placeFacility = 2, moveFacility = 0,
            placeHex = 2, moveHex = 0
        };
    }

    public const string NotEnoughMessage = "기운이 부족해.";

    [SerializeField, Min(1), Tooltip("최대 기운 (임시 테스트 값). 새 게임·수면 후 이 값으로 시작합니다.")]
    private int maxEnergy = 100;
    [SerializeField, Min(0), Tooltip("실행 중 현재 기운 (Inspector 확인용). 시작 시 최대치로 채워지고, 세이브를 불러오면 저장된 값으로 덮어씁니다.")]
    private int currentEnergy = 100;
    [SerializeField, Range(0.1f, 1f), Tooltip("기운 0일 때 걷기/달리기 속도 배율 (임시 테스트 값). 0.65 = 기본 속도의 65%.")]
    private float exhaustedSpeedMultiplier = 0.65f;
    [SerializeField, Tooltip("행동별 기운 소비량 (임시 테스트 값). 0 = 소비 없음.")]
    private ActionCosts costs = ActionCosts.PrototypeDefaults;

    [Header("References (비워 두면 같은 Scene에서 찾습니다)")]
    [SerializeField, Tooltip("실제 수면(GameDay.DayChanged)에서만 기운을 전부 회복합니다.")]
    private GameDay gameDay;
    [SerializeField, Tooltip("마을 DEV 토글. DEV ON이면 기운 소비를 무시합니다 (SpiritGrid DEV 무료배치도 동일).")]
    private GuideTooltip developerSource;

    private bool initialized;
    private GameDay observedDay;

    public event Action Changed;

    public int MaxEnergy => Mathf.Max(1, maxEnergy);
    public int CurrentEnergy { get { Initialize(); return currentEnergy; } }
    public bool IsFull => CurrentEnergy >= MaxEnergy;
    public bool IsExhausted => CurrentEnergy <= 0;
    public float SpeedMultiplier => IsExhausted ? Mathf.Clamp(exhaustedSpeedMultiplier, 0.1f, 1f) : 1f;
    // DEV ON (village DEV button or SpiritGrid DEV free placement): actions cost nothing. DEV OFF: normal costs.
    public bool IgnoresCost => EcoDevMode.FreePlacement || (developerSource != null && developerSource.DeveloperMode);

    // ---- Registry: the village player's energy is reachable from region scenes ----

    private static readonly List<PlayerEnergy> live = new List<PlayerEnergy>();
    // Raised when a PlayerEnergy wakes up or goes away, so views created before it can bind.
    public static event Action Registered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        live.Clear();
        Registered = null;
    }

    // Prefers the active scene's player; otherwise the suspended village player (SpiritGridScene visit).
    public static PlayerEnergy Active
    {
        get
        {
            Scene activeScene = SceneManager.GetActiveScene();
            PlayerEnergy fallback = null;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                PlayerEnergy energy = live[i];
                if (energy == null) { live.RemoveAt(i); continue; }
                if (energy.gameObject.scene == activeScene) return energy;
                if (fallback == null) fallback = energy;
            }
            return fallback;
        }
    }

    // The interactor's own energy, else the active one. Null when no village player exists (region scene opened alone).
    public static PlayerEnergy For(GameObject interactor) =>
        interactor != null && interactor.TryGetComponent(out PlayerEnergy own) ? own : Active;

    // Gameplay gate: true when the action may run. A missing PlayerEnergy never blocks anything.
    public static bool CheckAction(PlayerEnergy energy, EnergyAction action, bool notify = true)
    {
        if (energy == null || energy.CanSpend(energy.GetCost(action))) return true;
        if (notify)
        {
            AudioManager.Play(GameSfx.ActionFail);
            PlayerFeedback.Notify(NotEnoughMessage);
        }
        return false;
    }

    // Call only after the action committed (and before its quest event).
    public static void SpendAction(PlayerEnergy energy, EnergyAction action)
    {
        if (energy != null) energy.TrySpend(energy.GetCost(action));
    }

    // ---- Lifetime ----

    private void Awake()
    {
        Initialize();
        if (!live.Contains(this)) live.Add(this);
        ResolveReferences();
        BindDay();
        RaiseRegistered();
    }

    private void OnDestroy()
    {
        UnbindDay();
        live.Remove(this);
        RaiseRegistered();
    }

    private void OnValidate()
    {
        maxEnergy = Mathf.Max(1, maxEnergy);
        currentEnergy = Mathf.Clamp(currentEnergy, 0, maxEnergy);
    }

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;
        currentEnergy = MaxEnergy;
    }

    private void ResolveReferences()
    {
        if (gameDay != null && developerSource != null) return;
        Scene scene = gameObject.scene;
        if (!scene.IsValid() || !scene.isLoaded) return;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (gameDay == null) gameDay = root.GetComponentInChildren<GameDay>(true);
            if (developerSource == null) developerSource = root.GetComponentInChildren<GuideTooltip>(true);
        }
    }

    // Subscribed for the whole lifetime (not OnEnable): only GameDay.Sleep raises DayChanged.
    // RestoreDay (F8/F9 load) raises StateRestored instead and scene travel raises neither, so only a real sleep refills.
    private void BindDay()
    {
        UnbindDay();
        observedDay = gameDay;
        if (observedDay != null) observedDay.DayChanged += OnDayChanged;
    }

    private void UnbindDay()
    {
        if (observedDay != null) observedDay.DayChanged -= OnDayChanged;
        observedDay = null;
    }

    private void OnDayChanged(int previousDay, int newDay)
    {
        RestoreFull();
        Debug.Log($"{newDay}일차 아침: 기운 전부 회복 ({CurrentEnergy}/{MaxEnergy})", this);
    }

    // ---- Energy API ----

    public int GetCost(EnergyAction action)
    {
        int cost = action switch
        {
            EnergyAction.PrepareField => costs.prepareField,
            EnergyAction.PlantCrop => costs.plantCrop,
            EnergyAction.WaterCrop => costs.waterCrop,
            EnergyAction.HarvestCrop => costs.harvestCrop,
            EnergyAction.PlantFlower => costs.plantFlower,
            EnergyAction.WaterFlower => costs.waterFlower,
            EnergyAction.HarvestFlower => costs.harvestFlower,
            EnergyAction.PlantSapling => costs.plantSapling,
            EnergyAction.ChopHit => costs.chopHit,
            EnergyAction.PlaceFacility => costs.placeFacility,
            EnergyAction.MoveFacility => costs.moveFacility,
            EnergyAction.PlaceHex => costs.placeHex,
            EnergyAction.MoveHex => costs.moveHex,
            _ => 0
        };
        return Mathf.Max(0, cost);
    }

    public bool CanSpend(int cost) => cost <= 0 || IgnoresCost || CurrentEnergy >= cost;

    // Atomic: not enough energy leaves the value unchanged. DEV ON succeeds without spending.
    public bool TrySpend(int cost)
    {
        if (cost <= 0 || IgnoresCost) return true;
        if (CurrentEnergy < cost) return false;
        currentEnergy -= cost;
        RaiseChanged();
        return true;
    }

    // Never above MaxEnergy. Returns the amount actually restored.
    public int Restore(int amount)
    {
        if (amount <= 0) return 0;
        int before = CurrentEnergy;
        currentEnergy = (int)Math.Min(MaxEnergy, (long)before + amount);
        int restored = currentEnergy - before;
        if (restored > 0) RaiseChanged();
        return restored;
    }

    public void RestoreFull() => Restore(MaxEnergy);

    // Save/load only (not a recovery). A negative value means "not in the save" (older saves): full energy.
    public void RestoreSavedState(int savedEnergy)
    {
        initialized = true;
        currentEnergy = savedEnergy < 0 ? MaxEnergy : Mathf.Clamp(savedEnergy, 0, MaxEnergy);
        RaiseChanged();
    }

    // Test only (Play mode). Also bound to F6 / F7 by EnergyHUD in the Editor and development builds.
    [ContextMenu("테스트: 기운 0으로 (Play 중)")]
    private void DebugSetEmpty() => DebugSetEnergy(0);

    [ContextMenu("테스트: 기운 가득 (Play 중)")]
    private void DebugSetFull() => DebugSetEnergy(MaxEnergy);

    public void DebugSetEnergy(int value)
    {
        // Outside Play mode the value would be replaced by MaxEnergy when Play starts (Initialize).
        if (!Application.isPlaying)
        {
            Debug.LogWarning("기운 테스트 값은 Play 중에만 바꿀 수 있습니다. (시작하면 최대치로 채워집니다)", this);
            return;
        }
        Initialize();
        currentEnergy = Mathf.Clamp(value, 0, MaxEnergy);
        Debug.Log($"테스트: 기운 {currentEnergy}/{MaxEnergy}", this);
        RaiseChanged();
    }

    // Listener errors (HUD) must never break the gameplay action that changed the energy.
    private void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch (Exception exception) { Debug.LogException(exception, this); }
    }

    private static void RaiseRegistered()
    {
        try { Registered?.Invoke(); }
        catch (Exception exception) { Debug.LogException(exception); }
    }
}
