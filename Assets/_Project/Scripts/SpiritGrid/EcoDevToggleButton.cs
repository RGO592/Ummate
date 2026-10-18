using TMPro;
using UnityEngine;

// Top-right DEV button (same idea as the farm DEV button): ON = free placement while it stays on.
[DisallowMultipleComponent]
[RequireComponent(typeof(UnityEngine.UI.Button))]
public sealed class EcoDevToggleButton : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.Image background;
    [SerializeField] private TMP_Text label;
    [SerializeField] private Color offColor = Color.white;
    [SerializeField] private Color onColor = Color.white;

    private UnityEngine.UI.Button button;

    private void Awake()
    {
        // Each visit starts OFF; only this scene's DEV button enables free placement.
        EcoDevMode.SetFreePlacement(false);
        button = GetComponent<UnityEngine.UI.Button>();
        button.onClick.AddListener(EcoDevMode.ToggleFreePlacement);
    }

    private void OnEnable()
    {
        EcoDevMode.Changed += Refresh;
        Refresh();
    }

    private void OnDisable() => EcoDevMode.Changed -= Refresh;

    private void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(EcoDevMode.ToggleFreePlacement);
    }

    private void Refresh()
    {
        bool on = EcoDevMode.FreePlacement;
        if (background != null) background.color = on ? onColor : offColor;
        if (label != null) label.text = on ? "DEV\nON" : "DEV";
    }
}
