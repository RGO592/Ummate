using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Button = UnityEngine.UI.Button;

// J quest book, using shared ver.2 page sprites and scrollable pages.
// One per gameplay scene. It reads the single QuestLog on the
// FarmScene player, found across scenes because FarmScene stays loaded (suspended) during region visits.
// Left: active and completed quests. Right: the selected quest's status, description, objectives, rewards.
// Esc closes it before PauseMenu sees the press (PauseMenu runs last and only opens for an unconsumed Esc).
// While open, the scene's gameplay input list is disabled, the same way TradingPostUI and PauseMenu do it.
[DisallowMultipleComponent]
public sealed class QuestWindow : MonoBehaviour
{
    [Header("Appearance (UI ver.2 assets)")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Sprite panelSprite;
    [SerializeField] private Sprite buttonSprite;
    [SerializeField] private Sprite closeSprite;
    [SerializeField, Tooltip("Above TradingPostUI (200), below PauseMenu (300).")]
    private int sortingOrder = 250;
    [Header("Input lock (same list as PauseMenu)")]
    [SerializeField] private Behaviour[] inputComponents;
    [SerializeField] private Rigidbody playerBody;
    [Header("Never opens over these (optional per scene)")]
    [SerializeField] private PauseMenu pauseMenu;
    [SerializeField] private TradingPostUI tradingPost;
    [SerializeField] private SleepTransition sleepTransition;
    // Build mode is not listed: the window opens over it. SpiritGridScene's input list holds
    // SpiritBuildModeController and EcoPlacementController, so B, the build camera and Hex placement /
    // drag / removal pause while the window is open, and build mode itself stays on underneath.

    private static readonly Color TextColor = new Color(0.27f, 0.19f, 0.12f);
    private static readonly Color MutedColor = new Color(0.45f, 0.38f, 0.3f);
    private static readonly Color PanelFallback = new Color(0.98f, 0.95f, 0.88f);
    private static readonly Color SelectedColor = new Color(1f, 0.9f, 0.62f);
    private const string MutedHex = "#73614D";

    private QuestLog quests;
    private GameObject root;
    private RectTransform activeList, completedList;
    private TMP_Text activeEmpty, completedEmpty;
    private TMP_Text detailTitle, detailStatus, detailDescription, detailObjectives, detailRewards;
    private bool[] previouslyEnabled;
    private string selectedId;
    private ScrollRect detailScroll;

    public bool IsOpen { get; private set; }

    private QuestLog Quests
    {
        get
        {
            if (quests == null)
                foreach (QuestLog found in FindObjectsByType<QuestLog>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    quests = found;
                    break;
                }
            return quests;
        }
    }

    private static bool TogglePressed => Application.isFocused && Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame;

    private void Update()
    {
        if (IsOpen)
        {
            // Consumed so the same Esc press does not also open the pause menu.
            if (UIEscape.WasPressed)
            {
                UIEscape.Consume();
                Close();
            }
            else if (TogglePressed) Close();
            return;
        }
        if (TogglePressed) Open();
    }

    // Suspending or unloading the scene must never leave the player locked.
    private void OnDisable() => Close();

    private bool CanOpen() =>
        isActiveAndEnabled && Quests != null && !SceneTransitionService.IsTraveling &&
        (pauseMenu == null || !pauseMenu.IsOpen) && (tradingPost == null || !tradingPost.IsOpen) &&
        (sleepTransition == null || !sleepTransition.IsSleeping);

    public void Open()
    {
        if (IsOpen || !CanOpen()) return;
        EnsureBuilt();
        IsOpen = true;
        LockInput();
        Quests.Changed += Refresh;
        QuestLog.Entry main = Quests.CurrentMain;
        if (selectedId == null || Quests.GetStatus(selectedId) == QuestStatus.Locked)
            selectedId = main != null ? main.Definition.Id : null;
        root.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (quests != null) quests.Changed -= Refresh;
        if (root != null) root.SetActive(false);
        UnlockInput();
    }

    private void Select(string id)
    {
        selectedId = id;
        Refresh();
        detailScroll.StopMovement();
        detailScroll.verticalNormalizedPosition = 1f;
    }

    // ---- View ----

    private void Refresh()
    {
        if (!IsOpen || root == null || Quests == null) return;
        ClearChildren(activeList);
        ClearChildren(completedList);
        QuestLog.Entry selected = null;
        int activeCount = 0, completedCount = 0;
        foreach (QuestLog.Entry entry in Quests.Entries)
        {
            if (entry.Status == QuestStatus.Locked) continue;
            bool active = entry.Status == QuestStatus.Active;
            if (active) activeCount++;
            else completedCount++;
            if (selected == null && entry.Definition.Id == selectedId) selected = entry;
            string id = entry.Definition.Id;
            Button button = CreateButton($"Quest_{id}", active ? activeList : completedList,
                entry.Definition.Title, -1f, () => Select(id), TextAlignmentOptions.MidlineLeft);
            // No completion time is stored; the quest line completes in data order (Q01 → Q10), so the
            // newest completed quest is the latest in that order. Each one goes on top: newest first.
            if (!active) button.transform.SetAsFirstSibling();
            Image background = button.GetComponent<Image>();
            background.color = id == selectedId ? SelectedColor : active ? Color.white : new Color(0.87f, 0.84f, 0.78f);
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            label.text = (id == selectedId ? "> " : "") + entry.Definition.Title;
            label.color = active ? TextColor : MutedColor;
            // State badge stays separate from the title so long names remain readable.
            TMP_Text badge = CreateText("State", button.transform, active ? "진행" : "완료", 17f, TextAlignmentOptions.Center);
            badge.color = MutedColor;
            RectTransform badgeRect = (RectTransform)badge.transform;
            badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(1f, 0.5f);
            badgeRect.anchoredPosition = new Vector2(-34f, 0f);
            badgeRect.sizeDelta = new Vector2(52f, 32f);
            label.rectTransform.offsetMax = new Vector2(-64f, -4f);
        }
        activeEmpty.gameObject.SetActive(activeCount == 0);
        completedEmpty.gameObject.SetActive(completedCount == 0);
        ShowDetail(selected);
    }

    private void ShowDetail(QuestLog.Entry entry)
    {
        if (entry == null)
        {
            detailTitle.text = "퀘스트를 선택하세요";
            detailStatus.text = detailDescription.text = detailObjectives.text = detailRewards.text = "";
            return;
        }
        QuestDefinition quest = entry.Definition;
        detailTitle.text = quest.Title;
        detailStatus.text = entry.Status == QuestStatus.Completed ? "[완료]" : "[진행 중]";
        detailDescription.text = quest.Description;

        var objectives = new StringBuilder();
        for (int i = 0; i < entry.ObjectiveCount; i++)
        {
            string line = $"· {quest.Objectives[i].description}  {entry.GetProgress(i)} / {entry.GetRequired(i)}";
            objectives.AppendLine(entry.IsObjectiveDone(i) ? $"<color={MutedHex}>{line}  (완료)</color>" : line);
        }
        if (entry.ObjectiveCount == 0) objectives.AppendLine($"<color={MutedHex}>· 목표 준비 중 (조건 미정)</color>");
        detailObjectives.text = objectives.ToString().TrimEnd();
        detailRewards.text = DescribeRewards(quest);
    }

    private static string DescribeRewards(QuestDefinition quest)
    {
        var lines = new List<string>();
        foreach (QuestDefinition.ItemGrant grant in quest.StartItems)
            if (grant != null && grant.item != null) lines.Add($"· 시작 지급: {grant.item.DisplayName} {grant.quantity}개");
        foreach (QuestDefinition.ItemGrant grant in quest.RewardItems)
            if (grant != null && grant.item != null) lines.Add($"· {grant.item.DisplayName} {grant.quantity}개");
        if (quest.RewardGold > 0) lines.Add($"· {quest.RewardGold} G");
        foreach (string pending in quest.PendingRewards)
            if (!string.IsNullOrWhiteSpace(pending)) lines.Add($"<color={MutedHex}>· {pending}</color>");
        return lines.Count > 0 ? string.Join("\n", lines) : $"<color={MutedHex}>없음</color>";
    }

    private static void ClearChildren(RectTransform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            GameObject child = parent.GetChild(i).gameObject;
            child.SetActive(false); // Destroy is deferred; hide immediately so layout does not count it.
            Destroy(child);
        }
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
        // PlayerMovement is off now and no longer writes velocity: stop the walk it left behind.
        if (playerBody != null && !playerBody.isKinematic)
        {
            Vector3 velocity = playerBody.linearVelocity;
            playerBody.linearVelocity = new Vector3(0f, velocity.y, 0f);
        }
    }

    // Only components this window disabled are re-enabled.
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
        root = new GameObject("QuestWindowCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Full-screen dim also blocks clicks on the HUD (settings button, quickbar) underneath.
        RectTransform dim = CreateRect("Dim", root.transform);
        Stretch(dim);
        dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.28f);

        RectTransform panel = CreateRect("Panel", root.transform);
        panel.sizeDelta = new Vector2(1280f, 840f);
        ApplySprite(panel.gameObject.AddComponent<Image>(), panelSprite, PanelFallback);
        panel.GetComponent<Image>().color = new Color(0.78f, 0.64f, 0.46f);
        VerticalLayoutGroup vertical = AddVertical(panel.gameObject, 12f);
        vertical.padding = new RectOffset(24, 24, 24, 18);

        SetSize(CreateText("Title", panel, "퀘스트", 30f, TextAlignmentOptions.Center).gameObject, -1f, 48f);

        RectTransform body = CreateRect("Body", panel);
        HorizontalLayoutGroup columns = body.gameObject.AddComponent<HorizontalLayoutGroup>();
        columns.spacing = 10f;
        columns.childControlWidth = columns.childControlHeight = true;
        columns.childForceExpandWidth = false;
        columns.childForceExpandHeight = true;
        SetSize(body.gameObject, -1f, 640f, flexibleHeight: 1f);

        RectTransform leftPage = CreateRect("LeftPage", body);
        ApplySprite(leftPage.gameObject.AddComponent<Image>(), panelSprite, PanelFallback);
        SetSize(leftPage.gameObject, 490f, -1f);
        RectTransform left = CreatePageScroll(leftPage, "QuestList", out _);
        SetSize(CreateText("ActiveHeader", left, "<b>진행 중</b>", 24f, TextAlignmentOptions.MidlineLeft).gameObject, -1f, 34f);
        activeList = CreateRect("Active", left);
        AddVertical(activeList.gameObject, 6f);
        activeEmpty = CreateText("ActiveEmpty", left, "진행 중인 퀘스트가 없습니다.", 20f, TextAlignmentOptions.MidlineLeft);
        activeEmpty.color = MutedColor;
        SetSize(activeEmpty.gameObject, -1f, 30f);
        SetSize(CreateText("CompletedHeader", left, "<b>완료</b>", 24f, TextAlignmentOptions.MidlineLeft).gameObject, -1f, 40f);
        completedList = CreateRect("Completed", left);
        AddVertical(completedList.gameObject, 6f);
        completedEmpty = CreateText("CompletedEmpty", left, "완료한 퀘스트가 없습니다.", 20f, TextAlignmentOptions.MidlineLeft);
        completedEmpty.color = MutedColor;
        SetSize(completedEmpty.gameObject, -1f, 30f);

        RectTransform divider = CreateRect("Divider", body);
        Image line = divider.gameObject.AddComponent<Image>();
        line.color = new Color(0.76f, 0.64f, 0.47f, 0.4f);
        line.raycastTarget = false;
        SetSize(divider.gameObject, 12f, -1f);
        line.color = new Color(0.30f, 0.20f, 0.12f, 0.22f);
        RectTransform fold = CreateRect("FoldHighlight", divider);
        Stretch(fold);
        fold.offsetMin = new Vector2(7f, 0f);
        Image foldImage = fold.gameObject.AddComponent<Image>();
        foldImage.color = new Color(1f, 0.94f, 0.79f, 0.45f);
        foldImage.raycastTarget = false;

        RectTransform rightPage = CreateRect("RightPage", body);
        ApplySprite(rightPage.gameObject.AddComponent<Image>(), panelSprite, PanelFallback);
        SetSize(rightPage.gameObject, -1f, -1f, flexibleWidth: 1f);
        RectTransform right = CreatePageScroll(rightPage, "Detail", out detailScroll);
        detailTitle = CreateText("Title", right, "", 30f, TextAlignmentOptions.MidlineLeft, wrap: true);
        detailStatus = CreateText("Status", right, "", 22f, TextAlignmentOptions.MidlineLeft);
        detailStatus.color = MutedColor;
        RectTransform frame = CreateRect("QuestImageFrame", right);
        ApplySprite(frame.gameObject.AddComponent<Image>(), buttonSprite, PanelFallback);
        frame.GetComponent<Image>().color = new Color(0.91f, 0.87f, 0.77f);
        SetSize(frame.gameObject, -1f, 132f);
        RectTransform imageSlot = CreateRect("QuestImage", frame);
        Stretch(imageSlot);
        imageSlot.offsetMin = new Vector2(10f, 10f);
        imageSlot.offsetMax = new Vector2(-10f, -10f);
        Image illustration = imageSlot.gameObject.AddComponent<Image>();
        illustration.preserveAspect = true;
        illustration.raycastTarget = false;
        illustration.enabled = false; // UI slot only: QuestDefinition has no artwork field.
        TMP_Text placeholder = CreateText("Placeholder", frame, "퀘스트 기록", 20f, TextAlignmentOptions.Center);
        placeholder.color = MutedColor;
        Stretch(placeholder.rectTransform);
        detailDescription = CreateText("Description", right, "", 22f, TextAlignmentOptions.TopLeft, wrap: true);
        SetSize(CreateText("ObjectivesHeader", right, "<b>목표</b>", 24f, TextAlignmentOptions.MidlineLeft).gameObject, -1f, 40f);
        detailObjectives = CreateText("Objectives", right, "", 22f, TextAlignmentOptions.TopLeft, wrap: true);
        SetSize(CreateText("RewardsHeader", right, "<b>보상</b>", 24f, TextAlignmentOptions.MidlineLeft).gameObject, -1f, 40f);
        detailRewards = CreateText("Rewards", right, "", 22f, TextAlignmentOptions.TopLeft, wrap: true);

        RectTransform footer = CreateRect("Footer", panel);
        HorizontalLayoutGroup footerLayout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
        footerLayout.spacing = 16f;
        footerLayout.childAlignment = TextAnchor.MiddleRight;
        footerLayout.childControlWidth = footerLayout.childControlHeight = true;
        footerLayout.childForceExpandWidth = footerLayout.childForceExpandHeight = false;
        SetSize(footer.gameObject, -1f, 56f);
        TMP_Text hint = CreateText("Hint", footer, "J / ESC: 닫기", 18f, TextAlignmentOptions.MidlineRight);
        hint.color = MutedColor;
        SetSize(hint.gameObject, 200f, 48f);
        CreateButton("Close", footer, "닫기", 160f, Close, TextAlignmentOptions.Center);

        // X button sits outside the layout, pinned to the panel's top-right corner.
        Button closeX = CreateButton("CloseX", panel, closeSprite != null ? "" : "X", 52f, Close, TextAlignmentOptions.Center);
        closeX.GetComponent<LayoutElement>().ignoreLayout = true;
        RectTransform closeRect = (RectTransform)closeX.transform;
        closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = Vector2.one;
        closeRect.anchoredPosition = new Vector2(-18f, -18f);
        closeRect.sizeDelta = new Vector2(44f, 44f);
        if (closeSprite != null) ApplySprite(closeX.GetComponent<Image>(), closeSprite, Color.white);

        root.SetActive(false);
    }

    // Viewport owns masking; only its Content sizes itself, avoiding layout/fitter feedback.
    private RectTransform CreatePageScroll(RectTransform page, string name, out ScrollRect scroll)
    {
        RectTransform scrollRoot = CreateRect(name + "Scroll", page);
        Stretch(scrollRoot);
        scrollRoot.offsetMin = new Vector2(24f, 20f);
        scrollRoot.offsetMax = new Vector2(-24f, -20f);
        scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
        RectTransform viewport = CreateRect("Viewport", scrollRoot);
        Stretch(viewport);
        Image hitArea = viewport.gameObject.AddComponent<Image>();
        hitArea.color = Color.white;
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        RectTransform content = CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup layout = AddVertical(content.gameObject, 10f);
        layout.padding = new RectOffset(2, 2, 2, 12);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 32f;
        scroll.inertia = true;
        return content;
    }

    // ---- Small UI helpers (same conventions as TradingPostUI) ----

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

    private static void SetSize(GameObject go, float width, float height, float flexibleWidth = -1f, float flexibleHeight = -1f)
    {
        LayoutElement element = go.GetComponent<LayoutElement>();
        if (element == null) element = go.AddComponent<LayoutElement>();
        if (width >= 0f) { element.preferredWidth = width; element.minWidth = width; }
        if (height >= 0f) { element.preferredHeight = height; element.minHeight = height; }
        element.flexibleWidth = flexibleWidth;
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
        if (!wrap) label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }

    private Button CreateButton(string objectName, Transform parent, string text, float width, UnityAction onClick,
        TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateRect(objectName, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        ApplySprite(image, buttonSprite, new Color(0.9f, 0.84f, 0.72f));
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None }; // WASD/arrows must not move UI focus.
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1f, 0.97f, 0.88f);
        colors.pressedColor = new Color(0.84f, 0.74f, 0.57f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
        button.onClick.AddListener(onClick);
        SetSize(rect.gameObject, width, alignment == TextAlignmentOptions.MidlineLeft ? 62f : 46f);
        if (!string.IsNullOrEmpty(text))
        {
            TMP_Text label = CreateText("Label", rect, text, 21f, alignment, wrap: alignment == TextAlignmentOptions.MidlineLeft);
            RectTransform labelRect = (RectTransform)label.transform;
            Stretch(labelRect);
            if (alignment == TextAlignmentOptions.MidlineLeft)
            {
                labelRect.offsetMin = new Vector2(18f, 4f);
                labelRect.offsetMax = new Vector2(-18f, 0f);
            }
        }
        return button;
    }
}
