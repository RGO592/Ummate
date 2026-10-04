using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class GameSaveSystem : MonoBehaviour
{
    [Serializable] public sealed class ItemBinding { public string id; public ItemDefinition item; }
    [Serializable] public sealed class PlotBinding { public string id; public FarmPlot plot; }
    [Serializable] public sealed class ChickenBinding { public string id; public ChickenFeeding chicken; }
    [Serializable] public sealed class CowBinding { public string id; public CowFeeding cow; }
    [SerializeField] private GameDay gameDay;
    [SerializeField] private SleepTransition sleepTransition;
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerQuickbar quickbar;
    [SerializeField] private MissionProgress mission;
    [SerializeField] private ItemBinding[] items;
    [SerializeField] private PlotBinding[] plots;
    [SerializeField] private ChickenBinding[] chickens;
    [SerializeField] private CowBinding[] cows;
    private bool busy;
    private bool unreadableSave;
    private bool sessionStarted;
    // Stable IDs already used by the scene's item catalog and JSON saves.
    private const string WheatSeedsId = "3208e0eca67ee604c9647d55200a0494";
    private const string WateringCanId = "d48ee8a945ecf1740a8a626dd4419e73";
    public static string DefaultSavePath => Path.Combine(Application.persistentDataPath, "savegame.json");
    public static bool HasSaveFile => File.Exists(DefaultSavePath);
    public string SavePath => DefaultSavePath;

    private void OnEnable()
    {
        if (sleepTransition != null) sleepTransition.SleepCompleted += AutoSave;
    }
    private void OnDisable()
    {
        if (sleepTransition != null) sleepTransition.SleepCompleted -= AutoSave;
    }
    private void Start()
    {
        Debug.Log($"저장 파일: {SavePath}\nF8: 새 게임 (기존 세이브 초기화) / F9: 이어하기\n시작 방식을 선택한 뒤 취침하면 자동 저장됩니다.", this);
    }
    private void Update()
    {
        if (!Application.isFocused || Keyboard.current == null) return;
        if (Keyboard.current.f8Key.wasPressedThisFrame) NewGame();
        else if (Keyboard.current.f9Key.wasPressedThisFrame) ContinueGame();
    }
    private void AutoSave() => TrySave();

    public bool NewGame()
    {
        if (busy || sleepTransition == null || sleepTransition.IsSleeping)
        {
            Debug.Log("저장 처리 또는 취침 전환 중에는 새 게임을 시작할 수 없습니다.", this);
            return false;
        }
        busy = true;
        try
        {
            ValidateBindings();
            Require(items.Any(b => b.id == WheatSeedsId) && items.Any(b => b.id == WateringCanId), "새 게임 시작 아이템 연결이 누락되었습니다.");
            var data = new GameSaveData
            {
                version = 1, day = 1, selectedSlot = 0,
                inventory = items.Select(b => new GameSaveData.ItemState
                {
                    id = b.id, quantity = b.id == WheatSeedsId ? 3 : b.id == WateringCanId ? 1 : 0
                }).ToArray(),
                mission = new GameSaveData.MissionState { stage = 1 },
                plots = plots.Select(b => new GameSaveData.PlotState { id = b.id }).ToArray(),
                chickens = chickens.Select(b => new GameSaveData.ChickenState { id = b.id }).ToArray(),
                cows = cows.Select(b => new GameSaveData.CowState { id = b.id }).ToArray()
            };
            Validate(data);
            // Commit the reset first. A write failure leaves both the old save and live state intact.
            WriteSave(data);
            ApplyState(data);
            unreadableSave = false;
            sessionStarted = true;
            Debug.Log($"새 게임 시작: 기존 세이브를 초기화하고 1일차 새 게임 데이터로 저장했습니다. 미션·인벤토리·밭·동물 상태가 초기화되었습니다.\n{SavePath}", this);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"새 게임 시작 실패: {ex.Message}", this);
            return false;
        }
        finally { busy = false; }
    }

    public bool ContinueGame() => TryLoad();

    public bool TrySave()
    {
        if (busy || sleepTransition == null || sleepTransition.IsSleeping) return false;
        if (!sessionStarted)
        {
            Debug.Log("자동 저장 대기: 먼저 F8 새 게임 또는 F9 이어하기를 선택해 주세요. 기존 세이브는 유지됩니다.", this);
            return false;
        }
        busy = true;
        try
        {
            // Preserve an incompatible/damaged existing file instead of overwriting it with defaults.
            if (unreadableSave && File.Exists(SavePath))
                throw new InvalidDataException("불러오기에 실패한 기존 파일을 보호하고 있습니다. 파일을 확인해 주세요.");
            GameSaveData data = Capture();
            Validate(data);
            WriteSave(data);
            unreadableSave = false;
            Debug.Log($"자동 저장 완료: {data.day}일차 아침 · {SavePath}", this);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"저장 실패: {ex.Message}", this);
            return false;
        }
        finally { busy = false; }
    }

    public bool TryLoad()
    {
        if (busy || sleepTransition == null || sleepTransition.IsSleeping)
        {
            Debug.Log("취침 전환 중에는 불러올 수 없습니다.", this);
            return false;
        }
        if (!File.Exists(SavePath))
        {
            Debug.Log("이어하기 불가: 저장된 게임이 없습니다. F8로 새 게임을 시작해 주세요.", this);
            return false;
        }
        busy = true;
        try
        {
            GameSaveData data = JsonUtility.FromJson<GameSaveData>(File.ReadAllText(SavePath));
            // Validate the entire file and every reference before changing any gameplay state.
            Validate(data);
            ApplyState(data);
            unreadableSave = false;
            sessionStarted = true;
            Debug.Log($"저장된 게임 불러오기 완료: {data.day}일차. 날짜 진행·성장·생산·보상은 다시 실행하지 않았습니다.", this);
            return true;
        }
        catch (Exception ex)
        {
            unreadableSave = true;
            Debug.LogWarning($"불러오기 실패: {ex.Message}", this);
            return false;
        }
        finally { busy = false; }
    }

    private void WriteSave(GameSaveData data)
    {
        Directory.CreateDirectory(Application.persistentDataPath);
        string temporary = SavePath + ".tmp";
        File.WriteAllText(temporary, JsonUtility.ToJson(data, true), new System.Text.UTF8Encoding(false));
        // Same-directory replace keeps the previous save intact if writing is interrupted.
        if (File.Exists(SavePath)) File.Replace(temporary, SavePath, null);
        else File.Move(temporary, SavePath);
    }

    private void ApplyState(GameSaveData data)
    {
        var restoredInventory = data.inventory.ToDictionary(s => items.Single(b => b.id == s.id).item, s => s.quantity);
        gameDay.RestoreDay(data.day);
        mission.RestoreState(data.mission);
        foreach (var saved in data.plots) plots.Single(b => b.id == saved.id).plot.RestoreState(saved);
        foreach (var saved in data.chickens) chickens.Single(b => b.id == saved.id).chicken.RestoreState(saved);
        foreach (var saved in data.cows) cows.Single(b => b.id == saved.id).cow.RestoreState(saved);
        inventory.RestoreQuantities(restoredInventory);
        quickbar.SelectSlot(data.selectedSlot);
    }

    private GameSaveData Capture()
    {
        ValidateBindings();
        var quantities = inventory.CaptureQuantities();
        Require(quantities.Keys.All(item => items.Any(b => b.item == item)), "저장 목록에 등록되지 않은 아이템이 있습니다.");
        return new GameSaveData
        {
            version = 1, day = gameDay.CurrentDay, selectedSlot = quickbar.SelectedSlotIndex,
            inventory = items.Select(b => new GameSaveData.ItemState { id = b.id, quantity = inventory.GetQuantity(b.item) }).ToArray(),
            mission = mission.CaptureState(),
            plots = plots.Select(b => b.plot.CaptureState(b.id)).ToArray(),
            chickens = chickens.Select(b => b.chicken.CaptureState(b.id)).ToArray(),
            cows = cows.Select(b => b.cow.CaptureState(b.id)).ToArray()
        };
    }

    private void ValidateBindings()
    {
        Require(gameDay != null && sleepTransition != null && inventory != null && quickbar != null &&
            mission != null && mission.SaveReferencesValid, "저장 시스템 기본 연결이 누락되었습니다.");
        Require(items != null && items.Length > 0 && items.All(b => b != null && b.item != null), "아이템 연결 누락");
        Require(plots != null && plots.Length > 0 && plots.All(b => b != null && b.plot != null && b.plot.SaveReferencesValid), "밭 연결 누락");
        Require(chickens != null && chickens.All(b => b != null && b.chicken != null && b.chicken.SaveReferencesValid), "닭 연결 누락");
        Require(cows != null && cows.All(b => b != null && b.cow != null && b.cow.SaveReferencesValid), "소 연결 누락");
        CheckIds(items.Select(b => b.id)); CheckIds(plots.Select(b => b.id));
        CheckIds(chickens.Select(b => b.id)); CheckIds(cows.Select(b => b.id));
        Require(items.Select(b => b.item).Distinct().Count() == items.Length &&
            plots.Select(b => b.plot).Distinct().Count() == plots.Length &&
            chickens.Select(b => b.chicken).Distinct().Count() == chickens.Length &&
            cows.Select(b => b.cow).Distinct().Count() == cows.Length, "중복 연결이 있습니다.");
    }

    private void Validate(GameSaveData data)
    {
        ValidateBindings();
        Require(data != null && data.version == 1 && data.day >= 1, "지원하지 않거나 손상된 저장 파일입니다.");
        Require(data.selectedSlot >= 0 && data.selectedSlot < PlayerQuickbar.SlotCount, "퀵슬롯 값 오류");
        Require(data.inventory != null && data.inventory.All(s => s != null && s.quantity >= 0), "인벤토리 값 오류");
        Require(data.plots != null && data.plots.All(s => s != null), "밭 데이터 누락");
        Require(data.chickens != null && data.chickens.All(s => s != null), "닭 데이터 누락");
        Require(data.cows != null && data.cows.All(s => s != null), "소 데이터 누락");
        MatchIds(data.inventory.Select(s => s.id), items.Select(b => b.id));
        MatchIds(data.plots.Select(s => s.id), plots.Select(b => b.id));
        MatchIds(data.chickens.Select(s => s.id), chickens.Select(b => b.id));
        MatchIds(data.cows.Select(s => s.id), cows.Select(b => b.id));
        var m = data.mission;
        Require(m != null, "미션 데이터 누락");
        Require(m.wheatHarvested >= 0 && m.wheatHarvested <= 9 && m.eggsCollected >= 0 && m.eggsCollected <= 4 &&
            m.cornHarvested >= 0 && m.cornHarvested <= 9 && m.milkCollected >= 0 && m.milkCollected <= 4, "미션 수량 오류");
        Require((!m.m02CompletedAndRewarded || m.m01CompletedAndRewarded) &&
            (!m.m03CompletedAndRewarded || m.m02CompletedAndRewarded) && (!m.prologueCompleted || m.m03CompletedAndRewarded), "미션 완료 순서 오류");
        Require(m.stage == (m.prologueCompleted ? 5 : m.m03CompletedAndRewarded ? 4 : m.m02CompletedAndRewarded ? 3 : m.m01CompletedAndRewarded ? 2 : 1), "미션 단계 오류");
        Require(m.chickenUnlocked == m.m01CompletedAndRewarded && m.cowUnlocked == m.m03CompletedAndRewarded, "해금 상태 오류");
        foreach (var p in data.plots)
        {
            Require(Enum.IsDefined(typeof(FarmPlot.CropKind), p.crop) && Enum.IsDefined(typeof(FarmPlot.CropState), p.state), "작물 종류/상태 오류");
            if (p.state == FarmPlot.CropState.Empty)
                Require(p.crop == FarmPlot.CropKind.None && p.growthDays == 0 && !p.wateredToday, "빈 밭 데이터 오류");
            else
                Require(p.crop != FarmPlot.CropKind.None && p.growthDays == (p.state == FarmPlot.CropState.ReadyToHarvest ? 1 : 0) &&
                    (p.state != FarmPlot.CropState.ReadyToHarvest || !p.wateredToday), "성장 상태 오류");
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void CheckIds(IEnumerable<string> ids)
    {
        var values = ids.ToArray();
        Require(values.All(id => !string.IsNullOrWhiteSpace(id)) && values.Distinct().Count() == values.Length, "저장 ID 누락/중복");
    }
    private static void MatchIds(IEnumerable<string> saved, IEnumerable<string> configured)
    {
        CheckIds(saved);
        Require(new HashSet<string>(saved).SetEquals(configured), "저장 파일과 현재 Scene의 ID 목록이 다릅니다.");
    }
}
