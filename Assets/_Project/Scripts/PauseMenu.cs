using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Button = UnityEngine.UI.Button;

// Player pause / settings menu: home → 옵션 · 저장 · 불러오기 screens, plus 메인메뉴로 나가기.
// One instance per gameplay scene; the existing top-right settings button and Esc open the same menu.
// 저장 lists the auto slot (read-only) and manual slots 1–3; 불러오기 lists all four.
// Save / load go through the existing GameSaveSystem, found across scenes because FarmScene stays
// loaded (suspended) while regions are visited. Input is locked with the scene's own list, the same
// way SleepTransition and TradingPostUI do it, and game time is paused with Time.timeScale.
// Shared ver.2 presentation, built from each scene's existing sprite references.
// Runs after every other Update so a closer UI (TradingPostUI, a build drag) can consume Esc first.
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class PauseMenu : MonoBehaviour
{
    private enum Tab { Home, Options, Save, Load }

    [Header("Appearance (UI ver.2 assets)")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Sprite panelSprite;
    [SerializeField] private Sprite buttonSprite;
    [SerializeField] private Sprite closeSprite;
    [SerializeField, Tooltip("Above the HUD, PlayerFeedbackUI (100) and TradingPostUI (200).")]
    private int sortingOrder = 300;
    [Header("Open")]
    [SerializeField, Tooltip("The existing top-right HUD settings button.")]
    private Button settingsButton;
    [Header("Input lock (gameplay input of this scene)")]
    [SerializeField] private Behaviour[] inputComponents;
    [Header("Main menu")]
    [SerializeField] private string mainMenuScene = "Assets/_Project/Scenes/MainMenuScene.unity";

    private static readonly Color TextColor = new Color(0.27f, 0.19f, 0.12f);
    private static readonly Color MutedColor = new Color(0.45f, 0.38f, 0.3f);
    private static readonly Color PanelFallback = new Color(0.98f, 0.95f, 0.88f);
    private static readonly Color IdleTabColor = new Color(0.86f, 0.82f, 0.75f);

    private GameObject root;
    private readonly Dictionary<Tab, GameObject> pages = new Dictionary<Tab, GameObject>();
    private RectTransform menuPanel, pageArea;
    private GameObject detailFooter, footerDivider;
    private TMP_Text titleText;
    private sealed class SlotRow
    {
        public TMP_Text info;
        public Button button;
    }

    private TMP_Text messageText, saveHeaderText, confirmText, confirmOkLabel;
    private TMP_Dropdown resolutionDropdown, modeDropdown;
    private readonly SlotRow[] saveRows = new SlotRow[GameSaveSystem.SlotCount];
    private readonly SlotRow[] loadRows = new SlotRow[GameSaveSystem.SlotCount];
    private GameObject confirmRoot;
    private Action pendingConfirm;
    private List<Vector2Int> resolutions = new List<Vector2Int>();
    private GameSaveSystem saveSystem;
    private bool[] previouslyEnabled;
    private float previousTimeScale = 1f;
    private Tab currentTab;

    public bool IsOpen { get; private set; }

    // FarmScene's save system, also reachable while a region scene is active.
    private GameSaveSystem SaveSystem
    {
        get
        {
            if (saveSystem == null)
                foreach (GameSaveSystem found in FindObjectsByType<GameSaveSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    saveSystem = found;
                    break;
                }
            return saveSystem;
        }
    }

    private void OnEnable()
    {
        if (settingsButton != null) settingsButton.onClick.AddListener(Open);
    }

    // Suspending or unloading the scene must never leave the game paused or locked.
    private void OnDisable()
    {
        if (settingsButton != null) settingsButton.onClick.RemoveListener(Open);
        Close();
    }

    private void Update()
    {
        if (!UIEscape.WasPressed) return;
        if (IsOpen)
        {
            // Confirm → dropdown → detail screen → pause home → game.
            UIEscape.Consume();
            if (confirmRoot != null && confirmRoot.activeSelf) CloseConfirm();
            else if (!HideExpandedDropdown())
            {
                if (currentTab == Tab.Home) Close();
                else ShowTab(Tab.Home);
            }
            return;
        }
        if (CanOpen())
        {
            UIEscape.Consume();
            Open();
        }
    }

    private bool CanOpen()
    {
        if (!isActiveAndEnabled || SceneTransitionService.IsTraveling) return false;
        GameSaveSystem system = SaveSystem;
        return system == null || !system.IsSleeping;
    }

    public void Open()
    {
        if (IsOpen || !CanOpen()) return;
        EnsureBuilt();
        IsOpen = true;
        LockInput();
        previousTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        root.SetActive(true);
        SetMessage("");
        ShowTab(Tab.Home);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        CloseConfirm();
        HideExpandedDropdown();
        if (root != null) root.SetActive(false);
        Time.timeScale = previousTimeScale;
        UnlockInput();
    }

    // ---- Screen navigation (save/load operations remain unchanged) ----

    private void ShowTab(Tab tab)
    {
        HideExpandedDropdown();
        currentTab = tab;
        foreach (KeyValuePair<Tab, GameObject> page in pages) page.Value.SetActive(page.Key == tab);
        bool home = tab == Tab.Home;
        bool slots = tab == Tab.Save || tab == Tab.Load;
        titleText.text = home ? "일시정지" : tab == Tab.Options ? "옵션" : tab == Tab.Save ? "저장" : "불러오기";
        menuPanel.sizeDelta = home ? new Vector2(600f, 640f)
            : slots ? new Vector2(900f, 780f) : new Vector2(840f, 620f);
        SetSize(pageArea.gameObject, -1f, home ? 360f : slots ? 464f : 256f, flexibleHeight: 1f);
        detailFooter.SetActive(!home);
        footerDivider.SetActive(!home);
        SetMessage("");
        if (tab == Tab.Options) RefreshOptions();
        else if (slots) RefreshSlots();
    }

    private void RefreshOptions()
    {
        resolutions = DisplaySettings.AvailableResolutions();
        resolutionDropdown.ClearOptions();
        var labels = new List<string>();
        foreach (Vector2Int size in resolutions) labels.Add(DisplaySettings.Describe(size));
        resolutionDropdown.AddOptions(labels);
        resolutionDropdown.SetValueWithoutNotify(Mathf.Max(0, resolutions.IndexOf(DisplaySettings.CurrentResolution)));
        resolutionDropdown.RefreshShownValue();
        modeDropdown.SetValueWithoutNotify(DisplaySettings.CurrentModeIndex);
        modeDropdown.RefreshShownValue();
    }

    private void ApplyDisplay()
    {
        if (resolutions.Count == 0) return;
        Vector2Int size = resolutions[Mathf.Clamp(resolutionDropdown.value, 0, resolutions.Count - 1)];
        DisplaySettings.Apply(size, modeDropdown.value);
        SetMessage($"적용했습니다: {DisplaySettings.Describe(size)} · {DisplaySettings.ModeNames[modeDropdown.value]}");
    }

    private void RefreshSlots()
    {
        GameSaveSystem system = SaveSystem;
        saveHeaderText.text = system != null ? $"현재 진행: {system.CurrentDay}일차" : "저장 시스템을 찾을 수 없습니다. 숲 마을에서 시작해 주세요.";
        for (int slot = 0; slot < GameSaveSystem.SlotCount; slot++)
        {
            bool readable = GameSaveSystem.TryGetSlotSummary(slot, out GameSaveSystem.SlotSummary summary);
            string info = DescribeSlot(summary);
            saveRows[slot].info.text = info;
            loadRows[slot].info.text = info;
            // Auto is shown but only sleeping writes it; empty or unreadable slots cannot be loaded.
            saveRows[slot].button.interactable = system != null && slot != GameSaveSystem.AutoSlot;
            loadRows[slot].button.interactable = system != null && readable;
        }
    }

    // Shared by the save and load screens: game day · region, then the real-world save time.
    private static string DescribeSlot(GameSaveSystem.SlotSummary summary) =>
        summary.IsReadable ? $"{summary.Day}일차 · {summary.LocationName}\n<size=18><color=#73614D>{summary.SavedAt:yyyy-MM-dd HH:mm}</color></size>"
        : summary.Exists ? "<color=#73614D>읽을 수 없는 저장 데이터</color>"
        : "<color=#73614D>빈 슬롯</color>";

    // ---- Save / load / main menu ----

    private void OnSaveSlotClicked(int slot)
    {
        if (SaveSystem == null || slot == GameSaveSystem.AutoSlot) return;
        GameSaveSystem.TryGetSlotSummary(slot, out GameSaveSystem.SlotSummary summary);
        if (summary.Exists) ShowConfirm("이 슬롯의 저장 데이터를 덮어쓰시겠습니까?", "덮어쓰기", () => SaveNow(slot));
        else SaveNow(slot);
    }

    private void SaveNow(int slot)
    {
        GameSaveSystem system = SaveSystem;
        if (system == null) return;
        bool saved = system.TrySaveSlot(slot);
        RefreshSlots();
        SetMessage(saved ? $"{GameSaveSystem.GetSlotName(slot)}에 저장했습니다." : system.LastMessage);
    }

    private void OnLoadSlotClicked(int slot)
    {
        if (SaveSystem == null || !GameSaveSystem.TryGetSlotSummary(slot, out _)) return;
        ShowConfirm("이 저장 데이터를 불러오시겠습니까?\n현재 저장하지 않은 진행 상황은 사라집니다.", "불러오기", () => LoadNow(slot));
    }

    private void LoadNow(int slot)
    {
        GameSaveSystem system = SaveSystem;
        if (system == null) return;
        if (!system.TryLoadSlot(slot))
        {
            RefreshSlots();
            SetMessage(system.LastMessage);
            return;
        }
        Close();
        PlayerFeedback.Notify($"{GameSaveSystem.GetSlotName(slot)} · {system.CurrentDay}일차 저장 데이터를 불러왔습니다.");
    }

    private void OnMainMenuClicked() =>
        ShowConfirm("메인메뉴로 나가시겠습니까?\n저장하지 않은 진행 상황은 사라질 수 있습니다.", "나가기", ExitToMainMenu);

    private void ExitToMainMenu()
    {
        if (string.IsNullOrWhiteSpace(mainMenuScene) || !Application.CanStreamedLevelBeLoaded(mainMenuScene))
        {
            SetMessage("메인메뉴 Scene을 찾을 수 없습니다. Build Settings를 확인해 주세요.");
            return;
        }
        Close(); // Restores time scale before every gameplay scene is unloaded.
        SceneManager.LoadScene(mainMenuScene, LoadSceneMode.Single);
    }

    // ---- Confirm dialog (minimal; drawn above the menu) ----

    private void ShowConfirm(string question, string confirmLabel, Action onConfirm)
    {
        HideExpandedDropdown();
        pendingConfirm = onConfirm;
        confirmText.text = question;
        confirmOkLabel.text = confirmLabel;
        confirmRoot.SetActive(true);
    }

    private void Confirm()
    {
        Action action = pendingConfirm;
        CloseConfirm();
        action?.Invoke();
    }

    private void CloseConfirm()
    {
        pendingConfirm = null;
        if (confirmRoot != null) confirmRoot.SetActive(false);
    }

    private bool HideExpandedDropdown()
    {
        bool hidden = false;
        foreach (TMP_Dropdown dropdown in new[] { resolutionDropdown, modeDropdown })
            if (dropdown != null && dropdown.IsExpanded) { dropdown.Hide(); hidden = true; }
        return hidden;
    }

    private void SetMessage(string text)
    {
        if (messageText != null) messageText.text = text;
    }

    // ---- Input lock ----

    private void LockInput()
    {
        int count = inputComponents != null ? inputComponents.Length : 0;
        previouslyEnabled = new bool[count];
        for (int i = 0; i < count; i++)
        {
            Behaviour input = inputComponents[i];
            if (input == null || input == this) continue;
            previouslyEnabled[i] = input.enabled;
            input.enabled = false;
        }
    }

    // Only components this menu disabled are re-enabled (build mode keeps its own choices).
    private void UnlockInput()
    {
        if (previouslyEnabled == null) return;
        for (int i = 0; i < previouslyEnabled.Length; i++)
            if (inputComponents[i] != null && inputComponents[i] != this && previouslyEnabled[i])
                inputComponents[i].enabled = true;
        previouslyEnabled = null;
    }

    // ---- Construction ----

    private void EnsureBuilt()
    {
        if (root != null) return;
        root = new GameObject("PauseMenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Full-screen dim also blocks clicks on every HUD and build UI underneath.
        RectTransform dim = CreateRect("Dim", root.transform);
        Stretch(dim);
        dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.38f);

        RectTransform panel = menuPanel = CreateRect("Panel", root.transform);
        panel.sizeDelta = new Vector2(840f, 760f);
        ApplySprite(panel.gameObject.AddComponent<Image>(), panelSprite, PanelFallback);
        VerticalLayoutGroup vertical = AddVertical(panel.gameObject, 12f);
        vertical.padding = new RectOffset(44, 44, 32, 32);

        titleText = CreateText("Title", panel, "일시정지", 32f, TextAlignmentOptions.Center);
        SetSize(titleText.gameObject, -1f, 48f);

        AddDivider(panel, "HeaderDivider");

        pageArea = CreateRect("Pages", panel);
        AddVertical(pageArea.gameObject, 0f).childForceExpandHeight = true;
        SetSize(pageArea.gameObject, -1f, 360f, flexibleHeight: 1f);
        pages[Tab.Home] = BuildHomePage(pageArea);
        pages[Tab.Options] = BuildOptionsPage(pageArea);
        pages[Tab.Save] = BuildSavePage(pageArea);
        pages[Tab.Load] = BuildLoadPage(pageArea);

        messageText = CreateText("Message", panel, "", 20f, TextAlignmentOptions.Center, wrap: true);
        SetSize(messageText.gameObject, -1f, 48f);

        AddDivider(panel, "FooterDivider");
        footerDivider = panel.Find("FooterDivider").gameObject;

        RectTransform footer = CreateRect("Footer", panel);
        AddHorizontal(footer.gameObject, 16f, TextAnchor.MiddleRight);
        SetSize(footer.gameObject, -1f, 56f);
        detailFooter = footer.gameObject;
        CreateButton("Back", footer, "뒤로가기", 180f, () => ShowTab(Tab.Home));

        Button closeX = CreateButton("CloseX", panel, closeSprite != null ? "" : "X", 52f, Close);
        closeX.GetComponent<LayoutElement>().ignoreLayout = true;
        RectTransform closeRect = (RectTransform)closeX.transform;
        closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = Vector2.one;
        closeRect.anchoredPosition = new Vector2(-18f, -18f);
        closeRect.sizeDelta = new Vector2(40f, 40f);
        if (closeSprite != null) ApplySprite(closeX.GetComponent<Image>(), closeSprite, Color.white);

        BuildConfirm();
        root.SetActive(false);
    }

    private GameObject BuildHomePage(Transform parent)
    {
        RectTransform page = CreatePage("HomePage", parent);
        VerticalLayoutGroup layout = page.GetComponent<VerticalLayoutGroup>();
        layout.childForceExpandWidth = false;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 14f;
        CreateButton("Resume", page, "계속하기", 360f, Close);
        CreateButton("Options", page, "옵션", 360f, () => ShowTab(Tab.Options));
        CreateButton("Save", page, "저장", 360f, () => ShowTab(Tab.Save));
        CreateButton("Load", page, "불러오기", 360f, () => ShowTab(Tab.Load));
        Button mainMenu = CreateButton("MainMenu", page, "메인메뉴로 나가기", 360f, OnMainMenuClicked);
        mainMenu.GetComponent<Image>().color = IdleTabColor;
        mainMenu.GetComponentInChildren<TMP_Text>().fontSize = 20f;
        return page.gameObject;
    }

    private GameObject BuildOptionsPage(Transform parent)
    {
        RectTransform page = CreatePage("OptionsPage", parent);
        resolutionDropdown = AddDropdownRow(page, "해상도");
        modeDropdown = AddDropdownRow(page, "화면 모드");
        modeDropdown.AddOptions(new List<string>(DisplaySettings.ModeNames));
        TMP_Text note = CreateText("Note", page, "화면 비율은 선택한 해상도를 따릅니다.", 18f, TextAlignmentOptions.Center);
        note.color = MutedColor;
        SetSize(note.gameObject, -1f, 28f);
        RectTransform row = CreateRect("ApplyRow", page);
        AddHorizontal(row.gameObject, 0f, TextAnchor.MiddleCenter);
        SetSize(row.gameObject, -1f, 56f);
        CreateButton("Apply", row, "적용", 200f, ApplyDisplay);
        return page.gameObject;
    }

    private GameObject BuildSavePage(Transform parent)
    {
        RectTransform page = CreatePage("SavePage", parent);
        saveHeaderText = CreateText("Current", page, "", 24f, TextAlignmentOptions.Center, wrap: true);
        SetSize(saveHeaderText.gameObject, -1f, 36f);
        for (int slot = 0; slot < GameSaveSystem.SlotCount; slot++)
        {
            int target = slot;
            string action = slot == GameSaveSystem.AutoSlot ? "자동 저장 전용" : "저장";
            saveRows[slot] = AddSlotRow(page, slot, action, () => OnSaveSlotClicked(target));
        }
        TMP_Text note = CreateText("Note", page, "자동 저장은 직접 덮어쓸 수 없습니다.", 18f, TextAlignmentOptions.Center);
        note.color = MutedColor;
        SetSize(note.gameObject, -1f, 28f);
        return page.gameObject;
    }

    private GameObject BuildLoadPage(Transform parent)
    {
        RectTransform page = CreatePage("LoadPage", parent);
        TMP_Text note = CreateText("Note", page, "불러올 저장 데이터를 선택하세요.", 22f, TextAlignmentOptions.Center);
        note.color = MutedColor;
        SetSize(note.gameObject, -1f, 36f);
        for (int slot = 0; slot < GameSaveSystem.SlotCount; slot++)
        {
            int target = slot;
            loadRows[slot] = AddSlotRow(page, slot, "불러오기", () => OnLoadSlotClicked(target));
        }
        return page.gameObject;
    }

    // Four fixed-height cards: slot name | progress and timestamp | existing action.
    private SlotRow AddSlotRow(Transform parent, int slot, string actionLabel, UnityAction onClick)
    {
        RectTransform row = CreateRect($"Slot_{(GameSaveSystem.SlotKind)slot}", parent);
        Image background = row.gameObject.AddComponent<Image>();
        ApplySprite(background, panelSprite, PanelFallback);
        if (slot == GameSaveSystem.AutoSlot) background.color = new Color(0.93f, 0.91f, 0.86f);
        AddHorizontal(row.gameObject, 12f, TextAnchor.MiddleLeft);
        row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(16, 16, 12, 12);
        SetSize(row.gameObject, -1f, 80f);
        TMP_Text caption = CreateText("Name", row, GameSaveSystem.GetSlotName(slot), 23f, TextAlignmentOptions.MidlineLeft);
        SetSize(caption.gameObject, 154f, 52f);
        TMP_Text info = CreateText("Info", row, "", 22f, TextAlignmentOptions.MidlineLeft, wrap: true);
        SetSize(info.gameObject, 280f, 56f);
        info.GetComponent<LayoutElement>().flexibleWidth = 1f;
        Button button = CreateButton("Action", row, actionLabel, 148f, onClick);
        if (slot == GameSaveSystem.AutoSlot && actionLabel != "불러오기")
        {
            // Keep the existing disabled Button reference used by RefreshSlots,
            // but present it as a read-only label, not a save action.
            button.GetComponent<Image>().color = Color.clear;
            button.GetComponentInChildren<TMP_Text>().color = MutedColor;
            button.GetComponentInChildren<TMP_Text>().fontSize = 18f;
            button.interactable = false;
        }
        return new SlotRow { info = info, button = button };
    }

    private void BuildConfirm()
    {
        RectTransform overlay = CreateRect("Confirm", root.transform);
        Stretch(overlay);
        overlay.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
        RectTransform box = CreateRect("Box", overlay);
        box.sizeDelta = new Vector2(660f, 336f);
        ApplySprite(box.gameObject.AddComponent<Image>(), panelSprite, PanelFallback);
        VerticalLayoutGroup vertical = AddVertical(box.gameObject, 16f);
        vertical.padding = new RectOffset(40, 40, 40, 32);
        SetSize(CreateText("Title", box, "확인", 28f, TextAlignmentOptions.Center).gameObject, -1f, 36f);
        confirmText = CreateText("Question", box, "", 24f, TextAlignmentOptions.Center, wrap: true);
        SetSize(confirmText.gameObject, -1f, 132f, flexibleHeight: 1f);
        RectTransform buttons = CreateRect("Buttons", box);
        AddHorizontal(buttons.gameObject, 20f, TextAnchor.MiddleCenter);
        SetSize(buttons.gameObject, -1f, 56f);
        confirmOkLabel = CreateButton("Ok", buttons, "확인", 180f, Confirm).GetComponentInChildren<TMP_Text>();
        CreateButton("Cancel", buttons, "취소", 180f, CloseConfirm);
        confirmRoot = overlay.gameObject;
        confirmRoot.SetActive(false);
    }

    private RectTransform CreatePage(string objectName, Transform parent)
    {
        RectTransform page = CreateRect(objectName, parent);
        VerticalLayoutGroup vertical = AddVertical(page.gameObject, 12f);
        vertical.padding = new RectOffset(20, 20, 12, 0);
        vertical.childAlignment = TextAnchor.UpperCenter;
        return page;
    }

    private TMP_Dropdown AddDropdownRow(Transform parent, string label)
    {
        RectTransform row = CreateRect($"Row_{label}", parent);
        AddHorizontal(row.gameObject, 20f, TextAnchor.MiddleCenter);
        SetSize(row.gameObject, -1f, 52f);
        TMP_Text caption = CreateText("Label", row, label, 24f, TextAlignmentOptions.MidlineRight);
        SetSize(caption.gameObject, 160f, 48f);

        // Standard TMP dropdown; its list fades in unscaled time, so it works while the game is paused.
        GameObject go = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
        go.name = "Dropdown";
        go.transform.SetParent(row, false);
        SetSize(go, 400f, 48f);
        TMP_Dropdown dropdown = go.GetComponent<TMP_Dropdown>();
        dropdown.navigation = new Navigation { mode = Navigation.Mode.None }; // WASD/arrows must not move focus.
        dropdown.ClearOptions();
        foreach (TMP_Text text in go.GetComponentsInChildren<TMP_Text>(true))
        {
            if (font != null) text.font = font;
            text.fontSize = 22f;
            text.color = TextColor;
        }
        ApplySprite(go.GetComponent<Image>(), buttonSprite, PanelFallback);
        dropdown.template.sizeDelta = new Vector2(dropdown.template.sizeDelta.x, 280f);
        RectTransform item = (RectTransform)dropdown.itemText.transform.parent;
        item.sizeDelta = new Vector2(item.sizeDelta.x, 44f);
        // TMP measures its cloned list from both the item and content rects.
        dropdown.template.GetComponent<ScrollRect>().content.sizeDelta = new Vector2(0f, 52f);
        ApplySprite(dropdown.template.GetComponent<Image>(), panelSprite, PanelFallback);
        dropdown.template.anchoredPosition = new Vector2(0f, -4f);
        dropdown.captionText.rectTransform.offsetMin = new Vector2(20f, 6f);
        dropdown.captionText.rectTransform.offsetMax = new Vector2(-44f, -6f);
        dropdown.itemText.rectTransform.offsetMin = new Vector2(26f, 2f);
        dropdown.itemText.rectTransform.offsetMax = new Vector2(-12f, -2f);
        Toggle toggle = item.GetComponent<Toggle>();
        toggle.targetGraphic.color = new Color(0.97f, 0.93f, 0.86f);
        // A narrow brown selection marker replaces the missing check sprite.
        Image selection = (Image)toggle.graphic;
        selection.color = TextColor;
        selection.raycastTarget = false;
        selection.rectTransform.sizeDelta = new Vector2(4f, 22f);
        selection.rectTransform.anchoredPosition = new Vector2(12f, 0f);
        StyleSelectable(toggle);
        RectTransform arrow = (RectTransform)go.transform.Find("Arrow");
        arrow.GetComponent<Image>().enabled = false;
        arrow.anchoredPosition = new Vector2(-24f, 0f);
        AddChevronStroke(arrow, -4f, -45f);
        AddChevronStroke(arrow, 4f, 45f);
        Scrollbar scrollbar = dropdown.template.GetComponent<ScrollRect>().verticalScrollbar;
        scrollbar.GetComponent<Image>().color = new Color(0.87f, 0.82f, 0.73f);
        scrollbar.handleRect.GetComponent<Image>().color = new Color(0.64f, 0.52f, 0.38f);
        ((RectTransform)scrollbar.transform).sizeDelta = new Vector2(12f, 0f);
        StyleSelectable(scrollbar);
        StyleSelectable(dropdown);
        return dropdown;
    }

    // ---- Small UI helpers (same conventions as TradingPostUI) ----

    private static void StyleSelectable(Selectable selectable)
    {
        ColorBlock colors = selectable.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.96f, 0.87f);
        colors.pressedColor = new Color(0.82f, 0.72f, 0.56f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.61f, 0.58f, 0.53f, 0.65f);
        colors.fadeDuration = 0.08f;
        selectable.colors = colors;
    }

    private static void AddDivider(Transform parent, string name)
    {
        RectTransform rect = CreateRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.76f, 0.64f, 0.47f, 0.4f);
        image.raycastTarget = false;
        SetSize(rect.gameObject, -1f, 2f);
    }

    private static void AddChevronStroke(Transform parent, float x, float angle)
    {
        RectTransform rect = CreateRect("ChevronStroke", parent);
        rect.sizeDelta = new Vector2(12f, 3f);
        rect.anchoredPosition = new Vector2(x, 0f);
        rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = TextColor;
        image.raycastTarget = false;
    }

    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static VerticalLayoutGroup AddVertical(GameObject go, float spacing)
    {
        VerticalLayoutGroup layout = go.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return layout;
    }

    private static void AddHorizontal(GameObject go, float spacing, TextAnchor alignment)
    {
        HorizontalLayoutGroup layout = go.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = alignment;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
    }

    private static void SetSize(GameObject go, float width, float height, float flexibleHeight = -1f)
    {
        LayoutElement element = go.GetComponent<LayoutElement>();
        if (element == null) element = go.AddComponent<LayoutElement>();
        if (width >= 0f) { element.preferredWidth = width; element.minWidth = width; }
        if (height >= 0f) { element.preferredHeight = height; element.minHeight = height; }
        element.flexibleHeight = flexibleHeight;
    }

    private static void ApplySprite(Image image, Sprite sprite, Color fallback)
    {
        image.sprite = sprite;
        image.color = sprite != null ? Color.white : fallback;
        image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        if (image.type == Image.Type.Sliced) image.pixelsPerUnitMultiplier = 1f;
    }

    private TMP_Text CreateText(string objectName, Transform parent, string text, float size,
        TextAlignmentOptions alignment, bool wrap = false)
    {
        TextMeshProUGUI label = CreateRect(objectName, parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = size;
        label.color = TextColor;
        label.alignment = alignment;
        label.raycastTarget = false;
        label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        return label;
    }

    private Button CreateButton(string objectName, Transform parent, string text, float width, UnityAction onClick)
    {
        RectTransform rect = CreateRect(objectName, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        ApplySprite(image, buttonSprite, new Color(0.9f, 0.84f, 0.72f));
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        StyleSelectable(button);
        button.onClick.AddListener(onClick);
        SetSize(rect.gameObject, width, 52f);
        if (!string.IsNullOrEmpty(text))
        {
            TMP_Text label = CreateText("Label", rect, text, 22f, TextAlignmentOptions.Center);
            Stretch((RectTransform)label.transform);
        }
        return button;
    }
}
