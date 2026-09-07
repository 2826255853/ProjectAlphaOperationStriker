using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MonsterPathGrid))]
public sealed class MonsterPathGridEditor : Editor
{
    private SerializedProperty columns, rows, cellSize, pathHeight, openCells;

    private void OnEnable()
    {
        columns = serializedObject.FindProperty("columns");
        rows = serializedObject.FindProperty("rows");
        cellSize = serializedObject.FindProperty("cellSize");
        pathHeight = serializedObject.FindProperty("pathHeight");
        openCells = serializedObject.FindProperty("openCells");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(columns, new GUIContent("列数"));
        EditorGUILayout.PropertyField(rows, new GUIContent("行数"));
        EditorGUILayout.PropertyField(cellSize, new GUIContent("格子大小"));
        EditorGUILayout.PropertyField(pathHeight, new GUIContent("路径高度"));
        serializedObject.ApplyModifiedProperties();

        var grid = (MonsterPathGrid)target;
        grid.EnsureCellData();
        serializedObject.Update();
        openCells = serializedObject.FindProperty("openCells");
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("怪物可通行格（蓝色）", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("全部开放")) SetAll(true);
        if (GUILayout.Button("全部关闭")) SetAll(false);
        EditorGUILayout.EndHorizontal();
        for (int y = grid.Rows - 1; y >= 0; y--)
        {
            EditorGUILayout.BeginHorizontal();
            for (int x = 0; x < grid.Columns; x++)
            {
                var cell = openCells.GetArrayElementAtIndex(x + y * grid.Columns);
                GUI.backgroundColor = cell.boolValue ? new Color(0.35f, 0.8f, 1f) : new Color(0.35f, 0.35f, 0.35f);
                if (GUILayout.Button(cell.boolValue ? "O" : ".", GUILayout.Width(24f), GUILayout.Height(22f))) cell.boolValue = !cell.boolValue;
            }
            EditorGUILayout.EndHorizontal();
        }
        GUI.backgroundColor = Color.white;
        serializedObject.ApplyModifiedProperties();
    }

    private void SetAll(bool value)
    {
        serializedObject.Update();
        for (int i = 0; i < openCells.arraySize; i++) openCells.GetArrayElementAtIndex(i).boolValue = value;
        serializedObject.ApplyModifiedProperties();
        SceneView.RepaintAll();
    }
}
