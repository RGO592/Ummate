using UnityEngine;

// Lives on the always-active MissionUI parent, not on the panel it hides.
[DisallowMultipleComponent]
public sealed class CollapsibleMissionUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private UnityEngine.UI.Button closeButton;
    [SerializeField] private UnityEngine.UI.Button reopenButton;
    [SerializeField] private bool startExpanded = true;

    private void Awake() => SetExpanded(startExpanded);
    private void OnEnable()
    {
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (reopenButton != null) reopenButton.onClick.AddListener(Open);
    }
    private void OnDisable()
    {
        if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        if (reopenButton != null) reopenButton.onClick.RemoveListener(Open);
    }
    public void Close() => SetExpanded(false);
    public void Open() => SetExpanded(true);
    private void SetExpanded(bool expanded)
    {
        if (panel != null) panel.SetActive(expanded);
        if (reopenButton != null) reopenButton.gameObject.SetActive(!expanded);
    }
}
