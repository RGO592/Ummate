using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// One-time (idempotent) scene wiring for the Energy / stamina potion / trading post purchase feature.
// Menu: Tools/Susupjung/Energy Setup. Safe to run again: anything already connected is left as it is.
public static class EnergySetupTool
{
    private const string PotionPath = "Assets/_Project/Items/StaminaPotion.asset";
    private const string PotionSaveId = "StaminaPotion";
    private const int PotionQuickbarIndex = 8; // key 9
    private static readonly string[] VillageScenes =
    {
        "Assets/_Project/Scenes/FarmLayout_WorkScene.unity",
    };
    private const string SpiritScene = "Assets/_Project/Scenes/SpiritGridScene.unity";
    // SpiritGridScene: above the build-mode slot bar (y 16–184), same right margin.
    private static readonly Vector2 SpiritHudOffset = new Vector2(20f, 192f);
    private static readonly Vector2 VillageHudOffset = new Vector2(20f, 20f);

    [MenuItem("Tools/Susupjung/Energy Setup (씬 연결)")]
    public static void RunFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Run();
    }

    // Batch mode entry: Unity -batchmode -executeMethod EnergySetupTool.RunBatch -quit
    public static void RunBatch()
    {
        bool ok = Run();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private static bool Run()
    {
        CheckSaveFallback();
        ItemDefinition potion = AssetDatabase.LoadAssetAtPath<ItemDefinition>(PotionPath);
        if (potion == null)
        {
            Debug.LogError($"[EnergySetup] {PotionPath} 를 찾을 수 없습니다.");
            return false;
        }
        Debug.Log($"[EnergySetup] 물약: {potion.DisplayName} · 구매가 {potion.BuyPrice}G · 회복 {potion.EnergyRestore}");

        bool ok = true;
        foreach (string path in VillageScenes) ok &= SetupVillage(path, potion);
        ok &= SetupSpirit();
        AssetDatabase.SaveAssets();
        Debug.Log(ok ? "[EnergySetup] 완료" : "[EnergySetup] 일부 실패 — 위 로그를 확인하세요.");
        return ok;
    }

    // GameSaveData.currentEnergy relies on its field initializer (-1) surviving JsonUtility for older saves.
    private static void CheckSaveFallback()
    {
        GameSaveData old = JsonUtility.FromJson<GameSaveData>("{\"version\":1,\"day\":3,\"gold\":5}");
        Debug.Log(old.currentEnergy == -1
            ? "[EnergySetup] 구버전 Save 확인: currentEnergy 없음 → -1 (최대치로 불러옴) OK"
            : $"[EnergySetup] 경고: 구버전 Save의 currentEnergy가 {old.currentEnergy}로 읽힙니다.");
    }

    private static bool SetupVillage(string path, ItemDefinition potion)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) { Debug.LogWarning($"[EnergySetup] Scene 없음: {path}"); return true; }
        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        string label = scene.name;

        PlayerWallet wallet = Find<PlayerWallet>(scene).SingleOrDefault();
        if (wallet == null) { Debug.LogError($"[EnergySetup] {label}: PlayerWallet을 찾지 못했습니다."); return false; }

        // 1. PlayerEnergy next to the wallet.
        PlayerEnergy energy = wallet.GetComponent<PlayerEnergy>();
        if (energy == null)
        {
            energy = Undo.AddComponent<PlayerEnergy>(wallet.gameObject);
            Debug.Log($"[EnergySetup] {label}: PlayerEnergy 추가 → {wallet.name}");
        }
        var energyObject = new SerializedObject(energy);
        SetIfEmpty(energyObject, "gameDay", Find<GameDay>(scene).FirstOrDefault());
        SetIfEmpty(energyObject, "developerSource", Find<GuideTooltip>(scene).FirstOrDefault());
        energyObject.ApplyModifiedPropertiesWithoutUndo();

        // 2. Save system: energy reference + potion item binding (optional in older saves).
        foreach (GameSaveSystem save in Find<GameSaveSystem>(scene))
        {
            var so = new SerializedObject(save);
            SetIfEmpty(so, "energy", energy);
            SerializedProperty items = so.FindProperty("items");
            bool bound = false;
            for (int i = 0; i < items.arraySize; i++)
                if (items.GetArrayElementAtIndex(i).FindPropertyRelative("item").objectReferenceValue == potion) bound = true;
            if (!bound)
            {
                items.arraySize++;
                SerializedProperty entry = items.GetArrayElementAtIndex(items.arraySize - 1);
                entry.FindPropertyRelative("id").stringValue = PotionSaveId;
                entry.FindPropertyRelative("item").objectReferenceValue = potion;
                entry.FindPropertyRelative("optionalInOlderSaves").boolValue = true;
                Debug.Log($"[EnergySetup] {label}: GameSaveSystem 아이템 목록에 {PotionSaveId} 추가 (구버전 Save 호환)");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 3. Quickbar slot for the potion.
        foreach (PlayerQuickbar quickbar in Find<PlayerQuickbar>(scene))
        {
            var so = new SerializedObject(quickbar);
            SerializedProperty slots = so.FindProperty("slots");
            bool present = false;
            for (int i = 0; i < slots.arraySize; i++)
                if (slots.GetArrayElementAtIndex(i).objectReferenceValue == potion) present = true;
            if (!present && PotionQuickbarIndex < slots.arraySize)
            {
                SerializedProperty slot = slots.GetArrayElementAtIndex(PotionQuickbarIndex);
                Object previous = slot.objectReferenceValue;
                slot.objectReferenceValue = potion;
                Debug.Log($"[EnergySetup] {label}: 퀵슬롯 {PotionQuickbarIndex + 1}번 {(previous != null ? previous.name : "빈 칸")} → {potion.name}");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 4. Trading post buy catalog.
        foreach (TradingPostUI trading in Find<TradingPostUI>(scene))
        {
            var so = new SerializedObject(trading);
            SerializedProperty catalog = so.FindProperty("buyCatalog");
            bool listed = false;
            for (int i = 0; i < catalog.arraySize; i++)
                if (catalog.GetArrayElementAtIndex(i).objectReferenceValue == potion) listed = true;
            if (!listed)
            {
                catalog.arraySize++;
                catalog.GetArrayElementAtIndex(catalog.arraySize - 1).objectReferenceValue = potion;
                Debug.Log($"[EnergySetup] {label}: 교역소 구매 목록에 {potion.name} 추가");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            ReportInputLock(label, trading, "inputComponents");
        }

        // 5. Energy HUD.
        EnsureHud(scene, VillageHudOffset);

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        Debug.Log($"[EnergySetup] {label}: 저장 {(saved ? "완료" : "실패")}");
        return saved;
    }

    private static bool SetupSpirit()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SpiritScene) == null) return true;
        Scene scene = EditorSceneManager.OpenScene(SpiritScene, OpenSceneMode.Single);
        EnsureHud(scene, SpiritHudOffset);
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        Debug.Log($"[EnergySetup] {scene.name}: 저장 {(saved ? "완료" : "실패")}");
        return saved;
    }

    private static void EnsureHud(Scene scene, Vector2 offset)
    {
        if (Find<EnergyHUD>(scene).Any()) { Debug.Log($"[EnergySetup] {scene.name}: EnergyHUD 이미 있음"); return; }
        var go = new GameObject("EnergyHUD");
        SceneManager.MoveGameObjectToScene(go, scene);
        EnergyHUD hud = go.AddComponent<EnergyHUD>();
        var so = new SerializedObject(hud);
        // Same Korean font as the scene's feedback UI.
        PlayerFeedbackUI feedback = Find<PlayerFeedbackUI>(scene).FirstOrDefault();
        if (feedback != null) so.FindProperty("font").objectReferenceValue = new SerializedObject(feedback).FindProperty("font").objectReferenceValue;
        so.FindProperty("cornerOffset").vector2Value = offset;
        so.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log($"[EnergySetup] {scene.name}: EnergyHUD 추가 (우하단 offset {offset})");
    }

    private static void ReportInputLock(string label, Object owner, string property)
    {
        SerializedProperty list = new SerializedObject(owner).FindProperty(property);
        var names = new List<string>();
        for (int i = 0; list != null && i < list.arraySize; i++)
        {
            Object value = list.GetArrayElementAtIndex(i).objectReferenceValue;
            names.Add(value != null ? value.GetType().Name : "null");
        }
        Debug.Log($"[EnergySetup] {label}: {owner.GetType().Name}.{property} = [{string.Join(", ", names)}]");
    }

    private static void SetIfEmpty(SerializedObject so, string property, Object value)
    {
        SerializedProperty p = so.FindProperty(property);
        if (p != null && p.objectReferenceValue == null && value != null) p.objectReferenceValue = value;
    }

    private static IEnumerable<T> Find<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
}
