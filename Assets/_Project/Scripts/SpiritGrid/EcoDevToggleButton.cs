using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Top-right DEV button (same idea as the farm DEV button): ON = free placement while it stays on.
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class EcoDevToggleButton : MonoBehaviour
{
    [SerializeField] private Image background;
    [SerializeField] private TMP_Text label;
    [SerializeField] private Color offColor = new Color(0.55f, 0.55f, 0.55f);
    [SerializeField] private Color onColor = new Color(1f, 0.62f, 0.2f);

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(EcoDevMode.ToggleFreePlacement);
    }

    private void OnEnable()
    {
        EcoDevMode.Changed += Refresh;
        Refresh();
    }

    private void OnDisable() => EcoDevMode.Changed -= Refresh;

    private void Refresh()
    {
        bool on = EcoDevMode.FreePlacement;
        if (background != null) background.color = on ? onColor : offColor;
        if (label != null) label.text = on ? "DEV\nON" : "DEV";
    }
}
