using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SaveThemAll.Lite.Editor
{
    internal static class STAMapActions
    {
        internal static STALevel Create(string name, int columns, int rows, STAPalette palette)
        {
            GameObject root = new GameObject(STAEditorUtility.SafeName(name));
            STALevel map = root.AddComponent<STALevel>();
            map.levelName = root.name; map.columns = Mathf.Clamp(columns, 1, 128); map.rows = Mathf.Clamp(rows, 1, 128); map.palette = palette;
            map.terrainRoot = Child(root.transform, "Terrain"); map.objectRoot = Child(root.transform, "Objects");
            Undo.RegisterCreatedObjectUndo(root, "Tạo level"); Selection.activeGameObject = root;
            STAEditorUtility.Dirty(map); Frame(map); return map;
        }
        private static Transform Child(Transform parent, string name)
        { var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform; }
        private static void Roots(STALevel map)
        {
            Undo.RecordObject(map, "Thiết lập nhóm map");
            if (map.terrainRoot == null)
            { map.terrainRoot = Child(map.transform, "Terrain"); Undo.RegisterCreatedObjectUndo(map.terrainRoot.gameObject, "Tạo nhóm terrain"); }
            if (map.objectRoot == null)
            { map.objectRoot = Child(map.transform, "Objects"); Undo.RegisterCreatedObjectUndo(map.objectRoot.gameObject, "Tạo nhóm object"); }
        }
        internal static void Frame(STALevel map)
        {
            SceneView view = SceneView.lastActiveSceneView; if (view == null || map == null) return;
            Vector3 center = map.transform.TransformPoint(new Vector3((map.columns - 1) * .5f, (map.rows - 1) * .5f, 0) * map.cellSize);
            view.LookAt(center, map.transform.rotation, Mathf.Max(map.rows, map.columns) * map.cellSize * .7f, true);
        }
        internal static void Paint(STALevel map, STACell cell, bool solid)
        {
            STAEditorUtility.RequireMap(map);
            if (map.palette == null) throw new InvalidOperationException("Tạo prefab trước hoặc gán Palette của bộ asset.");
            STAGrid.EditPlan plan = STAGrid.Plan(map.cells, cell, solid, map.columns, map.rows, k => map.palette.Terrain(k) != null, map.OverrideAt);
            if (!plan.changed) return;
            if (plan.missing.Count > 0) throw new InvalidOperationException("Chưa đổi map: thiếu hoặc trùng liên kết terrain " + string.Join(", ", plan.missing.ToArray()) + ". Kiểm tra mã khối trong danh sách asset.");
            Replace(map, plan.cells, plan.affected);
        }
        internal static void Rebuild(STALevel map)
        {
            STAEditorUtility.RequireMap(map);
            if (map.palette == null && map.cells.Count > 0) throw new InvalidOperationException("Chưa có Palette.");
            var missing = STAGrid.Missing(map.cells, k => map.palette != null && map.palette.Terrain(k) != null, map.OverrideAt);
            if (missing.Count > 0) throw new InvalidOperationException("Giữ terrain cũ. Thiếu: " + string.Join(", ", missing.ToArray()));
            var affected = new HashSet<STACell>(map.cells);
            if (map.terrainRoot != null) foreach (STATile tile in map.terrainRoot.GetComponentsInChildren<STATile>(true)) affected.Add(tile.cell);
            Replace(map, new List<STACell>(map.cells), new List<STACell>(affected));
        }
        private static void Replace(STALevel map, List<STACell> cells, List<STACell> affected)
        {
            // Stage every replacement first. A missing/invalid prefab never destroys the previous terrain.
            var occupied = new HashSet<STACell>(cells);
            var changed = new HashSet<STACell>(affected);
            GameObject stage = new GameObject("STA temporary terrain");
            SceneManager.MoveGameObjectToScene(stage, map.gameObject.scene); stage.SetActive(false);
            var replacements = new List<GameObject>();
            try
            {
                foreach (STACell p in affected)
                {
                    if (!occupied.Contains(p)) continue;
                    string key = STAGrid.Key(occupied, p, map.OverrideAt);
                    replacements.Add(CreateTile(map, p, key, stage.transform));
                }
                Roots(map);
                Undo.RecordObject(map, "Sửa terrain"); map.cells = cells;
                if (map.terrainOverrides != null) map.terrainOverrides.RemoveAll(item => item == null || !occupied.Contains(item.cell));
                foreach (STATile old in map.terrainRoot.GetComponentsInChildren<STATile>(true))
                    if (old.transform.parent == map.terrainRoot && changed.Contains(old.cell)) Undo.DestroyObjectImmediate(old.gameObject);
                foreach (GameObject go in replacements)
                { go.transform.SetParent(map.terrainRoot, false); Undo.RegisterCreatedObjectUndo(go, "Tạo khối terrain"); }
                STAEditorUtility.Dirty(map);
            }
            finally { UnityEngine.Object.DestroyImmediate(stage); }
        }
        internal static GameObject CreateTile(STALevel map, STACell p, string key, Transform parent)
        {
            GameObject prefab = map.palette != null ? map.palette.Terrain(key) : null;
            if (prefab == null) throw new InvalidOperationException("Thiếu prefab: " + key);
            GameObject tile = new GameObject("Tile " + p.x + "," + p.y);
            tile.transform.SetParent(parent, false);
            tile.transform.localPosition = new Vector3(p.x, p.y, 0) * map.cellSize;
            tile.transform.localScale = Vector3.one * map.cellSize;
            STATile data = tile.AddComponent<STATile>(); data.cell = p; data.modelKey = key;
            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, tile.transform);
            if (visual == null) throw new InvalidOperationException("Không tạo được prefab: " + key);
            visual.transform.SetParent(tile.transform, false);
            foreach (Collider c in visual.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (Collider2D c in visual.GetComponentsInChildren<Collider2D>(true)) c.enabled = false;
            Vector3 size = new Vector3(1, map.collisionTop + .5f, map.collisionDepth);
            Vector3 center = new Vector3(0, (map.collisionTop - .5f) * .5f, 0);
            if (map.terrainCollider == STACollider.Box3D || map.terrainCollider == STACollider.Trigger3D)
            { BoxCollider box = tile.AddComponent<BoxCollider>(); box.size = size; box.center = center; box.isTrigger = map.terrainCollider == STACollider.Trigger3D; }
            else if (map.terrainCollider == STACollider.Box2D || map.terrainCollider == STACollider.Trigger2D)
            { BoxCollider2D box = tile.AddComponent<BoxCollider2D>(); box.size = new Vector2(size.x, size.y); box.offset = new Vector2(center.x, center.y); box.isTrigger = map.terrainCollider == STACollider.Trigger2D; }
            return tile;
        }
        internal static STAObject Place(STALevel map, STAPalette.Entry entry, STACell cell)
        {
            STAEditorUtility.RequireMap(map);
            if (entry == null || entry.prefab == null || !PrefabUtility.IsPartOfPrefabAsset(entry.prefab)) throw new InvalidOperationException("Chọn prefab hợp lệ trong danh sách asset.");
            if (!STAGrid.Inside(cell, map.columns, map.rows)) return null;
            Roots(map);
            GameObject wrapper = new GameObject(entry.label);
            wrapper.transform.SetParent(map.objectRoot, false);
            wrapper.transform.localPosition = new Vector3(cell.x, cell.y, -.65f) * map.cellSize;
            wrapper.transform.localScale = Vector3.one * map.cellSize;
            try
            {
                GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(entry.prefab, map.gameObject.scene);
                if (model == null) throw new InvalidOperationException("Không tạo được prefab object.");
                model.transform.SetParent(wrapper.transform, false);
                STAObject obj = wrapper.AddComponent<STAObject>(); obj.sourcePrefab = entry.prefab; obj.role = entry.role;
                obj.ascendsRight = entry.label.IndexOf("left", StringComparison.OrdinalIgnoreCase) < 0;
                Undo.RegisterCreatedObjectUndo(wrapper, "Đặt object"); Selection.activeGameObject = wrapper; STAEditorUtility.Dirty(map); return obj;
            }
            catch { UnityEngine.Object.DestroyImmediate(wrapper); throw; }
        }
        internal static void Duplicate(STAObject obj)
        {
            STALevel map = obj.GetComponentInParent<STALevel>(); STAEditorUtility.RequireMap(map);
            GameObject clone = UnityEngine.Object.Instantiate(obj.gameObject, obj.transform.parent);
            clone.name = obj.name + " Copy"; clone.transform.localPosition += Vector3.right * map.cellSize;
            Undo.RegisterCreatedObjectUndo(clone, "Nhân bản object"); Selection.activeGameObject = clone; STAEditorUtility.Dirty(map);
        }
        internal static void Fit(STAObject obj)
        {
            STALevel map = obj.GetComponentInParent<STALevel>(); STAEditorUtility.RequireMap(map);
            Bounds b;
            if (!STAEditorUtility.BoundsIn(obj.transform, map.transform, out b) || b.size.x < .0001f || b.size.y < .0001f) throw new InvalidOperationException("Không có mesh để căn kích thước.");
            float factor = Mathf.Min(Mathf.Max(.1f,obj.footprint.x) * map.cellSize / b.size.x, Mathf.Max(.1f,obj.footprint.y) * map.cellSize / b.size.y);
            Undo.RecordObject(obj.transform,"Căn object vừa vùng ô"); obj.transform.localScale *= factor; STAEditorUtility.Dirty(obj.transform);
        }
        internal static void Ground(STAObject obj)
        {
            STALevel map = obj.GetComponentInParent<STALevel>(); STAEditorUtility.RequireMap(map);
            Bounds b; if (!STAEditorUtility.BoundsIn(obj.transform,map.transform,out b)) throw new InvalidOperationException("Không có mesh để căn đáy.");
            float floor = float.NegativeInfinity;
            foreach (STACell cell in map.cells)
            {
                float left = (cell.x - .5f) * map.cellSize, right = (cell.x + .5f) * map.cellSize;
                float top = (cell.y + map.collisionTop) * map.cellSize;
                if (right > b.min.x + .001f && left < b.max.x - .001f && top <= b.min.y + .001f) floor = Mathf.Max(floor, top);
            }
            if (float.IsNegativeInfinity(floor)) throw new InvalidOperationException("Không có terrain bên dưới object. Đưa object lên trên mặt sàn rồi căn lại.");
            Undo.RecordObject(obj.transform,"Đặt đáy object chạm sàn");
            obj.transform.position += map.transform.TransformVector(Vector3.up * (floor - b.min.y)); STAEditorUtility.Dirty(obj.transform);
        }
        internal static void UpdateCollider(STAObject obj)
        {
            STAEditorUtility.RequireMap(obj.GetComponentInParent<STALevel>());
            Bounds b; if (!STAEditorUtility.BoundsIn(obj.transform,obj.transform,out b)) throw new InvalidOperationException("Không có mesh để tạo collider.");
            Undo.RecordObject(obj,"Cập nhật collider");
            bool three = obj.colliderMode == STACollider.Box3D || obj.colliderMode == STACollider.Trigger3D;
            bool two = obj.colliderMode == STACollider.Box2D || obj.colliderMode == STACollider.Trigger2D;
            if (!three && obj.authoredCollider3D != null) { Undo.DestroyObjectImmediate(obj.authoredCollider3D); obj.authoredCollider3D = null; }
            if (!two && obj.authoredCollider2D != null) { Undo.DestroyObjectImmediate(obj.authoredCollider2D); obj.authoredCollider2D = null; }
            if (three)
            {
                if (obj.authoredCollider3D == null) obj.authoredCollider3D = Undo.AddComponent<BoxCollider>(obj.gameObject);
                Undo.RecordObject(obj.authoredCollider3D,"Chỉnh Box Collider");
                obj.authoredCollider3D.center = b.center;
                obj.authoredCollider3D.size = new Vector3(Mathf.Max(.01f,b.size.x),Mathf.Max(.01f,b.size.y),Mathf.Max(.01f,b.size.z));
                obj.authoredCollider3D.isTrigger = obj.colliderMode == STACollider.Trigger3D;
            }
            if (two)
            {
                if (obj.authoredCollider2D == null) obj.authoredCollider2D = Undo.AddComponent<BoxCollider2D>(obj.gameObject);
                Undo.RecordObject(obj.authoredCollider2D,"Chỉnh Box Collider 2D");
                obj.authoredCollider2D.offset = new Vector2(b.center.x,b.center.y);
                obj.authoredCollider2D.size = new Vector2(Mathf.Max(.01f,b.size.x),Mathf.Max(.01f,b.size.y));
                obj.authoredCollider2D.isTrigger = obj.colliderMode == STACollider.Trigger2D;
            }
            STAEditorUtility.Dirty(obj);
        }
    }
}
