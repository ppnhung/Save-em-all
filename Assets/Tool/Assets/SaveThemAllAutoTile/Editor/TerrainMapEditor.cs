using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SaveThemAll.AutoTile.Editor
{
    [CustomEditor(typeof(TerrainMap))]
    public sealed class TerrainMapEditor : UnityEditor.Editor
    {
        private bool painting;
        private bool erase;
        private bool grid = true;
        private int brush;
        private Vector2Int lastCell = new Vector2Int(int.MinValue, int.MinValue);
        private int undoGroup = -1;
        private TerrainMap Map { get { return (TerrainMap)target; } }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            DrawPropertiesExcluding(serializedObject, "m_Script", "cells", "visualRoot", "collisionRoot");
            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();
            if (changed) { Map.Rebuild(); MarkChanged(); }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Terrain Painter", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Select this map. Enable Paint, then drag in Scene view. Shift + left mouse erases. Alt keeps Scene navigation. Ctrl/Cmd+Z undoes a stroke.", MessageType.Info);
            painting = GUILayout.Toggle(painting, painting ? "PAINT IS ON" : "Enable Paint", "Button");
            erase = EditorGUILayout.Toggle("Erase brush", erase);
            grid = EditorGUILayout.Toggle("Show grid", grid);
            List<string> names = BrushNames();
            brush = Mathf.Clamp(brush, 0, names.Count - 1);
            brush = EditorGUILayout.Popup("Model", brush, names.ToArray());
            EditorGUILayout.LabelField("Occupied cells", Map.CellCount.ToString());
            EditorGUILayout.HelpBox("Automatic = rules. Choosing a model pins only its appearance; it still counts as terrain. Repaint with Automatic to remove that override.", MessageType.None);
            if (GUILayout.Button("Rebuild Terrain")) { Map.Rebuild(); MarkChanged(); }
            if (GUILayout.Button("Frame Map (front view)")) FrameMap(Map);
            if (Map.catalog == null)
                EditorGUILayout.HelpBox("Run Tools > Save Them All > Setup Kit + Create Demo first.", MessageType.Warning);
        }

        private List<string> BrushNames()
        {
            List<string> names = new List<string> { "Automatic" };
            if (Map.catalog != null && Map.catalog.entries != null)
                foreach (TerrainCatalog.Entry e in Map.catalog.entries)
                    if (e != null && !string.IsNullOrEmpty(e.model) && !e.model.Contains("Background")) names.Add(e.model);
            return names;
        }

        private void OnSceneGUI()
        {
            if (Map == null) return;
            Matrix4x4 oldMatrix = Handles.matrix;
            Handles.matrix = Map.transform.localToWorldMatrix;
            if (grid)
            {
                Handles.color = new Color(0.55f, 0.85f, 1, 0.27f);
                float size = Map.cellSize;
                for (int x = 0; x <= Map.width; x++)
                    Handles.DrawLine(new Vector3((x - .5f) * size, -.5f * size, -.65f * size),
                        new Vector3((x - .5f) * size, (Map.height - .5f) * size, -.65f * size));
                for (int y = 0; y <= Map.height; y++)
                    Handles.DrawLine(new Vector3(-.5f * size, (y - .5f) * size, -.65f * size),
                        new Vector3((Map.width - .5f) * size, (y - .5f) * size, -.65f * size));
            }
            Handles.matrix = oldMatrix;
            if (!painting || Map.catalog == null) return;
            Event ev = Event.current;
            if (ev.alt) return;
            if (ev.type == EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            Ray ray = HandleUtility.GUIPointToWorldRay(ev.mousePosition);
            Plane plane = new Plane(Map.transform.forward,
                Map.transform.TransformPoint(new Vector3(0, 0, -.65f * Map.cellSize)));
            float distance;
            if (!plane.Raycast(ray, out distance)) return;
            Vector2Int cell = Map.WorldToCell(ray.GetPoint(distance));
            bool removing = erase || ev.shift;
            if (Map.InBounds(cell))
            {
                Handles.matrix = Map.transform.localToWorldMatrix;
                Handles.color = removing ? new Color(1, .35f, .3f, .9f) : new Color(.3f, 1, .65f, .9f);
                Handles.DrawWireCube(new Vector3(cell.x * Map.cellSize, cell.y * Map.cellSize, -.66f * Map.cellSize),
                    new Vector3(Map.cellSize, Map.cellSize, 0));
                Handles.matrix = oldMatrix;
                string model = Map.ModelAt(cell);
                Handles.Label(Map.CellToWorld(cell) + Map.transform.up * Map.cellSize * .65f,
                    "(" + cell.x + ", " + cell.y + ")  " + (model ?? "Empty"));
            }
            if (ev.type == EventType.MouseUp && ev.button == 0)
            {
                if (undoGroup >= 0) Undo.CollapseUndoOperations(undoGroup);
                undoGroup = -1;
                lastCell = new Vector2Int(int.MinValue, int.MinValue);
                ev.Use();
            }
            if ((ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag) && ev.button == 0 && Map.InBounds(cell))
            {
                if (ev.type == EventType.MouseDown)
                {
                    Undo.IncrementCurrentGroup();
                    undoGroup = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("Paint Terrain");
                    lastCell = new Vector2Int(int.MinValue, int.MinValue);
                }
                if (cell != lastCell)
                {
                    List<string> names = BrushNames();
                    brush = Mathf.Clamp(brush, 0, names.Count - 1);
                    Undo.RecordObject(Map, "Paint Terrain");
                    bool dirty = false;
                    Vector2Int start = lastCell.x == int.MinValue ? cell : lastCell;
                    int steps = Mathf.Max(Mathf.Abs(cell.x - start.x), Mathf.Abs(cell.y - start.y));
                    for (int step = 0; step <= steps; step++)
                    {
                        float t = steps == 0 ? 1f : (float)step / steps;
                        Vector2Int p = new Vector2Int(Mathf.RoundToInt(Mathf.Lerp(start.x, cell.x, t)),
                            Mathf.RoundToInt(Mathf.Lerp(start.y, cell.y, t)));
                        dirty |= Map.SetCell(p, !removing, brush == 0 ? null : names[brush]);
                    }
                    if (dirty) MarkChanged();
                    lastCell = cell;
                }
                ev.Use();
            }
            if (ev.type == EventType.MouseMove || ev.type == EventType.MouseDrag) SceneView.RepaintAll();
        }

        private void MarkChanged()
        {
            EditorUtility.SetDirty(Map);
            if (Map.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(Map.gameObject.scene);
        }

        public static void FrameMap(TerrainMap map)
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return;
            Vector3 center = map.transform.TransformPoint(new Vector3((map.width - 1) * .5f * map.cellSize,
                (map.height - 1) * .5f * map.cellSize, 0));
            view.LookAt(center, map.transform.rotation, Mathf.Max(map.width, map.height) * map.cellSize * .7f, true);
        }
    }

    [InitializeOnLoad]
    internal static class TerrainUndoRefresh
    {
        static TerrainUndoRefresh() { Undo.undoRedoPerformed += RebuildLoadedMaps; }
        private static void RebuildLoadedMaps()
        {
            if (Application.isPlaying) return;
            foreach (TerrainMap map in Object.FindObjectsOfType<TerrainMap>(true))
                if (!EditorUtility.IsPersistent(map) && map.gameObject.scene.IsValid()) map.Rebuild();
            SceneView.RepaintAll();
        }
    }
}
