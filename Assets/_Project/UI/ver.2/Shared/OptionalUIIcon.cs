using UnityEngine;

// An empty Image must not render a white square. The label is a temporary fallback.
[DisallowMultipleComponent]
public sealed class OptionalUIIcon : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.Image icon;
    [SerializeField] private GameObject fallbackLabel;
    private Sprite shownSprite;
    private void OnEnable() => Refresh();
    private void LateUpdate()
    {
        if (icon != null && icon.sprite != shownSprite) Refresh();
    }
    private void Refresh()
    {
        shownSprite = icon != null ? icon.sprite : null;
        if (icon != null) icon.enabled = shownSprite != null;
        if (fallbackLabel != null) fallbackLabel.SetActive(shownSprite == null);
    }
}
