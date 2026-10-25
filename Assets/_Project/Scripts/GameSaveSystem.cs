using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class GameSaveSystem : MonoBehaviour
{
    [Serializable] public sealed class ItemBinding
    {
        public string id;
        public ItemDefinition item;
        [Tooltip("Allow this newly introduced item to be absent from older saves (quantity 0). Existing items stay required.")]
        public bool optionalInOlderSaves;
    }
    [Serializable] public sealed class PlotBinding { public string id; public FarmPlot plot; }
    [Serializable] public sealed class ChickenBinding { public string id; public ChickenFeeding chicken; }
    [Serializable] public sealed class CowBinding { public string id; public CowFeeding cow; }
    [Serializable] public sealed class FlowerBedBinding { public string id; public FlowerBed bed; }
    [SerializeField] private GameDay gameDay;
    [SerializeField] private SleepTransition sleepTransition;
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerWallet wallet;
    [SerializeField] private PlayerQuickbar quickbar;
    [SerializeField] private MissionProgress mission;
    [SerializeField] private ItemBinding[] items;
    [SerializeField] private PlotBinding[] plots;
    [SerializeField] private ChickenBinding[] chickens;
    [SerializeField] private CowBinding[] cows;
    [SerializeField] private FlowerBedBinding[] flowerBeds;
    [SerializeField, Tooltip("Build Settings paths of the scenes a save may return the player to. Never the main menu.")]
    private string[] gameplayScenes = new string[0];
    private bool busy;
    private bool sessionStarted;
    // Per slot: a file that failed to load. Only the silent auto-save refuses to overwrite it.
    private readonly bool[] unreadableSlots = new bool[SlotCount];
    // Stable IDs already used by the scene's item catalog and JSON saves.
    private const string WheatSeedsId = "3208e0eca67ee604c9647d55200a0494";
    private const string WateringCanId = "d48ee8a945ecf1740a8a626dd4419e73";

    // Slot 0 is written only by the sleep auto-save (and New Game); slots 1–3 only by the player.
    // Every slot is its own file, so a failed or damaged slot never touches the others.
    public enum SlotKind { Auto, Manual1, Manual2, Manual3 }
    public const int SlotCount = 4;
    public const int AutoSlot = 0;
    private static readonly string[] SlotFileNames = { "autosave.json", "save_1.json", "save_2.json", "save_3.json" };
    // The single save file used before slots existed. Copied once, never deleted.
    private const string LegacyFileName = "savegame.json";
    private static bool legacyChecked;

    // Display-only: what a menu needs for one slot row. Never validated or applied.
    public readonly struct SlotSummary
    {
        public SlotSummary(int slot, bool exists, int day, DateTime savedAt, string scene)
        {
            Slot = slot; Exists = exists; Day = day; SavedAt = savedAt; Scene = scene ?? "";
        }
        public int Slot { get; }
        public SlotKind Kind => (SlotKind)Slot;
        // A file is present; it may still be unreadable (Day 0).
        public bool Exists { get; }
        public int Day { get; }
        // Real-world time: the file's last write time.
        public DateTime SavedAt { get; }
        // Saved scene path; empty for older saves (they continue in FarmScene).
        public string Scene { get; }
        public string LocationName => GetLocationName(Scene);
        public bool IsReadable => Day >= 1;
    }
    // Only the fields a summary needs; JsonUtility skips the rest of the file.
    [Serializable] private sealed class SlotHeader { public int version; public int day; public string scene; }

    // Player-facing region names by scene file name. A new region (desert, snowfield) adds one line here.
    private const string DefaultLocationScene = "FarmScene";
    private static readonly Dictionary<string, string> LocationNames = new Dictionary<string, string>
    {
        { "FarmScene", "숲 마을" },
        { "SpiritGridScene", "숲&강" },
    };

    // Older saves without a scene continue in FarmScene, so they show its name. Unknown scenes show their file name.
    public static string GetLocationName(string scenePath)
    {
        string key = string.IsNullOrEmpty(scenePath) ? DefaultLocationScene : Path.GetFileNameWithoutExtension(scenePath);
        return LocationNames.TryGetValue(key, out string name) ? name : key;
    }

    // Player-facing result of the last NewGame / save / load, shown by PauseMenu. No dev key names.
    public string LastMessage { get; private set; } = "";
    public int CurrentDay => gameDay != null ? gameDay.CurrentDay : 0;
    public bool IsSleeping => sleepTransition != null && sleepTransition.IsSleeping;

    public static bool IsValidSlot(int slot) => slot >= 0 && slot < SlotCount;
    public static string GetSlotName(int slot) => slot == AutoSlot ? "자동 저장" : $"수동 저장 {slot}";
    public static string GetSlotPath(int slot) => Path.Combine(Application.persistentDataPath, SlotFileNames[slot]);

    // True when the slot holds a readable save. The summary is filled either way (Exists for a damaged file).
    public static bool TryGetSlotSummary(int slot, out SlotSummary summary)
    {
        summary = new SlotSummary(slot, false, 0, default, null);
        if (!IsValidSlot(slot)) return false;
        MigrateLegacySave();
        try
        {
            string path = GetSlotPath(slot);
            if (!File.Exists(path)) return false;
            summary = new SlotSummary(slot, true, 0, default, null);
            if (!TryReadHeader(path, out SlotHeader header)) return false;
            summary = new SlotSummary(slot, true, header.day, File.GetLastWriteTime(path), header.scene);
            return true;
        }
        catch (Exception) { return false; }
    }

    public static bool HasAnySave => TryFindLatestSlot(out _);

    // Most recently written readable slot, auto and manual alike (main menu Continue).
    public static bool TryFindLatestSlot(out int slot)
    {
        int[] slots = ReadableSlotsByRecency();
        slot = slots.Length > 0 ? slots[0] : -1;
        return slots.Length > 0;
    }

    private static int[] ReadableSlotsByRecency()
    {
        var summaries = new List<SlotSummary>();
        for (int slot = 0; slot < SlotCount; slot++)
            if (TryGetSlotSummary(slot, out SlotSummary summary)) summaries.Add(summary);
        return summaries.OrderByDescending(s => s.SavedAt).Select(s => s.Slot).ToArray();
    }

    private static bool TryReadHeader(string path, out SlotHeader header)
    {
        header = JsonUtility.FromJson<SlotHeader>(File.ReadAllText(path));
        return header != null && header.version == 1 && header.day >= 1;
    }

    // One-time move to slots: only when savegame.json exists and no slot file does yet. The old file was
    // written by New Game and the sleep auto-save, so it becomes the auto slot. It is copied, never deleted,
    // and an unreadable file is left alone (reported once).
    private static void MigrateLegacySave()
    {
        if (legacyChecked) return;
        legacyChecked = true;
        string legacy = Path.Combine(Application.persistentDataPath, LegacyFileName);
        try
        {
            if (!File.Exists(legacy) || Enumerable.Range(0, SlotCount).Any(slot => File.Exists(GetSlotPath(slot)))) return;
            if (!TryReadHeader(legacy, out _))
            {
                Debug.LogWarning($"기존 저장 파일을 읽을 수 없어 슬롯으로 옮기지 않았습니다. 원본은 그대로 둡니다.\n{legacy}");
                return;
            }
            string target = GetSlotPath(AutoSlot), temporary = target + ".tmp";
            // A copy keeps the original write time, so the slot shows when the game was really saved.
            File.Copy(legacy, temporary, true);
            File.Move(temporary, target);
            Debug.Log($"기존 저장 파일을 자동 저장 칸으로 옮겼습니다. 원본은 그대로 둡니다.\n{legacy} → {target}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"기존 저장 파일을 슬롯으로 옮기지 못했습니다. 원본은 그대로 둡니다: {ex.Message}\n{legacy}");
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => legacyChecked = false;

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
        Debug.Log($"저장 폴더: {Application.persistentDataPath}\nF8: 새 게임 (자동 저장 칸 초기화) / F9: 이어하기 (가장 최근 저장)\n시작 방식을 선택한 뒤 취침하면 자동 저장 칸에 저장됩니다.", this);
    }
    private void Update()
    {
        if (!Application.isFocused || Keyboard.current == null) return;
        if (Keyboard.current.f8Key.wasPressedThisFrame) NewGame();
        else if (Keyboard.current.f9Key.wasPressedThisFrame) ContinueGame();
    }
    private void AutoSave() => TryAutoSave();

    private void Report(string message, bool warning = false)
    {
        LastMessage = message;
        if (warning) Debug.LogWarning(message, this);
        else Debug.Log(message, this);
    }

    public bool NewGame()
    {
        if (busy || sleepTransition == null || sleepTransition.IsSleeping)
        {
            Report("저장 처리 또는 취침 전환 중에는 새 게임을 시작할 수 없습니다.");
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
                gold = 0,
                inventory = items.Select(b => new GameSaveData.ItemState
                {
                    id = b.id, quantity = b.id == WheatSeedsId ? 3 : b.id == WateringCanId ? 1 : 0
                }).ToArray(),
                mission = new GameSaveData.MissionState { stage = 1 },
                plots = plots.Select(b => new GameSaveData.PlotState { id = b.id }).ToArray(),
                chickens = chickens.Select(b => new GameSaveData.ChickenState { id = b.id }).ToArray(),
                cows = cows.Select(b => new GameSaveData.CowState { id = b.id }).ToArray(),
                flowerBeds = flowerBeds.Select(b => new GameSaveData.FlowerBedState { id = b.id }).ToArray(),
                hexGrids = new GameSaveData.HexGridState[0]
            };
            Validate(data);
            // Commit the reset to the auto slot first. A write failure leaves the old save and live state
            // intact; manual slots are never touched.
            WriteSave(AutoSlot, data);
            ApplyState(data);
            unreadableSlots[AutoSlot] = false;
            sessionStarted = true;
            Report($"새 게임 시작: 1일차 새 게임 데이터를 자동 저장 칸에 저장했습니다. 미션·인벤토리·밭·화단·동물·숲 Hex 상태가 초기화되었습니다. 수동 저장 칸은 그대로입니다.\n{GetSlotPath(AutoSlot)}");
            return true;
        }
        catch (Exception ex)
        {
            Report($"새 게임 시작 실패: {ex.Message}", true);
            return false;
        }
        finally { busy = false; }
    }

    // Main menu Continue and F9: the most recent readable slot. Uses the same load as the pause menu;
    // an older slot is tried only when a newer one fails validation.
    public bool ContinueGame()
    {
        if (!CanUseSaves("저장 처리, 취침 전환 또는 지역 이동 중에는 불러올 수 없습니다.")) return false;
        int[] slots = ReadableSlotsByRecency();
        if (slots.Length == 0)
        {
            Report("저장된 게임이 없습니다.");
            return false;
        }
        foreach (int slot in slots)
            if (TryLoadSlot(slot)) return true;
        return false;
    }

    // Sleep completed: always the auto slot, never a manual one.
    public bool TryAutoSave() => SaveToSlot(AutoSlot);

    // Player save from the pause menu: manual slots 1–3 only.
    public bool TrySaveSlot(int slot)
    {
        if (!IsValidSlot(slot) || slot == AutoSlot)
        {
            Report("자동 저장 칸에는 직접 저장할 수 없습니다. 수동 저장 1~3을 선택해 주세요.");
            return false;
        }
        return SaveToSlot(slot);
    }

    private bool SaveToSlot(int slot)
    {
        if (!CanUseSaves("저장할 수 없습니다: 취침·지역 이동 또는 저장 처리 중입니다.", report: false)) return false;
        // Without New Game / Continue / Load (Play started straight in a gameplay scene) the live state is
        // only the scene's defaults: the silent auto-save must not replace the auto slot with it. A manual
        // save is the player's explicit choice of the current state and is always allowed.
        if (slot == AutoSlot && !sessionStarted)
        {
            Report("자동 저장 대기: 새 게임·이어하기·불러오기로 시작한 게임만 자동 저장합니다. 기존 자동 저장은 유지됩니다.");
            return false;
        }
        busy = true;
        try
        {
            // The silent auto-save preserves an incompatible/damaged auto file instead of overwriting it.
            // A manual slot is overwritten only after the player confirms it.
            if (slot == AutoSlot && unreadableSlots[slot] && File.Exists(GetSlotPath(slot)))
                throw new InvalidDataException("불러오기에 실패한 자동 저장 파일을 보호하고 있습니다. 파일을 확인해 주세요.");
            GameSaveData data = Capture();
            Validate(data);
            WriteSave(slot, data);
            unreadableSlots[slot] = false;
            Report($"저장 완료: {GetSlotName(slot)} · {data.day}일차 · {GetSlotPath(slot)}");
            return true;
        }
        catch (Exception ex)
        {
            Report($"저장할 수 없습니다: {ex.Message}", true);
            return false;
        }
        finally { busy = false; }
    }

    // The single load path for every slot (pause menu and Continue).
    public bool TryLoadSlot(int slot)
    {
        if (!CanUseSaves("저장 처리, 취침 전환 또는 지역 이동 중에는 불러올 수 없습니다.")) return false;
        if (!IsValidSlot(slot))
        {
            Report("잘못된 저장 슬롯입니다.", true);
            return false;
        }
        MigrateLegacySave();
        string path = GetSlotPath(slot);
        if (!File.Exists(path))
        {
            Report($"{GetSlotName(slot)}: 저장된 게임이 없습니다.");
            return false;
        }
        busy = true;
        try
        {
            GameSaveData data = JsonUtility.FromJson<GameSaveData>(File.ReadAllText(path));
            // Validate the entire file and every reference before changing any gameplay state.
            Validate(data);
            ApplyState(data);
            unreadableSlots[slot] = false;
            sessionStarted = true;
            RestoreLocation(data);
            Report($"저장된 게임 불러오기 완료: {GetSlotName(slot)} · {data.day}일차. 날짜 진행·성장·생산·보상은 다시 실행하지 않았습니다.");
            return true;
        }
        catch (Exception ex)
        {
            unreadableSlots[slot] = true;
            Report($"불러오기 실패 ({GetSlotName(slot)}): {ex.Message}", true);
            return false;
        }
        finally { busy = false; }
    }

    // Save load wins over travel spawns: the saved scene and player position. Older saves have no scene
    // and continue in this scene (FarmScene), where its player already stands.
    private void RestoreLocation(GameSaveData data)
    {
        bool saved = !string.IsNullOrEmpty(data.scene);
        string scene = saved ? data.scene : gameObject.scene.path;
        if (!SceneTransitionService.RestoreLocation(scene, saved ? data.playerPosition : (Vector3?)null))
            Debug.LogWarning($"저장 위치로 이동하지 못했습니다. 현재 위치에서 계속합니다: {scene}", this);
    }

    private bool IsGameplayScene(string path) =>
        !string.IsNullOrEmpty(path) && gameplayScenes != null && gameplayScenes.Contains(path);

    private bool CanUseSaves(string blockedMessage, bool report = true)
    {
        if (!busy && !SceneTransitionService.IsTraveling && sleepTransition != null && !sleepTransition.IsSleeping) return true;
        if (report) Report(blockedMessage);
        else LastMessage = blockedMessage;
        return false;
    }

    private static void WriteSave(int slot, GameSaveData data)
    {
        MigrateLegacySave();
        Directory.CreateDirectory(Application.persistentDataPath);
        string path = GetSlotPath(slot), temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonUtility.ToJson(data, true), new System.Text.UTF8Encoding(false));
        // Same-directory replace keeps this slot's previous save intact if writing is interrupted.
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }

    private void ApplyState(GameSaveData data)
    {
        var restoredInventory = data.inventory.ToDictionary(s => items.Single(b => b.id == s.id).item, s => s.quantity);
        gameDay.RestoreDay(data.day);
        mission.RestoreState(data.mission);
        foreach (var saved in data.plots) plots.Single(b => b.id == saved.id).plot.RestoreState(saved);
        foreach (var saved in data.chickens) chickens.Single(b => b.id == saved.id).chicken.RestoreState(saved);
        foreach (var saved in data.cows) cows.Single(b => b.id == saved.id).cow.RestoreState(saved);
        // Older saves have no flower beds: every bed restores as empty.
        foreach (var binding in flowerBeds)
            binding.bed.RestoreState(data.flowerBeds?.SingleOrDefault(s => s.id == binding.id));
        inventory.RestoreQuantities(restoredInventory);
        wallet.RestoreGold(data.gold);
        quickbar.SelectSlot(data.selectedSlot);
        // Region grids that are not loaded yet keep their state until their scene awakes.
        EcoGridManager.RestoreAll(data.hexGrids);
    }

    private GameSaveData Capture()
    {
        ValidateBindings();
        var quantities = inventory.CaptureQuantities();
        Require(quantities.Keys.All(item => items.Any(b => b.item == item)), "저장 목록에 등록되지 않은 아이템이 있습니다.");
        // The player's actual scene: travel and Continue keep exactly that one active, suspended regions
        // are inactive, and the main menu has no player and is not a gameplay scene.
        Scene active = SceneManager.GetActiveScene();
        Transform player = SceneTransitionService.FindPlayerRoot(active);
        Require(IsGameplayScene(active.path), $"현재 Scene({active.name})은 게임 지역 목록에 없습니다.");
        Require(player != null && player.gameObject.activeInHierarchy, "플레이어를 찾지 못했습니다.");
        return new GameSaveData
        {
            scene = active.path, playerPosition = player.position,
            version = 1, day = gameDay.CurrentDay, selectedSlot = quickbar.SelectedSlotIndex,
            gold = wallet.Gold,
            inventory = items.Select(b => new GameSaveData.ItemState { id = b.id, quantity = inventory.GetQuantity(b.item) }).ToArray(),
            mission = mission.CaptureState(),
            plots = plots.Select(b => b.plot.CaptureState(b.id)).ToArray(),
            chickens = chickens.Select(b => b.chicken.CaptureState(b.id)).ToArray(),
            cows = cows.Select(b => b.cow.CaptureState(b.id)).ToArray(),
            flowerBeds = flowerBeds.Select(b => b.bed.CaptureState(b.id)).ToArray(),
            hexGrids = EcoGridManager.CaptureAll()
        };
    }

    private void ValidateBindings()
    {
        Require(gameDay != null && sleepTransition != null && inventory != null && wallet != null && quickbar != null &&
            mission != null && mission.SaveReferencesValid, "저장 시스템 기본 연결이 누락되었습니다.");
        Require(items != null && items.Length > 0 && items.All(b => b != null && b.item != null), "아이템 연결 누락");
        Require(plots != null && plots.Length > 0 && plots.All(b => b != null && b.plot != null && b.plot.SaveReferencesValid), "밭 연결 누락");
        Require(chickens != null && chickens.All(b => b != null && b.chicken != null && b.chicken.SaveReferencesValid), "닭 연결 누락");
        Require(cows != null && cows.All(b => b != null && b.cow != null && b.cow.SaveReferencesValid), "소 연결 누락");
        Require(flowerBeds != null && flowerBeds.All(b => b != null && b.bed != null && b.bed.SaveReferencesValid), "화단 연결 누락");
        Require(IsGameplayScene(gameObject.scene.path), "게임 지역 Scene 목록에 저장 시스템의 Scene이 없습니다.");
        CheckIds(items.Select(b => b.id)); CheckIds(plots.Select(b => b.id));
        CheckIds(chickens.Select(b => b.id)); CheckIds(cows.Select(b => b.id)); CheckIds(flowerBeds.Select(b => b.id));
        Require(items.Select(b => b.item).Distinct().Count() == items.Length &&
            plots.Select(b => b.plot).Distinct().Count() == plots.Length &&
            chickens.Select(b => b.chicken).Distinct().Count() == chickens.Length &&
            cows.Select(b => b.cow).Distinct().Count() == cows.Length &&
            flowerBeds.Select(b => b.bed).Distinct().Count() == flowerBeds.Length, "중복 연결이 있습니다.");
    }

    private void Validate(GameSaveData data)
    {
        ValidateBindings();
        Require(data != null && data.version == 1 && data.day >= 1, "지원하지 않거나 손상된 저장 파일입니다.");
        Require(data.selectedSlot >= 0 && data.selectedSlot < PlayerQuickbar.SlotCount, "퀵슬롯 값 오류");
        Require(data.gold >= 0, "골드 값 오류");
        Require(data.inventory != null && data.inventory.All(s => s != null && s.quantity >= 0), "인벤토리 값 오류");
        Require(data.plots != null && data.plots.All(s => s != null), "밭 데이터 누락");
        Require(data.chickens != null && data.chickens.All(s => s != null), "닭 데이터 누락");
        Require(data.cows != null && data.cows.All(s => s != null), "소 데이터 누락");
        MatchInventoryIds(data.inventory.Select(s => s.id));
        MatchIds(data.plots.Select(s => s.id), plots.Select(b => b.id));
        MatchIds(data.chickens.Select(s => s.id), chickens.Select(b => b.id));
        MatchIds(data.cows.Select(s => s.id), cows.Select(b => b.id));
        if (data.flowerBeds != null)
        {
            Require(data.flowerBeds.All(s => s != null && Enum.IsDefined(typeof(FlowerBed.BedState), s.state)), "화단 상태 오류");
            MatchIds(data.flowerBeds.Select(s => s.id), flowerBeds.Select(b => b.id));
        }
        ValidateHexGrids(data.hexGrids);
        // Optional (older saves have none). Checked before any state changes or scene loads; the position
        // itself is never clamped or moved.
        if (!string.IsNullOrEmpty(data.scene))
        {
            Require(IsGameplayScene(data.scene) && Application.CanStreamedLevelBeLoaded(data.scene), "저장된 지역 Scene이 없거나 게임 지역이 아닙니다.");
            Vector3 p = data.playerPosition;
            Require(IsFinite(p.x) && IsFinite(p.y) && IsFinite(p.z), "플레이어 위치 값 오류");
        }
        var m = data.mission;
        Require(m != null, "미션 데이터 누락");
        Require(m.wheatHarvested >= 0 && m.wheatHarvested <= 9 && m.eggsCollected >= 0 && m.eggsCollected <= 4 &&
            m.cornHarvested >= 0 && m.cornHarvested <= 9 && m.milkCollected >= 0 && m.milkCollected <= 4, "미션 수량 오류");
        Require((!m.m02CompletedAndRewarded || m.m01CompletedAndRewarded) &&
            (!m.m03CompletedAndRewarded || m.m02CompletedAndRewarded) && (!m.prologueCompleted || m.m03CompletedAndRewarded), "미션 완료 순서 오류");
        Require(m.stage == (m.prologueCompleted ? 5 : m.m03CompletedAndRewarded ? 4 : m.m02CompletedAndRewarded ? 3 : m.m01CompletedAndRewarded ? 2 : 1), "미션 단계 오류");
        // chickenUnlocked / cowUnlocked are legacy: restore derives the areas from M01 / M03, so a stale value
        // (older builds captured it from an area suspended by region travel) is ignored instead of rejecting the save.
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
    // Optional (older saves have none). Coordinates are checked against the map when the grid restores.
    private static void ValidateHexGrids(GameSaveData.HexGridState[] grids)
    {
        if (grids == null) return;
        Require(grids.All(g => g != null && !string.IsNullOrWhiteSpace(g.id)) && grids.Select(g => g.id).Distinct().Count() == grids.Length, "Hex Grid ID 누락/중복");
        foreach (var grid in grids)
        {
            var cells = grid.cells ?? new GameSaveData.HexCellState[0];
            Require(cells.All(c => c != null && c.tileType != EnvironmentTileType.None && Enum.IsDefined(typeof(EnvironmentTileType), c.tileType)), "Hex 환경 종류 오류");
            Require(cells.Select(c => (c.q, c.r)).Distinct().Count() == cells.Length, "Hex 좌표 중복");
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void CheckIds(IEnumerable<string> ids)
    {
        var values = ids.ToArray();
        Require(values.All(id => !string.IsNullOrWhiteSpace(id)) && values.Distinct().Count() == values.Length, "저장 ID 누락/중복");
    }
    // Adding Wood must neither reject existing saves nor silently discard unknown saved items.
    private void MatchInventoryIds(IEnumerable<string> saved)
    {
        string[] values = saved.ToArray();
        CheckIds(values);
        var ids = new HashSet<string>(values);
        Require(ids.IsSubsetOf(items.Select(b => b.id)) &&
            items.Where(b => !b.optionalInOlderSaves).All(b => ids.Contains(b.id)),
            "저장 파일과 현재 Scene의 아이템 ID 목록이 다릅니다.");
    }
    private static void MatchIds(IEnumerable<string> saved, IEnumerable<string> configured)
    {
        CheckIds(saved);
        Require(new HashSet<string>(saved).SetEquals(configured), "저장 파일과 현재 Scene의 ID 목록이 다릅니다.");
    }
}
