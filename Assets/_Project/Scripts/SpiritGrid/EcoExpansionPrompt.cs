using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// A prototype confirmation flow only. It never touches cells, currency, saves, or unlock state.
[DisallowMultipleComponent]
public sealed class EcoExpansionPrompt : MonoBehaviour
{
    [SerializeField] private RectTransform canvasRoot;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Sprite panelSprite;
    private GameObject modal;
    private TMP_Text message;
    private UnityEngine.UI.Button openButton;
    private UnityEngine.UI.Button cancelButton;
    private int closedFrame = -1;
    public bool IsOpen => modal != null && modal.activeSelf;
    public bool InputBlocked => IsOpen || Time.frameCount == closedFrame;

    private void Awake()
    {
        RectTransform backdrop = Rect("ExpansionConfirmation", canvasRoot, Vector2.zero, Vector2.zero);
        backdrop.anchorMin = Vector2.zero;
        backdrop.anchorMax = Vector2.one;
        backdrop.offsetMin = backdrop.offsetMax = Vector2.zero;
        modal = backdrop.gameObject;
        var shade = modal.AddComponent<UnityEngine.UI.Image>();
        shade.color = new Color(0, 0, 0, 0.55f);
        // A full-screen raycast target prevents clicks passing through to build controls.
        shade.raycastTarget = true;
        RectTransform panel = Rect("Panel", backdrop, new Vector2(440, 300), Vector2.zero);
        var image = panel.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite = panelSprite;
        image.type = UnityEngine.UI.Image.Type.Sliced;
        image.color = new Color(1f, 0.96f, 0.87f);
        Label("Title", panel, "새로운 영역 개방", new Vector2(390, 40), new Vector2(0, 108), 26);
        message = Label("Message", panel, "", new Vector2(390, 142), new Vector2(0, 14), 22);
        openButton = Button("OpenButton", panel, "개방하기", new Vector2(-96, -108));
        cancelButton = Button("CancelButton", panel, "취소", new Vector2(96, -108));
        openButton.onClick.AddListener(Confirm);
        cancelButton.onClick.AddListener(Close);
        modal.SetActive(false);
    }

    public void Open()
    {
        message.text = "필요 자원\n골드 1000\n나무 20\n돌 10";
        openButton.interactable = true;
        modal.transform.SetAsLastSibling();
        modal.SetActive(true);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(cancelButton.gameObject);
    }

    private void Confirm()
    {
        message.text = "현재 버전에서는 아직\n개방할 수 없습니다.";
        openButton.interactable = false;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(cancelButton.gameObject);
    }

    public void Close()
    {
        if (!IsOpen) return;
        modal.SetActive(false);
        closedFrame = Time.frameCount;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private TMP_Text Label(string name, Transform parent, string text, Vector2 size, Vector2 pos, int fontSize)
    {
        RectTransform rect = Rect(name, parent, size, pos);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.text = text;
        label.fontSize = fontSize;
        label.color = new Color(0.2f, 0.23f, 0.17f);
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }

    private UnityEngine.UI.Button Button(string name, Transform parent, string title, Vector2 pos)
    {
        RectTransform rect = Rect(name, parent, new Vector2(166, 46), pos);
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite = panelSprite;
        image.type = UnityEngine.UI.Image.Type.Sliced;
        image.color = new Color(0.7f, 0.83f, 0.6f);
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        Label("Label", rect, title, new Vector2(148, 40), Vector2.zero, 22);
        return button;
    }

    private void OnDestroy() { if (modal != null) Destroy(modal); }
}
