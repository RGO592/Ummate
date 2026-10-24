using TMPro;
using UnityEngine;

// Test HUD: reads the placement controller and grid every frame; never changes game state.
[DisallowMultipleComponent]
public sealed class EcoGridDebugUI : MonoBehaviour
{
    [SerializeField] private EcoPlacementController controller;
    [SerializeField] private EcoGridManager grid;
    [SerializeField] private TMP_Text label;

    private string lastText;

    private void Update()
    {
        if (controller == null || grid == null || label == null) return;
        string text = BuildText();
        if (text == lastText) return;
        lastText = text;
        label.text = text;
    }

    private string BuildText()
    {
        string mode = grid.DevFreePlacement ? "DEV 무료 배치" : "일반 배치";
        string selected = EnvironmentTileTypes.DisplayName(controller.SelectedType);

        string action;
        if (controller.HasPicked)
        {
            EcoGridCell picked = grid.GetCell(controller.PickedCoord);
            action = $"이동 대상: {EnvironmentTileTypes.DisplayName(picked.TileType)} ({controller.PickedCoord.q},{controller.PickedCoord.r})";
        }
        else action = $"배치할 환경 칸: {selected}";

        string hover = "커서: 그리드 밖";
        if (controller.HasHover)
        {
            EcoGridCell cell = grid.GetCell(controller.HoverCoord);
            string content = cell.IsEmpty ? "빈 칸" : EnvironmentTileTypes.DisplayName(cell.TileType);
            hover = $"커서 ({controller.HoverCoord.q},{controller.HoverCoord.r}) {content} → {controller.HoverStatus}";
        }

        return $"[{mode}] {grid.Width}×{grid.Height}\n" +
               $"{action}\n" +
               $"{hover}\n" +
               $"{controller.LastMessage}\n" +
               "슬롯 드래그: 배치 | 환경물 드래그: 이동 | 우클릭·Esc 취소 | X 철거 | Delete 이동 대상 제거";
    }
}
