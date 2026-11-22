using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// K habitat journal (function-check UI; art is replaced later). Left: every habitat of HabitatCatalog with its
// discovery state. Right: the selected habitat's name, state, a shape preview built from its pattern offsets
// (blank "?" Hex slots in exactly the judged arrangement, no environment icons) and the environments it needs as
// text, generated from the same definition. Created at runtime by QuestWindow (one per gameplay scene) and locks
// that scene's gameplay input while open, like the quest window.
[DisallowMultipleComponent]
public sealed class HabitatJournalUI : MonoBehaviour
{
    private static readonly Color TextColor = new Color(0.27f, 0.19f, 0.12f);
    private static readonly Color MutedColor = new Color(0.45f, 0.38f, 0.3f);
    private static readonly Color FoundColor = new Color(0.25f, 0.5f, 0.22f);
    private static readonly Color PanelFallback = new Color(0.98f, 0.95f, 0.88f);
    private static readonly Color SlotFill = new Color(0.93f, 0.89f, 0.8f);
    private static readonly Color SlotEdge = new Color(0.55f, 0.42f, 0.28f);
    private const float SlotWidth = 84f;
    private static Sprite hexSprite;

    private TMP_FontAsset font;
    private Sprite panelSprite, buttonSprite, closeSprite;
    private int sortingOrder = 255;
    private Behaviour[] inputComponents;
    private Rigidbody playerBody;
    private PauseMenu pauseMenu;
    private TradingPostUI tradingPost;
    private SleepTransition sleepTransition;
    private QuestWindow questWindow;

    private HabitatCatalog catalog;
    private GameObject root;
    private RectTransform listRoot, previewRoot;
    private TMP_Text nameText, stateText, requirementText;
    private readonly List<(HabitatDefinition habitat, Image background, TMP_Text label)> rows =
        new List<(HabitatDefinition, Image, TMP_Text)>();
    private HabitatDefinition selected;
    private bool[] previouslyEnabled;

    public bool IsOpen { get; private set; }

    public void Configure(TMP_FontAsset uiFont, Sprite panel, Sprite button, Sprite close, int order, Behaviour[] inputs,
        Rigidbody body, PauseMenu pause, TradingPostUI trading, SleepTransition sleep, QuestWindow quests)
    {
        font = uiFont; panelSprite = panel; buttonSprite = button; closeSprite = close; sortingOrder = order;
        inputComponents = inputs; playerBody = body; pauseMenu = pause; tradingPost = trading; sleepTransition = sleep;
        questWindow = quests;
    }

    private static bool TogglePressed => Application.isFocused && Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame;

    private void Update()
    {
        if (IsOpen)
        {
            // Consumed so the same Esc press does not also open the pause menu.
            if (UIEscape.WasPressed) { UIEscape.Consume(); Close(); }
            else if (TogglePressed) Close();
            return;
        }
        if (TogglePressed) Open();
    }

    private void OnEnable()
    {
        HabitatRecords.FirstDiscovered += OnRecordsChanged;
        HabitatRecords.Restored += Refresh;
    }

    // Suspending or unloading the scene must never leave the player locked.
    private void OnDisable()
    {
        HabitatRecords.FirstDiscovered -= OnRecordsChanged;
        HabitatRecords.Restored -= Refresh;
        Close();
    }

    private void OnRecordsChanged(string id) => Refresh();

    private bool CanOpen() =>
        isActiveAndEnabled && !SceneTransitionService.IsTraveling &&
        (pauseMenu == null || !pauseMenu.IsOpen) && (tradingPost == null || !tradingPost.IsOpen) &&
        (sleepTransition == null || !sleepTransition.IsSleeping) && (questWindow == null || !questWindow.IsOpen);

    public void Open()
    {
        if (IsOpen || !CanOpen()) return;
        catalog = catalog != null ? catalog : HabitatCatalog.Load();
        if (catalog == null)
        {
            Debug.LogWarning($"도감 데이터가 없습니다 (Resources/{HabitatCatalog.ResourcePath}).", this);
            return;
        }
        EnsureBuilt();
        IsOpen = true;
        LockInput();
        if (selected == null && catalog.Habitats.Count > 0) selected = catalog.Habitats[0];
        root.SetActive(true);
        AudioManager.Play(GameSfx.MenuToggle);
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (root != null) root.SetActive(false);
        UnlockInput();
        // Player closes only: suspending or unloading the scene (OnDisable) stays quiet.
        if (isActiveAndEnabled) AudioManager.Play(GameSfx.MenuToggle);
    }

    private void Select(HabitatDefinition habitat)
    {
        selected = habitat;
        Refresh();
    }

    // ---- View ----

    private void Refresh()
    {
        if (!IsOpen || root == null || catalog == null) return;
        foreach (var row in rows)
        {
            bool found = HabitatRecords.IsDiscovered(row.habitat.Id);
            row.label.text = $"{row.habitat.DisplayName}\n<size=75%><color=#{ColorUtility.ToHtmlStringRGB(found ? FoundColor : MutedColor)}>{(found ? "발견" : "미발견")}</color></size>";
            row.background.color = row.habitat == selected ? new Color(1f, 0.86f, 0.6f) : Color.white;
        }
        if (selected == null) return;
        bool discovered = HabitatRecords.IsDiscovered(selected.Id);
        nameText.text = selected.DisplayName;
        stateText.text = discovered ? "발견함" : "아직 발견하지 못했어";
        stateText.color = discovered ? FoundColor : MutedColor;
        requirementText.text = $"필요한 환경:  {selected.RequirementText}";
        BuildPreview(selected, discovered);
    }

    // The judged shape itself: one slot per pattern cell at its axial offset (pointy-top, like the grid).
    // Not yet discovered: blank "?" slots. Discovered: each slot shows its environment by name on a light tint of that
    // environment's colour (text, no icon art yet).
    private void BuildPreview(HabitatDefinition habitat, bool discovered)
    {
        for (int i = previewRoot.childCount - 1; i >= 0; i--) Destroy(previewRoot.GetChild(i).gameObject);
        IReadOnlyList<HabitatDefinition.PatternCell> pattern = habitat.Pattern;
        if (pattern.Count == 0) return;
        var centres = new List<Vector2>();
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        foreach (HabitatDefinition.PatternCell cell in pattern)
        {
            Vector3 local = HexMetrics.LocalCenter(cell.offset, SlotWidth);
            var point = new Vector2(local.x, local.z);
            centres.Add(point);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        Vector2 middle = (min + max) * 0.5f;
        float height = SlotWidth * 2f / HexMetrics.Sqrt3;
        for (int i = 0; i < centres.Count; i++)
        {
            Vector2 position = centres[i] - middle;
            EnvironmentTileType tile = pattern[i].tile;
            Color fill = discovered ? Color.Lerp(SlotFill, EnvironmentTileTypes.DefaultColor(tile), 0.45f) : SlotFill;
            Image edge = CreateHex("SlotEdge", previewRoot, position, new Vector2(SlotWidth, height) * 0.96f, SlotEdge);
            CreateHex("Slot", edge.transform, Vector2.zero, new Vector2(SlotWidth, height) * 0.86f, fill);
            TMP_Text mark = CreateText("Mark", edge.transform, discovered ? EnvironmentTileTypes.DisplayName(tile) : "?",
                discovered ? 24f : 30f, TextAlignmentOptions.Center);
            mark.color = discovered ? TextColor : MutedColor;
            Stretch((RectTransform)mark.transform);
        }
    }

    // ---- Construction (runtime, same conventions as QuestWindow / TradingPostUI) ----

    private void EnsureBuilt()
    {
        if (root != null) return;
        root = new GameObject("HabitatJournalCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform dim = CreateRect("Dim", root.transform);
        Stretch(dim);
        dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.38f);

        RectTransform panel = CreateRect("Panel", root.transform);
        panel.sizeDelta = new Vector2(1100f, 660f);
        ApplySprite(panel.gameObject.AddComponent<Image>(), panelSprite, PanelFallback);

        TMP_Text title = CreateText("Title", panel, "생태 도감", 34f, TextAlignmentOptions.Center);
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -44f), new Vector2(0f, 56f));

        Button close = CreateButton("Close", panel, closeSprite != null ? "" : "X", Close);
        RectTransform closeRect = (RectTransform)close.transform;
        closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = Vector2.one;
        closeRect.anchoredPosition = new Vector2(-20f, -20f);
        closeRect.sizeDelta = new Vector2(44f, 44f);
        if (closeSprite != null) ApplySprite(close.GetComponent<Image>(), closeSprite, Color.white);

        // Left: the list.
        listRoot = CreateRect("List", panel);
        Place(listRoot, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(190f, -20f), new Vector2(300f, -180f));
        VerticalLayoutGroup list = listRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        list.spacing = 10f;
        list.childControlWidth = list.childControlHeight = true;
        list.childForceExpandWidth = true;
        list.childForceExpandHeight = false;
        catalog = catalog != null ? catalog : HabitatCatalog.Load();
        foreach (HabitatDefinition habitat in catalog.Habitats)
        {
            if (habitat == null) continue;
            HabitatDefinition target = habitat;
            Button row = CreateButton($"Row_{habitat.Id}", listRoot, "", () => Select(target));
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 76f;
            TMP_Text label = CreateText("Label", row.transform, "", 26f, TextAlignmentOptions.MidlineLeft);
            Stretch(label.rectTransform);
            label.margin = new Vector4(22f, 0f, 12f, 0f);
            rows.Add((habitat, row.GetComponent<Image>(), label));
        }

        // Right: the detail of the selected habitat.
        RectTransform detail = CreateRect("Detail", panel);
        detail.anchorMin = Vector2.zero;
        detail.anchorMax = Vector2.one;
        detail.offsetMin = new Vector2(380f, 60f);
        detail.offsetMax = new Vector2(-50f, -110f);
        nameText = CreateText("Name", detail, "", 40f, TextAlignmentOptions.TopLeft);
        Place(nameText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -30f), new Vector2(0f, 60f));
        stateText = CreateText("State", detail, "", 24f, TextAlignmentOptions.TopLeft);
        Place(stateText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -78f), new Vector2(0f, 36f));

        TMP_Text patternLabel = CreateText("PatternLabel", detail, "서식지 패턴", 22f, TextAlignmentOptions.TopLeft);
        patternLabel.color = MutedColor;
        Place(patternLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -122f), new Vector2(0f, 30f));
        RectTransform previewFrame = CreateRect("PreviewFrame", detail);
        Place(previewFrame, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -262f), new Vector2(0f, 240f));
        previewFrame.gameObject.AddComponent<Image>().color = new Color(0.9f, 0.85f, 0.74f, 0.55f);
        previewRoot = CreateRect("Preview", previewFrame);
        previewRoot.anchorMin = previewRoot.anchorMax = new Vector2(0.5f, 0.5f);
        previewRoot.sizeDelta = Vector2.zero;

        requirementText = CreateText("Requirement", detail, "", 28f, TextAlignmentOptions.TopLeft);
        Place(requirementText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -412f), new Vector2(0f, 44f));
        TMP_Text rule = CreateText("Rule", detail, "칸 모양과 위치가 그림과 같아야 해요. 돌리거나 좌우를 뒤집어 놓아도 괜찮아요.", 20f, TextAlignmentOptions.TopLeft);
        rule.color = MutedColor;
        rule.textWrappingMode = TextWrappingModes.Normal;
        Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -460f), new Vector2(0f, 56f));

        TMP_Text hint = CreateText("Hint", panel, "K / Esc: 닫기", 18f, TextAlignmentOptions.Center);
        hint.color = MutedColor;
        Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 30f), new Vector2(0f, 30f));
        root.SetActive(false);
    }

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
        if (playerBody != null && !playerBody.isKinematic)
        {
            Vector3 velocity = playerBody.linearVelocity;
            playerBody.linearVelocity = new Vector3(0f, velocity.y, 0f);
        }
    }

    private void UnlockInput()
    {
        if (previouslyEnabled == null) return;
        for (int i = 0; i < previouslyEnabled.Length; i++)
            if (inputComponents[i] != null && inputComponents[i] != this && previouslyEnabled[i])
                inputComponents[i].enabled = true;
        previouslyEnabled = null;
    }

    // ---- Small UI helpers ----

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

    // Anchored strip: position is the pivot's offset, size the delta along non-stretched axes.
    private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void ApplySprite(Image image, Sprite sprite, Color fallback)
    {
        image.sprite = sprite;
        image.color = sprite != null ? Color.white : fallback;
        image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
    }

    private TMP_Text CreateText(string objectName, Transform parent, string text, float size, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI label = CreateRect(objectName, parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = size;
        label.color = TextColor;
        label.alignment = alignment;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        return label;
    }

    private Button CreateButton(string objectName, Transform parent, string text, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rect = CreateRect(objectName, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        ApplySprite(image, buttonSprite, new Color(0.9f, 0.84f, 0.72f));
        if (buttonSprite != null) image.color = Color.white;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(onClick);
        if (!string.IsNullOrEmpty(text))
        {
            TMP_Text label = CreateText("Label", rect, text, 22f, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
        }
        return button;
    }

    private static Image CreateHex(string objectName, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        RectTransform rect = CreateRect(objectName, parent);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = HexSprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    // A white pointy-top hexagon (placeholder art), generated once.
    private static Sprite HexSprite
    {
        get
        {
            if (hexSprite != null) return hexSprite;
            const int width = 110, height = 127;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[width * height];
            float cx = (width - 1) * 0.5f, cy = (height - 1) * 0.5f, inner = width * 0.5f;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    // Pointy-top: flat sides left/right (|x| <= inner) and two slanted pairs.
                    float px = Mathf.Abs(x - cx), py = Mathf.Abs(y - cy);
                    float d = Mathf.Max(px, px * 0.5f + py * (HexMetrics.Sqrt3 * 0.5f));
                    byte alpha = (byte)(Mathf.Clamp01(inner - d + 0.5f) * 255f);
                    pixels[y * width + x] = new Color32(255, 255, 255, alpha);
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            hexSprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
            return hexSprite;
        }
    }
}
