using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Function-check UI only; the team will replace the presentation later.
// Builds its own overlay canvas at runtime so FarmScene needs no hand-authored UI hierarchy.
[DisallowMultipleComponent]
public sealed class TradingPostUI : MonoBehaviour
{
    [Header("Appearance (UI ver.2 assets)")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Sprite panelSprite;
    [SerializeField] private Sprite buttonSprite;
    [SerializeField] private Sprite closeSprite;
    [SerializeField] private int sortingOrder = 200;
    [Header("Input lock (same list as SleepTransition)")]
    [SerializeField] private Behaviour[] inputComponents;
    [SerializeField] private Rigidbody playerBody;
    [SerializeField] private SleepTransition sleepTransition;
    [Header("DEV (existing GuideTooltip DEV toggle)")]
    [SerializeField] private GuideTooltip developerSource;
    [SerializeField, Min(1)] private int developerGoldAmount = 1000;

    private static readonly Color TextColor = new Color(0.27f, 0.19f, 0.12f);
    private static readonly Color MutedColor = new Color(0.45f, 0.38f, 0.3f);
    private readonly Dictionary<ItemDefinition, int> selectedQuantities = new Dictionary<ItemDefinition, int>();
    private PlayerInventory inventory;
    private PlayerWallet wallet;
    private GameObject root;
    private RectTransform rowContainer;
    private TMP_Text goldText, emptyText, messageText;
    private GameObject developerButton;
    private bool[] previouslyEnabled;
    private string message = "";

    public bool IsOpen { get; private set; }

    public void Open(PlayerInventory targetInventory, PlayerWallet targetWallet)
    {
        if (IsOpen || targetInventory == null || targetWallet == null || !isActiveAndEnabled) return;
        if ((sleepTransition != null && sleepTransition.IsSleeping) || SceneTransitionService.IsTraveling) return;
        EnsureBuilt();
        inventory = targetInventory;
        wallet = targetWallet;
        inventory.Changed += Refresh;
        wallet.Changed += Refresh;
        IsOpen = true;
        message = "";
        selectedQuantities.Clear();
        LockInput();
        root.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (inventory != null) inventory.Changed -= Refresh;
        if (wallet != null) wallet.Changed -= Refresh;
        inventory = null;
        wallet = null;
        if (root != null) root.SetActive(false);
        UnlockInput();
    }

    private void Update()
    {
        if (!IsOpen) return;
        // Consumed so the same Esc press does not also open the pause menu.
        if (UIEscape.WasPressed)
        {
            UIEscape.Consume();
            Close();
            return;
        }
        bool showDeveloper = developerSource != null && developerSource.DeveloperMode;
        if (developerButton != null && developerButton.activeSelf != showDeveloper) developerButton.SetActive(showDeveloper);
    }

    private void OnDisable() => Close();

    private void Sell(ItemDefinition item, int quantity)
    {
        if (!IsOpen) return;
        TradeService.SellResult result = TradeService.TrySell(inventory, wallet, item, quantity, out int earned);
        message = result switch
        {
            TradeService.SellResult.Sold => $"{item.DisplayName} {quantity}개 판매: +{earned} G",
            TradeService.SellResult.NotEnoughItems => "보유 수량보다 많이 판매할 수 없습니다.",
            TradeService.SellResult.NotSellable => "판매할 수 없는 아이템입니다.",
            TradeService.SellResult.WalletFull => "골드를 더 받을 수 없습니다.",
            _ => "판매에 실패했습니다."
        };
        if (result == TradeService.SellResult.Sold)
        {
            selectedQuantities[item] = 1;
            Debug.Log($"교역소 판매: {item.DisplayName} {quantity}개 → +{earned} G (보유 골드: {wallet.Gold} G)", this);
        }
        Refresh();
    }

    private void ChangeQuantity(ItemDefinition item, int delta)
    {
        if (!IsOpen) return;
        selectedQuantities[item] = ClampQuantity(item, GetSelected(item) + delta);
        Refresh();
    }

    private void AddDeveloperGold()
    {
        if (!IsOpen || developerSource == null || !developerSource.DeveloperMode) return;
        message = wallet.AddGold(developerGoldAmount) ? $"DEV: +{developerGoldAmount} G" : "골드를 더 받을 수 없습니다.";
        Refresh();
    }

    private int GetSelected(ItemDefinition item) => selectedQuantities.TryGetValue(item, out int value) ? value : 1;
    private int ClampQuantity(ItemDefinition item, int value) =>
        Mathf.Clamp(value, 1, Mathf.Max(1, inventory != null ? inventory.GetQuantity(item) : 1));

    private void Refresh()
    {
        if (!IsOpen || root == null) return;
        goldText.text = $"보유 골드: {wallet.Gold} G";
        messageText.text = message;
        for (int i = rowContainer.childCount - 1; i >= 0; i--)
        {
            GameObject row = rowContainer.GetChild(i).gameObject;
            row.SetActive(false); // Destroy is deferred; hide immediately so layout does not count it.
            Destroy(row);
        }
        List<ItemDefinition> sellable = new List<ItemDefinition>();
        foreach (KeyValuePair<ItemDefinition, int> entry in inventory.CaptureQuantities())
            if (entry.Key != null && entry.Value > 0 && entry.Key.CanSell) sellable.Add(entry.Key);
        sellable.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));
        foreach (ItemDefinition item in sellable) BuildRow(item);
        emptyText.gameObject.SetActive(sellable.Count == 0);
    }

    private void BuildRow(ItemDefinition item)
    {
        int owned = inventory.GetQuantity(item);
        int selected = ClampQuantity(item, GetSelected(item));
        selectedQuantities[item] = selected;
        int price = TradeService.GetUnitSellPrice(item);

        RectTransform row = CreateRect($"Row_{item.name}", rowContainer);
        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        SetSize(row.gameObject, -1f, 64f);

        TMP_Text info = CreateText("Info", row, $"{item.DisplayName}\n<size=18>보유: {owned}   판매가: {price} G</size>", 24f, TextAlignmentOptions.MidlineLeft);
        SetSize(info.gameObject, -1f, 64f, flexibleWidth: 1f);
        CreateButton("Minus", row, "-", 48f, () => ChangeQuantity(item, -1)).interactable = selected > 1;
        TMP_Text quantity = CreateText("Quantity", row, selected.ToString(), 24f, TextAlignmentOptions.Center);
        SetSize(quantity.gameObject, 56f, 48f);
        CreateButton("Plus", row, "+", 48f, () => ChangeQuantity(item, 1)).interactable = selected < owned;
        CreateButton("Sell", row, $"판매 ({selected * (long)price} G)", 170f, () => Sell(item, GetSelected(item)));
        CreateButton("SellAll", row, "전부 판매", 120f, () => Sell(item, inventory.GetQuantity(item)));
    }

    private void EnsureBuilt()
    {
        if (root != null) return;
        root = new GameObject("TradingPostCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Full-screen dim also blocks clicks on the HUD underneath.
        RectTransform dim = CreateRect("Dim", root.transform);
        Stretch(dim);
        dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

        RectTransform panel = CreateRect("Panel", root.transform);
        panel.sizeDelta = new Vector2(820f, 0f);
        Image panelImage = panel.gameObject.AddComponent<Image>();
        ApplySprite(panelImage, panelSprite, new Color(0.98f, 0.95f, 0.88f));
        VerticalLayoutGroup vertical = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vertical.padding = new RectOffset(44, 44, 36, 36);
        vertical.spacing = 12f;
        vertical.childControlWidth = vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        SetSize(CreateText("Title", panel, "교역소", 36f, TextAlignmentOptions.Center).gameObject, -1f, 48f);
        goldText = CreateText("Gold", panel, "", 26f, TextAlignmentOptions.Center);
        SetSize(goldText.gameObject, -1f, 36f);
        TMP_Text note = CreateText("PriceNote", panel, "※ 판매 가격은 임시 테스트 값입니다.", 18f, TextAlignmentOptions.Center);
        note.color = MutedColor;
        SetSize(note.gameObject, -1f, 26f);

        rowContainer = CreateRect("Rows", panel);
        VerticalLayoutGroup rows = rowContainer.gameObject.AddComponent<VerticalLayoutGroup>();
        rows.spacing = 8f;
        rows.childControlWidth = rows.childControlHeight = true;
        rows.childForceExpandWidth = true;
        rows.childForceExpandHeight = false;

        emptyText = CreateText("Empty", panel, "판매할 수 있는 아이템이 없습니다.", 22f, TextAlignmentOptions.Center);
        emptyText.color = MutedColor;
        SetSize(emptyText.gameObject, -1f, 40f);
        messageText = CreateText("Message", panel, "", 20f, TextAlignmentOptions.Center);
        SetSize(messageText.gameObject, -1f, 30f);

        RectTransform footer = CreateRect("Footer", panel);
        HorizontalLayoutGroup footerLayout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
        footerLayout.spacing = 16f;
        footerLayout.childAlignment = TextAnchor.MiddleCenter;
        footerLayout.childControlWidth = footerLayout.childControlHeight = true;
        footerLayout.childForceExpandWidth = footerLayout.childForceExpandHeight = false;
        SetSize(footer.gameObject, -1f, 56f);
        developerButton = CreateButton("DevGold", footer, $"+{developerGoldAmount} G (DEV)", 200f, AddDeveloperGold).gameObject;
        developerButton.SetActive(false);
        CreateButton("Close", footer, "닫기", 160f, Close);

        // X button sits outside the layout, pinned to the panel's top-right corner.
        Button closeX = CreateButton("CloseX", panel, closeSprite != null ? "" : "X", 52f, Close);
        closeX.GetComponent<LayoutElement>().ignoreLayout = true;
        RectTransform closeRect = (RectTransform)closeX.transform;
        closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = Vector2.one;
        closeRect.anchoredPosition = new Vector2(-18f, -18f);
        closeRect.sizeDelta = new Vector2(52f, 52f);
        if (closeSprite != null) ApplySprite(closeX.GetComponent<Image>(), closeSprite, Color.white);

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

    private static void SetSize(GameObject go, float width, float height, float flexibleWidth = -1f)
    {
        LayoutElement element = go.GetComponent<LayoutElement>();
        if (element == null) element = go.AddComponent<LayoutElement>();
        if (width >= 0f) { element.preferredWidth = width; element.minWidth = width; }
        if (height >= 0f) { element.preferredHeight = height; element.minHeight = height; }
        element.flexibleWidth = flexibleWidth;
    }

    private static void ApplySprite(Image image, Sprite sprite, Color fallback)
    {
        image.sprite = sprite;
        image.color = sprite != null ? Color.white : fallback;
        image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        if (image.type == Image.Type.Sliced) image.pixelsPerUnitMultiplier = 3f;
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

    private Button CreateButton(string objectName, Transform parent, string text, float width, UnityAction onClick)
    {
        RectTransform rect = CreateRect(objectName, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        ApplySprite(image, buttonSprite, new Color(0.9f, 0.84f, 0.72f));
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None }; // WASD/arrows must not move UI focus.
        button.onClick.AddListener(onClick);
        SetSize(rect.gameObject, width, 48f);
        if (!string.IsNullOrEmpty(text))
        {
            TMP_Text label = CreateText("Label", rect, text, 20f, TextAlignmentOptions.Center);
            Stretch((RectTransform)label.transform);
        }
        return button;
    }
}
