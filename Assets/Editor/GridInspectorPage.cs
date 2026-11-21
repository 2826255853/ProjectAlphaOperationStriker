using UnityEditor;
using UnityEngine;

// Keep every cell reachable without constructing a GUILayout button for the whole map.
internal static class GridInspectorPage
{
    public static RectInt VisibleCells(int columns, int rows, ref Vector2Int page)
    {
        const int width = 16, height = 12;
        int across = (columns - 1) / width;
        int down = (rows - 1) / height;
        page.x = across == 0 ? 0 : EditorGUILayout.IntSlider("列页", Mathf.Clamp(page.x, 0, across) + 1, 1, across + 1) - 1;
        page.y = down == 0 ? 0 : EditorGUILayout.IntSlider("行页", Mathf.Clamp(page.y, 0, down) + 1, 1, down + 1) - 1;
        int x = page.x * width, y = page.y * height;
        var rect = new RectInt(x, y, Mathf.Min(width, columns - x), Mathf.Min(height, rows - y));
        if (across + down > 0)
            EditorGUILayout.LabelField($"列 {x}–{rect.xMax - 1}，行 {y}–{rect.yMax - 1}");
        return rect;
    }
}
