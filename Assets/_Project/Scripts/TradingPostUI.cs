using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Function-check UI only; the team will replace the presentation later.
// Builds its own overlay canvas at runtime so the village needs no hand-authored UI hierarchy.
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
    [Header("Shop (구매 탭 목록 · 가격은 각 ItemDefinition의 Buy Price)")]
    [SerializeField, Tooltip("교역소에서 살 수 있는 아이템. 이후 씨앗·묘목·먹이·복원 재료도 여기에 추가합니다. Can Buy가 꺼진 아이템은 표시되지 않습니다.")]
    private ItemDefinition[] buyCatalog = new ItemDefinition[0];
    [SerializeField, Min(1), Tooltip("한 번에 구매할 수 있는 최대 수량.")]
    private int maxBuyQuantity = 99;

    private enum Tab { Sell, Buy }
    private static readonly Color TextColor = new Color(0.27f, 0.19f, 0.12f);
    private static readonly Color MutedColor = new Color(0.45f, 0.38f, 0.3f);
    private static readonly Color SelectedTabTint = new Color(1f, 0.82f, 0.5f);
    private readonly Dictionary<ItemDefinition, int> selectedQuantities = new Dictionary<ItemDefinition, int>();
    private readonly Dictionary<ItemDefinition, int> buyQuantities = new Dictionary<ItemDefinition, int>();
    private PlayerInventory inventory;
    private PlayerWallet wallet;
    private GameObject root;
    private RectTransform rowContainer;
    private RectTransform listViewport, detailPanel;
    private UnityEngine.UI.ScrollRect itemScroll;
    private TMP_Text detailSummary, detailHistory;
    private ItemDefinition selectedMarketItem;
    private readonly Dictionary<ItemDefinition, UnityEngine.UI.Image> sellRowBackgrounds =
        new Dictionary<ItemDefinition, UnityEngine.UI.Image>();
    private TMP_Text goldText, emptyText, messageText, noteText;
    private Button sellTabButton, buyTabButton;
    private GameObject developerButton, developerMarketButton;
    private MarketPriceSystem market;
    private bool[] previouslyEnabled;
    private string message = "";
    private Tab tab;

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
        // Event driven: a market step while the window is open redraws the rows; prices are never polled per frame.
        market = MarketPriceSystem.Active;
        if (market != null) market.MarketChanged += Refresh;
        IsOpen = true;
        message = "";
        tab = Tab.Sell;
        selectedQuantities.Clear();
        buyQuantities.Clear();
        selectedMarketItem = null;
        itemScroll.verticalNormalizedPosition = 1f;
        LockInput();
        root.SetActive(true);
        AudioManager.Play(GameSfx.MenuToggle);
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (inventory != null) inventory.Changed -= Refresh;
        if (wallet != null) wallet.Changed -= Refresh;
        if (market != null) market.MarketChanged -= Refresh;
        inventory = null;
        wallet = null;
        market = null;
        if (root != null) root.SetActive(false);
        UnlockInput();
        // Player closes only: a scene suspended or unloaded with the window open (OnDisable) stays quiet.
        if (isActiveAndEnabled) AudioManager.Play(GameSfx.MenuToggle);
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
        bool showMarket = showDeveloper && market != null;
        if (developerMarketButton != null && developerMarketButton.activeSelf != showMarket) developerMarketButton.SetActive(showMarket);
    }

    // DEV: one market step without changing the day (same as MarketPriceSystem's context menu).
    private void AdvanceMarketForDeveloper()
    {
        if (!IsOpen || market == null || developerSource == null || !developerSource.DeveloperMode) return;
        message = "DEV: 시세 하루 갱신 (날짜는 그대로)";
        market.DebugAdvance(); // MarketChanged → Refresh
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
            TradeService.SellResult.Blocked => TradeService.LastBlockReason,
            _ => "판매에 실패했습니다."
        };
        // The button's own sound replaces the generic click.
        AudioManager.Play(result == TradeService.SellResult.Sold ? GameSfx.Sell : GameSfx.ActionFail);
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

    // ---- Buy tab ----

    private void SelectTab(Tab next)
    {
        if (!IsOpen || tab == next) return;
        tab = next;
        message = "";
        itemScroll.StopMovement();
        itemScroll.verticalNormalizedPosition = 1f;
        Refresh();
    }

    private void Buy(ItemDefinition item, int quantity)
    {
        if (!IsOpen) return;
        TradeService.BuyResult result = TradeService.TryBuy(inventory, wallet, item, quantity, out int spent);
        message = result switch
        {
            TradeService.BuyResult.Bought => $"{item.DisplayName} {quantity}개 구매: -{spent} G",
            TradeService.BuyResult.NotEnoughGold => "골드가 부족합니다.",
            TradeService.BuyResult.NotForSale => "구매할 수 없는 아이템입니다.",
            TradeService.BuyResult.InventoryFull => "더 가질 수 없습니다.",
            _ => "구매에 실패했습니다."
        };
        AudioManager.Play(result == TradeService.BuyResult.Bought ? GameSfx.Buy : GameSfx.ActionFail);
        if (result == TradeService.BuyResult.Bought)
        {
            buyQuantities[item] = 1;
            Debug.Log($"교역소 구매: {item.DisplayName} {quantity}개 → -{spent} G (보유 골드: {wallet.Gold} G)", this);
        }
        Refresh();
    }

    private void ChangeBuyQuantity(ItemDefinition item, int delta)
    {
        if (!IsOpen) return;
        buyQuantities[item] = ClampBuyQuantity(item, GetBuySelected(item) + delta);
        Refresh();
    }

    private int GetBuySelected(ItemDefinition item) => buyQuantities.TryGetValue(item, out int value) ? value : 1;
    private int MaxAffordable(ItemDefinition item)
    {
        int price = TradeService.GetUnitBuyPrice(item);
        return price > 0 && wallet != null ? wallet.Gold / price : 0;
    }
    private int ClampBuyQuantity(ItemDefinition item, int value) =>
        Mathf.Clamp(value, 1, Mathf.Max(1, Mathf.Min(maxBuyQuantity, MaxAffordable(item))));

    private void Refresh()
    {
        if (!IsOpen || root == null) return;
        goldText.text = $"보유 골드: {wallet.Gold} G";
        messageText.text = message;
        RefreshTabs();
        sellRowBackgrounds.Clear();
        for (int i = rowContainer.childCount - 1; i >= 0; i--)
        {
            GameObject row = rowContainer.GetChild(i).gameObject;
            row.SetActive(false); // Destroy is deferred; hide immediately so layout does not count it.
            Destroy(row);
        }
        List<ItemDefinition> items = new List<ItemDefinition>();
        if (tab == Tab.Sell)
        {
            foreach (KeyValuePair<ItemDefinition, int> entry in inventory.CaptureQuantities())
                if (entry.Key != null && entry.Value > 0 && entry.Key.CanSell) items.Add(entry.Key);
            items.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));
            if (!items.Contains(selectedMarketItem)) selectedMarketItem = items.Count > 0 ? items[0] : null;
            foreach (ItemDefinition item in items) BuildRow(item);
        }
        else
        {
            // Catalog order is the shop order.
            if (buyCatalog != null)
                foreach (ItemDefinition item in buyCatalog)
                    if (item != null && item.CanBuy && !items.Contains(item)) items.Add(item);
            foreach (ItemDefinition item in items) BuildBuyRow(item);
        }
        emptyText.text = tab == Tab.Sell ? "판매할 수 있는 아이템이 없습니다." : "구매할 수 있는 아이템이 없습니다.";
        emptyText.gameObject.SetActive(items.Count == 0);
        listViewport.gameObject.SetActive(items.Count > 0);
        // Limit panel growth; inventory changes and DEV updates keep the current scroll offset.
        float rowHeight = tab == Tab.Sell ? 128f : 64f;
        SetSize(listViewport.gameObject, -1f, Mathf.Min(264f, items.Count * (rowHeight + 8f) - 8f));
        RefreshMarketDetail();
    }

    private void RefreshTabs()
    {
        noteText.text = tab == Tab.Sell
            ? (market != null ? $"현재 보유 판매품 총 가치  {TotalSellableValue():N0} G  ·  오늘 시세 기준" : "시세 정보 연결 없음")
            : "※ 구매 가격은 고정 · 임시 테스트 값입니다.";
        SetTabVisual(sellTabButton, tab == Tab.Sell);
        SetTabVisual(buyTabButton, tab == Tab.Buy);
    }

    private void SetTabVisual(Button button, bool selected)
    {
        if (button == null) return;
        Image image = button.GetComponent<Image>();
        if (image != null) image.color = selected ? SelectedTabTint : buttonSprite != null ? Color.white : new Color(0.9f, 0.84f, 0.72f);
        TMP_Text label = button.GetComponentInChildren<TMP_Text>();
        if (label != null) label.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
    }

    private void BuildBuyRow(ItemDefinition item)
    {
        int owned = inventory.GetQuantity(item);
        int price = TradeService.GetUnitBuyPrice(item);
        int selected = ClampBuyQuantity(item, GetBuySelected(item));
        buyQuantities[item] = selected;
        int limit = Mathf.Max(1, Mathf.Min(maxBuyQuantity, MaxAffordable(item)));

        RectTransform row = CreateRow($"BuyRow_{item.name}");
        TMP_Text info = CreateText("Info", row, $"{item.DisplayName}\n<size=18>보유: {owned}   가격: {price} G</size>", 24f, TextAlignmentOptions.MidlineLeft);
        SetSize(info.gameObject, -1f, 64f, flexibleWidth: 1f);
        CreateButton("Minus", row, "-", 48f, () => ChangeBuyQuantity(item, -1)).interactable = selected > 1;
        TMP_Text quantity = CreateText("Quantity", row, selected.ToString(), 24f, TextAlignmentOptions.Center);
        SetSize(quantity.gameObject, 56f, 48f);
        CreateButton("Plus", row, "+", 48f, () => ChangeBuyQuantity(item, 1)).interactable = selected < limit;
        // Stays clickable without enough gold so the click explains why (message line).
        CreateButton("Buy", row, $"구매 ({selected * (long)price} G)", 170f, () => Buy(item, GetBuySelected(item)));
    }

    private RectTransform CreateRow(string objectName, float height = 64f)
    {
        RectTransform row = CreateRect(objectName, rowContainer);
        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        SetSize(row.gameObject, -1f, height);
        return row;
    }

    // ---- Sell tab ----

    // Everything sellable the player holds, sold today at today's price (no purchase cost is tracked).
    private long TotalSellableValue()
    {
        long total = 0;
        if (inventory == null) return total;
        foreach (KeyValuePair<ItemDefinition, int> entry in inventory.CaptureQuantities())
            if (entry.Key != null && entry.Value > 0 && entry.Key.CanSell)
                total += market != null ? market.GetStackValue(entry.Key, entry.Value) : 0;
        return total;
    }

    // "오늘 13G · 기준 10G (+30%) · 어제 +2G 상승". Words, not ▲/▼, so the Korean UI font needs no extra glyphs.
    // Only formatting belongs here. All prices, deltas and valuations come from the market.
    private int TodayPrice(ItemDefinition item) =>
        market != null ? market.GetCurrentPrice(item) : TradeService.GetUnitSellPrice(item);

    private string StackValue(ItemDefinition item, int amount) =>
        market != null ? $"{market.GetStackValue(item, amount):N0} G" : "시세 연결 없음";

    private string BaseComparison(ItemDefinition item) =>
        market != null
            ? $"기준 {market.GetBasePrice(item):N0}G · <b>{market.GetBaseChangePercent(item):+0.#;-0.#;0}%</b>"
            : "시세 연결 없음";

    private string DailyChange(ItemDefinition item)
    {
        if (market == null) return "전날 정보 없음";
        int difference = market.GetDailyDifference(item);
        return difference > 0 ? $"<color=#956139>어제보다 +{difference:N0}G 상승</color>"
            : difference < 0 ? $"<color=#456B78>어제보다 {difference:N0}G 하락</color>"
            : "<color=#73614D>어제와 동일 · 보합</color>";
    }

    private void SelectMarketItem(ItemDefinition item)
    {
        selectedMarketItem = item;
        RefreshMarketDetail();
    }

    private void RefreshMarketDetail()
    {
        bool visible = tab == Tab.Sell && selectedMarketItem != null;
        detailPanel.gameObject.SetActive(visible);
        foreach (var entry in sellRowBackgrounds)
            entry.Value.color = entry.Key == selectedMarketItem
                ? new Color(0.94f, 0.85f, 0.66f) : new Color(0.96f, 0.92f, 0.83f);
        if (!visible) return;

        ItemDefinition item = selectedMarketItem;
        int owned = inventory.GetQuantity(item);
        string yesterday = market != null ? $"{market.GetPreviousPrice(item):N0}G" : "-";
        detailSummary.text = $"<b>{item.DisplayName}</b>  ·  오늘 <size=28><b>{TodayPrice(item):N0}G</b></size>    {BaseComparison(item)}\n"
            + $"전날 {yesterday}  /  {DailyChange(item)}    보유 {owned:N0}개  ·  전량 가치 <b>{StackValue(item, owned)}</b>";
        if (market == null)
        {
            detailHistory.text = "최근 7일 · 시세 연결 없음";
            return;
        }
        int[] history = market.GetPriceHistory(item);
        int count = Mathf.Min(7, history.Length);
        var prices = new List<string>(count);
        for (int i = history.Length - count; i < history.Length; i++) prices.Add(history[i].ToString("N0"));
        detailHistory.text = count > 0
            ? $"최근 7일 · 기록 {count}일 (오래된 순 / 마지막은 오늘, G)\n{string.Join(" / ", prices)}"
            : "최근 7일 · 기록 없음";
    }

    private void BuildRow(ItemDefinition item)
    {
        int owned = inventory.GetQuantity(item);
        int selected = ClampQuantity(item, GetSelected(item));
        selectedQuantities[item] = selected;

        RectTransform row = CreateRect($"Row_{item.name}", rowContainer);
        SetSize(row.gameObject, -1f, 128f);
        UnityEngine.UI.Image background = row.gameObject.AddComponent<UnityEngine.UI.Image>();
        sellRowBackgrounds[item] = background;
        UnityEngine.UI.Button select = row.gameObject.AddComponent<UnityEngine.UI.Button>();
        select.targetGraphic = background;
        select.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        select.onClick.AddListener(() => SelectMarketItem(item));
        var layout = row.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 10);
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        RectTransform summary = CreateRowLine("Summary", row, 52f);
        TMP_Text name = CreateText("Name", summary, $"<b>{item.DisplayName}</b>\n<size=15>선택하여 시세 기록 보기</size>", 23f, TextAlignmentOptions.MidlineLeft);
        SetSize(name.gameObject, 210f, 52f);
        name.enableAutoSizing = true;
        name.fontSizeMin = 17f;
        name.fontSizeMax = 23f;
        name.overflowMode = TextOverflowModes.Ellipsis;
        TMP_Text today = CreateText("TodayPrice", summary, $"<size=16>오늘 판매가</size>\n<b>{TodayPrice(item):N0}G</b>", 30f, TextAlignmentOptions.MidlineLeft);
        SetSize(today.gameObject, 210f, 52f);
        today.enableAutoSizing = true;
        today.fontSizeMin = 18f;
        today.fontSizeMax = 30f;
        TMP_Text comparison = CreateText("Comparison", summary, $"{BaseComparison(item)}\n<size=17>{DailyChange(item)}</size>", 21f, TextAlignmentOptions.MidlineLeft);
        SetSize(comparison.gameObject, -1f, 52f, flexibleWidth: 1f);
        comparison.enableAutoSizing = true;
        comparison.fontSizeMin = 16f;
        comparison.fontSizeMax = 21f;

        RectTransform actions = CreateRowLine("Actions", row, 48f);
        TMP_Text stock = CreateText("StockValue", actions,
            $"보유 {owned:N0}개\n전량 가치 <b>{StackValue(item, owned)}</b>", 18f, TextAlignmentOptions.MidlineLeft);
        SetSize(stock.gameObject, -1f, 48f, flexibleWidth: 1f);
        stock.enableAutoSizing = true;
        stock.fontSizeMin = 14f;
        stock.fontSizeMax = 18f;
        CreateButton("Minus", actions, "-", 42f, () => ChangeQuantity(item, -1)).interactable = selected > 1;
        TMP_Text quantity = CreateText("Quantity", actions, selected.ToString(), 22f, TextAlignmentOptions.Center);
        SetSize(quantity.gameObject, 56f, 48f);
        quantity.enableAutoSizing = true;
        quantity.fontSizeMin = 12f;
        quantity.fontSizeMax = 22f;
        CreateButton("Plus", actions, "+", 42f, () => ChangeQuantity(item, 1)).interactable = selected < owned;
        string proceeds = market != null ? $"판매 ({StackValue(item, selected)})" : "판매";
        UnityEngine.UI.Button sell = CreateButton("Sell", actions, proceeds, 190f, () => Sell(item, GetSelected(item)));
        TMP_Text sellLabel = sell.GetComponentInChildren<TMP_Text>();
        sellLabel.enableAutoSizing = true;
        sellLabel.fontSizeMin = 12f;
        sellLabel.fontSizeMax = 20f;
        CreateButton("SellAll", actions, "전부 판매", 120f, () => Sell(item, inventory.GetQuantity(item)));
    }

    private RectTransform CreateRowLine(string name, Transform parent, float height)
    {
        RectTransform line = CreateRect(name, parent);
        var layout = line.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        SetSize(line.gameObject, -1f, height);
        return line;
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
        panel.sizeDelta = new Vector2(1000f, 0f); // Two-line sell rows keep prices separate from actions.
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

        RectTransform tabs = CreateRect("Tabs", panel);
        HorizontalLayoutGroup tabLayout = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
        tabLayout.spacing = 12f;
        tabLayout.childAlignment = TextAnchor.MiddleCenter;
        tabLayout.childControlWidth = tabLayout.childControlHeight = true;
        tabLayout.childForceExpandWidth = tabLayout.childForceExpandHeight = false;
        SetSize(tabs.gameObject, -1f, 52f);
        sellTabButton = CreateButton("SellTab", tabs, "판매", 180f, () => SelectTab(Tab.Sell));
        buyTabButton = CreateButton("BuyTab", tabs, "구매", 180f, () => SelectTab(Tab.Buy));

        noteText = CreateText("PriceNote", panel, "※ 판매 가격은 임시 테스트 값입니다.", 18f, TextAlignmentOptions.Center);
        noteText.color = MutedColor;
        SetSize(noteText.gameObject, -1f, 26f);

        listViewport = CreateRect("ItemList", panel);
        SetSize(listViewport.gameObject, -1f, 264f);
        itemScroll = listViewport.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        itemScroll.horizontal = false;
        itemScroll.vertical = true;
        itemScroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        itemScroll.scrollSensitivity = 32f;
        RectTransform viewport = CreateRect("Viewport", listViewport);
        Stretch(viewport);
        viewport.gameObject.AddComponent<UnityEngine.UI.Image>().color = Color.white;
        viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
        rowContainer = CreateRect("Rows", viewport);
        rowContainer.anchorMin = new Vector2(0f, 1f);
        rowContainer.anchorMax = Vector2.one;
        rowContainer.pivot = new Vector2(0.5f, 1f);
        rowContainer.sizeDelta = Vector2.zero;
        rowContainer.anchoredPosition = Vector2.zero;
        VerticalLayoutGroup rows = rowContainer.gameObject.AddComponent<VerticalLayoutGroup>();
        rows.spacing = 8f;
        rows.childControlWidth = rows.childControlHeight = true;
        rows.childForceExpandWidth = true;
        rows.childForceExpandHeight = false;
        rowContainer.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit =
            UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        itemScroll.viewport = viewport;
        itemScroll.content = rowContainer;

        detailPanel = CreateRect("MarketDetail", panel);
        SetSize(detailPanel.gameObject, -1f, 156f);
        detailPanel.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0.94f, 0.89f, 0.79f);
        var detailLayout = detailPanel.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        detailLayout.padding = new RectOffset(14, 14, 10, 10);
        detailLayout.spacing = 8f;
        detailLayout.childControlWidth = detailLayout.childControlHeight = true;
        detailLayout.childForceExpandWidth = true;
        detailLayout.childForceExpandHeight = false;
        detailSummary = CreateText("SelectedMarket", detailPanel, "", 19f, TextAlignmentOptions.MidlineLeft);
        SetSize(detailSummary.gameObject, -1f, 74f);
        detailSummary.textWrappingMode = TextWrappingModes.Normal;
        detailSummary.enableAutoSizing = true;
        detailSummary.fontSizeMin = 15f;
        detailSummary.fontSizeMax = 19f;
        detailHistory = CreateText("PriceHistory", detailPanel, "", 18f, TextAlignmentOptions.MidlineLeft);
        detailHistory.color = MutedColor;
        SetSize(detailHistory.gameObject, -1f, 54f);
        detailHistory.textWrappingMode = TextWrappingModes.Normal;
        detailHistory.enableAutoSizing = true;
        detailHistory.fontSizeMin = 14f;
        detailHistory.fontSizeMax = 18f;

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
        developerMarketButton = CreateButton("DevMarket", footer, "시세 갱신 (DEV)", 200f, AdvanceMarketForDeveloper).gameObject;
        developerMarketButton.SetActive(false);
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
        else if (flexibleWidth >= 0f) { element.preferredWidth = 0f; element.minWidth = 0f; }
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
