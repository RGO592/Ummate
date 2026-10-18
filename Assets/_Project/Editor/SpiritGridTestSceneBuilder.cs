using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Manual menus only (no InitializeOnLoad). Nothing is saved without the user choosing a file in a save dialog.
//  - Create Spirit Grid Scene: builds the spirit realm scene (explore + build mode, build bar, DEV toggle, return portal).
//  - Add Dimension Portal To FarmScene: adds one portal object to the open FarmScene (save with Ctrl+S yourself).
public static class SpiritGridTestSceneBuilder
{
    const string FarmScenePath = "Assets/_Project/Scenes/FarmScene.unity";
    const string SpiritSceneName = "SpiritGridScene";
    const string MaterialFolder = "Assets/_Project/Materials/SpiritGrid";
    const string KoreanFontPath = "Assets/_Project/Fonts/InventoryKorean.asset";
    const string PlayerModelPath = "Assets/_Project/Art/Player.fbx";
    const string UiFolder = "Assets/_Project/UI/";

    // Farm portal default spot: east end of the main path, north of the house, facing the path (-X).
    static readonly Vector3 FarmPortalPosition = new Vector3(15.5f, 0f, -1.5f);
    const float FarmPortalYaw = -90f;

    const int GridCells = 6;
    const float CellSize = 10f;         // one cell = the old 4x4 block
    const float GridHalf = GridCells * CellSize * 0.5f;
    const float CurbWidth = 1.5f;
    const float WorldHalf = 130f;
    // Spawn and return portal sit on the open ground south of the core area.
    const float SpawnZ = -(GridHalf + CurbWidth + 8f);

    // ------------------------------------------------------------------ Spirit scene

    [MenuItem("Tools/Ummate/Spirit Grid/Create Spirit Grid Scene")]
    public static void CreateSpiritScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Spirit Grid", "Play Mode를 종료한 뒤 실행해 주세요.", "확인");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var light = new GameObject("Directional Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.shadows = LightShadows.Soft;
        light.transform.rotation = new Quaternion(0.41f, -0.23f, 0.11f, 0.88f).normalized;   // same as the farm

        BuildSpiritEnvironment();

        var gridRoot = new GameObject("EcoGridRoot");
        var managerGo = new GameObject("EcoGridManager");
        EcoGridManager grid = managerGo.AddComponent<EcoGridManager>();
        EcoPlacementController placement = managerGo.AddComponent<EcoPlacementController>();
        Set(grid, "gridRoot", gridRoot.transform);
        var gridSo = new SerializedObject(grid);
        gridSo.FindProperty("width").intValue = GridCells;
        gridSo.FindProperty("height").intValue = GridCells;
        gridSo.FindProperty("cellSize").floatValue = CellSize;
        gridSo.ApplyModifiedPropertiesWithoutUndo();
        Set(placement, "grid", grid);

        GameObject player = BuildPlayer(new Vector3(0f, 1f, SpawnZ));

        var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
        Camera camera = cameraGo.AddComponent<Camera>();
        camera.fieldOfView = 60f;
        cameraGo.AddComponent<AudioListener>();
        Quaternion view = Quaternion.Euler(45f, 45f, 0f);
        cameraGo.transform.SetPositionAndRotation(player.transform.position + view * Vector3.back * 12f, view);
        CameraFollow follow = cameraGo.AddComponent<CameraFollow>();
        Set(follow, "target", player.transform);
        Set(placement, "viewCamera", camera);

        DimensionPortal returnPortal = BuildPortal("ReturnPortal", DimensionPortal.PortalMode.ReturnToFarm,
            new Vector3(-9f, 0f, SpawnZ), 90f);

        var buildModeGo = new GameObject("SpiritBuildMode");
        SpiritBuildModeController buildMode = buildModeGo.AddComponent<SpiritBuildModeController>();

        BuildSpiritUI(grid, placement, buildMode, returnPortal, out GameObject exploreHud, out GameObject buildHud);

        Set(buildMode, "viewCamera", camera);
        Set(buildMode, "exploreFollow", follow);
        Set(buildMode, "grid", grid);
        Set(buildMode, "placement", placement);
        Set(buildMode, "player", player);
        SetArray(buildMode, "buildModeObjects", buildHud);
        SetArray(buildMode, "exploreModeObjects", exploreHud);

        var eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<InputSystemUIInputModule>();

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = buildModeGo;

        if (EditorUtility.DisplayDialog("Spirit Grid",
                $"생태계 씬을 만들었습니다.\n\n지금 저장하고 Build Settings에 등록할까요?\n" +
                $"차원문이 '{SpiritSceneName}' 이름으로 씬을 불러오므로 파일 이름은 {SpiritSceneName}로 저장해 주세요.",
                "저장 위치 선택", "나중에"))
        {
            string path = EditorUtility.SaveFilePanelInProject("Spirit Grid 씬 저장", SpiritSceneName, "unity",
                "생태계 씬을 저장할 위치를 선택하세요.", "Assets/_Project/Scenes");
            if (!string.IsNullOrEmpty(path) && EditorSceneManager.SaveScene(scene, path))
                RegisterInBuildSettings(path);
        }
        else Debug.Log($"생태계 씬은 아직 저장되지 않았습니다. 저장 후 'Tools/Ummate/Spirit Grid/Register Current Scene In Build Settings'를 실행해 주세요.");
    }

    [MenuItem("Tools/Ummate/Spirit Grid/Register Current Scene In Build Settings")]
    public static void RegisterCurrentScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(scene.path))
        {
            EditorUtility.DisplayDialog("Spirit Grid", "먼저 씬을 저장해 주세요.", "확인");
            return;
        }
        RegisterInBuildSettings(scene.path);
    }

    static void RegisterInBuildSettings(string path)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == path))
        {
            Debug.Log($"이미 Build Settings에 등록되어 있습니다: {path}");
            return;
        }
        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log($"Build Settings에 등록했습니다: {path}");
        if (System.IO.Path.GetFileNameWithoutExtension(path) != SpiritSceneName)
            Debug.LogWarning($"씬 이름이 '{SpiritSceneName}'이 아닙니다. 두 차원문의 Spirit Scene Name을 맞춰 주세요.");
    }

    // Wide ground with a hole for the 6x6 grid, a stone curb around the core area,
    // and four faint locked-area pads hinting at later expansion.
    static void BuildSpiritEnvironment()
    {
        Transform env = new GameObject("Environment").transform;
        Material grass = Mat("SG_Grass", new Color(0.45f, 0.62f, 0.34f));
        Material curb = Mat("SG_Curb", new Color(0.62f, 0.6f, 0.56f));
        Material locked = Mat("SG_LockedArea", new Color(0.38f, 0.53f, 0.3f));

        Transform ground = new GameObject("OuterGround").transform;
        ground.SetParent(env, false);
        float hole = GridHalf + CurbWidth;
        Box(ground, "Ground_North", new Vector3(-WorldHalf, -0.3f, hole), new Vector3(WorldHalf, 0f, WorldHalf), grass, true);
        Box(ground, "Ground_South", new Vector3(-WorldHalf, -0.3f, -WorldHalf), new Vector3(WorldHalf, 0f, -hole), grass, true);
        Box(ground, "Ground_East", new Vector3(hole, -0.3f, -hole), new Vector3(WorldHalf, 0f, hole), grass, true);
        Box(ground, "Ground_West", new Vector3(-WorldHalf, -0.3f, -hole), new Vector3(-hole, 0f, hole), grass, true);

        Transform border = new GameObject("CoreAreaCurb").transform;
        border.SetParent(env, false);
        Box(border, "Curb_North", new Vector3(-hole, -1.5f, GridHalf), new Vector3(hole, 0.12f, hole), curb, true);
        Box(border, "Curb_South", new Vector3(-hole, -1.5f, -hole), new Vector3(hole, 0.12f, -GridHalf), curb, true);
        Box(border, "Curb_East", new Vector3(GridHalf, -1.5f, -GridHalf), new Vector3(hole, 0.12f, GridHalf), curb, true);
        Box(border, "Curb_West", new Vector3(-hole, -1.5f, -GridHalf), new Vector3(-GridHalf, 0.12f, GridHalf), curb, true);

        // Future expansion areas (not interactive yet).
        Transform expansion = new GameObject("ExpansionAreas_Locked").transform;
        expansion.SetParent(env, false);
        float gap = hole + 4f, size = GridHalf * 2f;
        Box(expansion, "Locked_North", new Vector3(-GridHalf, 0f, gap), new Vector3(GridHalf, 0.01f, gap + size), locked, false);
        Box(expansion, "Locked_East", new Vector3(gap, 0f, -GridHalf), new Vector3(gap + size, 0.01f, GridHalf), locked, false);
        Box(expansion, "Locked_West", new Vector3(-gap - size, 0f, -GridHalf), new Vector3(-gap, 0.01f, GridHalf), locked, false);
    }

    static GameObject BuildPlayer(Vector3 position)
    {
        var player = new GameObject("SpiritPlayer");
        player.transform.position = position;
        player.AddComponent<CapsuleCollider>();
        Rigidbody body = player.AddComponent<Rigidbody>();
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        player.AddComponent<PlayerMovement>();
        player.AddComponent<PlayerInteractor>();

        var visual = new GameObject("Visual");
        visual.transform.SetParent(player.transform, false);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelPath);
        if (model != null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.transform.SetParent(visual.transform, false);
            instance.transform.localPosition = new Vector3(0f, 0.4f, 0f);   // same as the farm player
            instance.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            instance.transform.localScale = Vector3.one * 40f;
        }
        else Debug.LogWarning($"플레이어 모델을 찾지 못했습니다: {PlayerModelPath}");
        return player;
    }

    // ------------------------------------------------------------------ Portal (shared by both scenes)

    static DimensionPortal BuildPortal(string name, DimensionPortal.PortalMode mode, Vector3 position, float yaw)
    {
        Material frame = Mat("SG_PortalFrame", new Color(0.36f, 0.3f, 0.42f));
        Material back = Mat("SG_PortalInner", new Color(0.25f, 0.12f, 0.42f));
        Material swirlMat = Mat("SG_PortalSwirl", new Color(0.65f, 0.45f, 1f), new Color(0.6f, 0.35f, 1f) * 1.5f);

        var root = new GameObject(name);
        root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        var collider = root.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, 2.5f, 0f);
        collider.size = new Vector3(3.8f, 5f, 0.8f);
        DimensionPortal portal = root.AddComponent<DimensionPortal>();

        Transform visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);
        Box(visual, "Step", new Vector3(-2.1f, 0f, -0.8f), new Vector3(2.1f, 0.2f, 0.8f), frame, false);
        Box(visual, "Pillar_L", new Vector3(-1.9f, 0.2f, -0.35f), new Vector3(-1.4f, 4.8f, 0.35f), frame, false);
        Box(visual, "Pillar_R", new Vector3(1.4f, 0.2f, -0.35f), new Vector3(1.9f, 4.8f, 0.35f), frame, false);
        Box(visual, "Lintel", new Vector3(-2.1f, 4.8f, -0.4f), new Vector3(2.1f, 5.3f, 0.4f), frame, false);
        Box(visual, "Inner", new Vector3(-1.4f, 0.2f, -0.02f), new Vector3(1.4f, 4.8f, 0.02f), back, false);
        GameObject swirl = Box(visual, "Swirl", new Vector3(-0.95f, 1.55f, -0.08f), new Vector3(0.95f, 3.45f, 0.08f), swirlMat, false);
        swirl.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        var so = new SerializedObject(portal);
        so.FindProperty("mode").intValue = (int)mode;
        so.FindProperty("swirl").objectReferenceValue = swirl.transform;
        so.ApplyModifiedPropertiesWithoutUndo();
        return portal;
    }

    [MenuItem("Tools/Ummate/Spirit Grid/Add Dimension Portal To FarmScene")]
    public static void AddPortalToFarm()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Spirit Grid", "Play Mode를 종료한 뒤 실행해 주세요.", "확인");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != FarmScenePath)
        {
            EditorUtility.DisplayDialog("Spirit Grid", $"FarmScene을 연 상태에서 실행해 주세요.\n({FarmScenePath})", "확인");
            return;
        }
        GameObject existing = scene.GetRootGameObjects().FirstOrDefault(g => g.GetComponent<DimensionPortal>() != null);
        if (existing != null)
        {
            Selection.activeGameObject = existing;
            EditorUtility.DisplayDialog("Spirit Grid", "FarmScene에 이미 차원문이 있습니다. 선택해 두었습니다.", "확인");
            return;
        }
        DimensionPortal portal = BuildPortal("DimensionPortal", DimensionPortal.PortalMode.EnterSpiritRealm, FarmPortalPosition, FarmPortalYaw);
        Undo.RegisterCreatedObjectUndo(portal.gameObject, "Add Dimension Portal");
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = portal.gameObject;
        EditorUtility.DisplayDialog("Spirit Grid",
            "FarmScene에 차원문을 추가했습니다 (저장하지 않음).\n위치를 확인한 뒤 직접 Ctrl+S로 저장해 주세요.\nCtrl+Z로 되돌릴 수 있습니다.", "확인");
    }

    // ------------------------------------------------------------------ UI

    static void BuildSpiritUI(EcoGridManager grid, EcoPlacementController placement, SpiritBuildModeController buildMode,
        DimensionPortal returnPortal, out GameObject exploreHud, out GameObject buildHud)
    {
        var canvasGo = new GameObject("SpiritUI", typeof(RectTransform));
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        Transform root = canvasGo.transform;

        Sprite panel = LoadSprite("사각형_패널_배경.png");
        Sprite slotNormal = LoadSprite("퀵슬롯_기본.png");
        Sprite slotSelected = LoadSprite("퀵슬롯_선택.png");
        Sprite circle = LoadSprite("원형_배경.png");
        var panelColor = new Color(1f, 0.97f, 0.9f, 0.96f);
        var dark = new Color(0.25f, 0.2f, 0.16f);

        // Explore HUD
        exploreHud = Stretch(root, "ExploreHUD");
        Button back = MakeButton(exploreHud.transform, "ReturnButton", "← 농장으로 돌아가기", 20,
            new Vector2(0f, 1f), new Vector2(16f, -16f), new Vector2(250f, 56f), panel, panelColor, dark);
        UnityEventTools.AddVoidPersistentListener(back.onClick, returnPortal.Use);
        Button enter = MakeButton(exploreHud.transform, "BuildButton", "건설  (B)", 26,
            new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(210f, 76f), panel, new Color(1f, 0.85f, 0.45f), dark);
        UnityEventTools.AddVoidPersistentListener(enter.onClick, buildMode.EnterBuildMode);
        MakeText(exploreHud.transform, "ExploreHint", "WASD 이동 · 차원문 앞에서 F: 농장으로 · B: 건설 모드", 18, Color.white,
            new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(700f, 36f), TextAlignmentOptions.Center);

        // Build HUD
        buildHud = Stretch(root, "BuildHUD");
        GameObject title = MakePanel(buildHud.transform, "TitlePanel", new Vector2(0f, 1f), new Vector2(16f, -16f), new Vector2(200f, 56f), panel, panelColor);
        MakeText(title.transform, "Title", "건설 모드", 26, dark, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 56f), TextAlignmentOptions.Center);
        Button done = MakeButton(buildHud.transform, "DoneButton", "완료 (B)", 22,
            new Vector2(0f, 1f), new Vector2(228f, -16f), new Vector2(150f, 56f), panel, new Color(1f, 0.85f, 0.45f), dark);
        UnityEventTools.AddVoidPersistentListener(done.onClick, buildMode.ExitBuildMode);
        MakeText(buildHud.transform, "ControlsHint", "WASD 화면 이동 · 휠 확대/축소 · 1~5 종류 · 우클릭/Esc 취소 · Delete 제거", 16, Color.white,
            new Vector2(0f, 1f), new Vector2(18f, -80f), new Vector2(700f, 30f), TextAlignmentOptions.Left);

        // Status line above the bar
        GameObject statusPanel = MakePanel(buildHud.transform, "StatusPanel", new Vector2(0.5f, 0f), new Vector2(0f, 196f), new Vector2(760f, 42f), panel, new Color(0.15f, 0.12f, 0.1f, 0.75f));
        TMP_Text status = MakeText(statusPanel.transform, "StatusText", "", 18, Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(740f, 42f), TextAlignmentOptions.Center);

        // Bottom build bar
        var bar = new GameObject("BuildBar", typeof(RectTransform));
        bar.transform.SetParent(buildHud.transform, false);
        var barRect = (RectTransform)bar.transform;
        barRect.anchorMin = new Vector2(0f, 0f);
        barRect.anchorMax = new Vector2(1f, 0f);
        barRect.pivot = new Vector2(0.5f, 0f);
        barRect.offsetMin = new Vector2(40f, 16f);
        barRect.offsetMax = new Vector2(-40f, 184f);
        Image barImage = bar.AddComponent<Image>();
        ApplySprite(barImage, panel, panelColor);
        MakeText(bar.transform, "Category", "환경 칸", 20, dark, new Vector2(0f, 1f), new Vector2(24f, -8f), new Vector2(200f, 30f), TextAlignmentOptions.Left);

        EcoBuildBarUI barUi = bar.AddComponent<EcoBuildBarUI>();
        var barSo = new SerializedObject(barUi);
        barSo.FindProperty("placement").objectReferenceValue = placement;
        barSo.FindProperty("grid").objectReferenceValue = grid;
        barSo.FindProperty("normalSlotSprite").objectReferenceValue = slotNormal;
        barSo.FindProperty("selectedSlotSprite").objectReferenceValue = slotSelected;
        barSo.FindProperty("statusLabel").objectReferenceValue = status;
        SerializedProperty slots = barSo.FindProperty("slots");
        slots.arraySize = EnvironmentTileTypes.All.Length;
        for (int i = 0; i < EnvironmentTileTypes.All.Length; i++)
        {
            EnvironmentTileType type = EnvironmentTileTypes.All[i];
            float x = 24f + i * 128f;
            var slotGo = new GameObject($"Slot_{type}", typeof(RectTransform));
            slotGo.transform.SetParent(bar.transform, false);
            SetRect((RectTransform)slotGo.transform, new Vector2(0f, 0f), new Vector2(x, 14f), new Vector2(116f, 116f), new Vector2(0f, 0f));
            Image bg = slotGo.AddComponent<Image>();
            ApplySprite(bg, slotNormal, Color.white);
            Button button = slotGo.AddComponent<Button>();
            button.targetGraphic = bg;
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(slotGo.transform, false);
            SetRect((RectTransform)iconGo.transform, new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(52f, 52f), new Vector2(0.5f, 1f));
            Image icon = iconGo.AddComponent<Image>();
            icon.color = EnvironmentTileTypes.DefaultColor(type);
            icon.raycastTarget = false;
            TMP_Text nameLabel = MakeText(slotGo.transform, "Name", EnvironmentTileTypes.DisplayName(type), 20, dark,
                new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(110f, 26f), TextAlignmentOptions.Center);
            TMP_Text costLabel = MakeText(slotGo.transform, "Cost", "", 14, new Color(0.45f, 0.35f, 0.2f),
                new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(110f, 22f), TextAlignmentOptions.Center);
            MakeText(slotGo.transform, "Key", (i + 1).ToString(), 13, new Color(0.45f, 0.35f, 0.2f),
                new Vector2(0f, 1f), new Vector2(10f, -6f), new Vector2(20f, 18f), TextAlignmentOptions.Left);

            SerializedProperty slot = slots.GetArrayElementAtIndex(i);
            slot.FindPropertyRelative("type").intValue = (int)type;
            slot.FindPropertyRelative("button").objectReferenceValue = button;
            slot.FindPropertyRelative("background").objectReferenceValue = bg;
            slot.FindPropertyRelative("icon").objectReferenceValue = icon;
            slot.FindPropertyRelative("nameLabel").objectReferenceValue = nameLabel;
            slot.FindPropertyRelative("costLabel").objectReferenceValue = costLabel;
        }
        Button remove = MakeButton(bar.transform, "RemoveButton", "제거", 22,
            new Vector2(1f, 0f), new Vector2(-24f, 76f), new Vector2(140f, 54f), panel, new Color(1f, 0.6f, 0.55f), dark);
        Button cancel = MakeButton(bar.transform, "CancelButton", "선택 취소", 20,
            new Vector2(1f, 0f), new Vector2(-24f, 14f), new Vector2(140f, 54f), panel, new Color(0.92f, 0.9f, 0.86f), dark);
        barSo.FindProperty("removeButton").objectReferenceValue = remove;
        barSo.FindProperty("cancelButton").objectReferenceValue = cancel;
        barSo.ApplyModifiedPropertiesWithoutUndo();

        // DEV toggle, always visible (top-right like the farm DEV button)
        var devGo = new GameObject("DevButton", typeof(RectTransform));
        devGo.transform.SetParent(root, false);
        SetRect((RectTransform)devGo.transform, new Vector2(1f, 1f), new Vector2(-20f, -20f), new Vector2(76f, 76f), new Vector2(1f, 1f));
        Image devImage = devGo.AddComponent<Image>();
        ApplySprite(devImage, circle, new Color(0.55f, 0.55f, 0.55f));
        Button devButton = devGo.AddComponent<Button>();
        devButton.targetGraphic = devImage;
        TMP_Text devLabel = MakeText(devGo.transform, "Label", "DEV", 18, Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(76f, 76f), TextAlignmentOptions.Center);
        EcoDevToggleButton devToggle = devGo.AddComponent<EcoDevToggleButton>();
        Set(devToggle, "background", devImage);
        Set(devToggle, "label", devLabel);
    }

    static GameObject Stretch(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return go;
    }

    static void SetRect(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    static GameObject MakePanel(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        SetRect((RectTransform)go.transform, anchor, position, size, anchor);
        ApplySprite(go.AddComponent<Image>(), sprite, color);
        return go;
    }

    static Button MakeButton(Transform parent, string name, string label, float fontSize, Vector2 anchor, Vector2 position,
        Vector2 size, Sprite sprite, Color color, Color textColor)
    {
        GameObject go = MakePanel(parent, name, anchor, position, size, sprite, color);
        Button button = go.AddComponent<Button>();
        button.targetGraphic = go.GetComponent<Image>();
        MakeText(go.transform, "Label", label, fontSize, textColor, new Vector2(0.5f, 0.5f), Vector2.zero, size, TextAlignmentOptions.Center);
        return button;
    }

    static TMP_Text MakeText(Transform parent, string name, string text, float size, Color color, Vector2 anchor,
        Vector2 position, Vector2 box, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        SetRect((RectTransform)go.transform, anchor, position, box, anchor);
        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KoreanFontPath);
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = alignment;
        label.raycastTarget = false;
        return label;
    }

    static void ApplySprite(Image image, Sprite sprite, Color color)
    {
        image.sprite = sprite;
        image.color = color;
        if (sprite != null && sprite.border != Vector4.zero) image.type = Image.Type.Sliced;
    }

    static Sprite LoadSprite(string file)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiFolder + file);
        if (sprite == null) Debug.LogWarning($"UI 스프라이트를 찾지 못했습니다: {UiFolder + file}");
        return sprite;
    }

    // ------------------------------------------------------------------ Geometry / assets

    static GameObject Box(Transform parent, string name, Vector3 min, Vector3 max, Material material, bool keepCollider)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = (min + max) * 0.5f;
        go.transform.localScale = max - min;
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static Material Mat(string name, Color color, Color? emission = null)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/_Project/Materials", "SpiritGrid");
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetFloat("_Smoothness", 0.1f);
        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void Set(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        SerializedProperty property = so.FindProperty(field);
        if (property == null)
        {
            Debug.LogError($"{target.GetType().Name}.{field} 필드를 찾지 못했습니다.");
            return;
        }
        property.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetArray(Object target, string field, params Object[] values)
    {
        var so = new SerializedObject(target);
        SerializedProperty property = so.FindProperty(field);
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
