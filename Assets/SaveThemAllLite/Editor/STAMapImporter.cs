using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SaveThemAll.Lite.Editor
{
    internal static class STAMapImporter
    {
        internal sealed class Binding
        { internal string type, label; internal GameObject prefab; internal STARole role; }

        internal static STAMapDocument Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || System.Text.Encoding.UTF8.GetByteCount(json) > STAMapDocument.MaxBytes)
                throw new ArgumentException("File map trống hoặc lớn hơn 2 MB.");
            json = json.Trim().TrimStart('\uFEFF').Trim();
            if (!json.StartsWith("{", StringComparison.Ordinal) || !json.EndsWith("}", StringComparison.Ordinal))
                throw new ArgumentException("Chọn file JSON xuất từ web (.terrainmap.json). File .js chỉ dùng được khi nội dung là JSON thuần, không có mã JavaScript.");
            STAMapDocument doc;
            try { doc = JsonUtility.FromJson<STAMapDocument>(json); }
            catch (Exception ex) { throw new ArgumentException("Không đọc được JSON: " + ex.Message); }
            if (doc == null) throw new ArgumentException("JSON không có dữ liệu map.");
            doc.Validate(); return doc;
        }
        internal static bool IsPrefab(GameObject prefab)
        {
            return prefab != null && EditorUtility.IsPersistent(prefab) && prefab.transform.parent == null &&
                PrefabUtility.IsPartOfPrefabAsset(prefab) && prefab.GetComponent<STALevel>() == null;
        }
        internal static STARole Role(string type)
        {
            switch (type)
            {
                case "spawn": return STARole.Spawn;
                case "exit": return STARole.Exit;
                case "cage": return STARole.Cage;
                case "spikes": return STARole.Spikes;
                case "stairs": return STARole.Stairs;
                case "bridge": return STARole.Bridge;
                case "fan": return STARole.Fan;
                case "stone": return STARole.Stone;
                case "portal": return STARole.Teleport;
                case "lava": return STARole.Lava;
                case "key": return STARole.Key;
                default: return STARole.Prop;
            }
        }
        internal static List<Binding> Link(STAMapDocument doc, STAPalette palette)
        {
            var result = new List<Binding>();
            foreach (string type in doc.RequiredTypes())
            {
                STAMapDocument.AssetDefinition asset = doc.Asset(type);
                var binding = new Binding { type = type, label = asset != null ? asset.label : type, role = Role(asset != null ? asset.role : type) };
                if (palette != null && palette.importBindings != null)
                    foreach (STAPalette.ImportBinding saved in palette.importBindings)
                        if (saved != null && saved.type == type && IsPrefab(saved.prefab))
                        { binding.prefab = saved.prefab; binding.role = saved.role; break; }
                if (binding.prefab == null) binding.prefab = Resolve(binding, asset, palette);
                result.Add(binding);
            }
            return result;
        }
        private static GameObject Resolve(Binding binding, STAMapDocument.AssetDefinition asset, STAPalette palette)
        {
            bool matched;
            GameObject found;
            if (STAMapDocument.IsPrefabId(binding.type))
            {
                string guid = binding.type.Substring(7);
                found = Unique(palette, e => e.sourceGuid == guid || AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(e.prefab)) == guid, out matched);
                if (matched) return found;
                found = RegularPrefabAt(AssetDatabase.GUIDToAssetPath(guid));
                if (found != null) return found;
            }
            if (asset != null && !string.IsNullOrEmpty(asset.sourcePath))
            {
                string sourceGuid = AssetDatabase.AssetPathToGUID(asset.sourcePath);
                found = Unique(palette, e => (!string.IsNullOrEmpty(sourceGuid) && e.sourceGuid == sourceGuid) || AssetDatabase.GetAssetPath(e.prefab) == asset.sourcePath, out matched);
                if (matched) return found;
                found = RegularPrefabAt(asset.sourcePath);
                if (found != null) return found;
            }
            string name = CleanName(binding.label);
            found = Unique(palette, e => CleanName(e.label) == name || CleanName(e.prefab.name) == name, out matched);
            if (matched) return found;
            // A custom prefab ID denotes a particular visual: never substitute another skin by role.
            if (asset != null) return null;
            return Unique(palette, e => e.role == binding.role, out matched);
        }
        private static string CleanName(string name)
        {
            name = name ?? "";
            if (name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                name = Path.GetFileNameWithoutExtension(name);
            int suffix = name.LastIndexOf("__", StringComparison.Ordinal);
            if (suffix >= 0 && name.Length - suffix == 34 && STAMapDocument.IsPrefabId("prefab_" + name.Substring(suffix + 2)))
                name = name.Substring(0, suffix);
            return name.Trim().ToLowerInvariant();
        }
        private static GameObject RegularPrefabAt(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return null;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return IsPrefab(prefab) ? prefab : null;
        }
        private static GameObject Unique(STAPalette palette, Func<STAPalette.Entry, bool> match, out bool matched)
        {
            matched = false; GameObject found = null;
            if (palette == null || palette.entries == null) return null;
            foreach (STAPalette.Entry entry in palette.entries)
            {
                if (entry == null || entry.role == STARole.Terrain || !IsPrefab(entry.prefab) || !match(entry)) continue;
                matched = true;
                if (found != null && found != entry.prefab) return null;
                found = entry.prefab;
            }
            return found;
        }
        internal static List<string> MissingTerrain(STAMapDocument doc, STAPalette palette)
        { return doc.MissingTerrain(key => palette != null && IsPrefab(palette.Terrain(key))); }

        internal static STALevel Build(STAMapDocument doc, STAPalette palette, List<Binding> bindings,
            string fileName, float objectDepth, STACollider collider, STALevel previous)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Thoát Play Mode trước khi import.");
            if (!SceneManager.GetActiveScene().IsValid() || !SceneManager.GetActiveScene().isLoaded || EditorSceneManager.IsPreviewScene(SceneManager.GetActiveScene()))
                throw new InvalidOperationException("Mở scene chính và thoát chế độ sửa prefab trước khi dựng map.");
            if (float.IsNaN(objectDepth) || float.IsInfinity(objectDepth)) throw new ArgumentException("Chiều sâu Z không hợp lệ.");
            doc.Validate();
            List<string> missing = MissingTerrain(doc, palette);
            if (missing.Count > 0) throw new InvalidOperationException("Thiếu terrain: " + string.Join(", ", missing.ToArray()));
            var linked = new Dictionary<string, Binding>(StringComparer.Ordinal);
            foreach (Binding item in bindings)
            {
                if (!IsPrefab(item.prefab)) throw new InvalidOperationException("Chưa gán prefab hợp lệ cho: " + item.label);
                linked.Add(item.type, item);
            }
            foreach (string type in doc.RequiredTypes())
                if (!linked.ContainsKey(type)) throw new InvalidOperationException("Thiếu liên kết: " + type);

            GameObject root = null; int undoGroup = -1;
            try
            {
                // Build under an inactive root. No existing map or Undo state is touched until complete.
                root = new GameObject(STAEditorUtility.SafeName(doc.name)); root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, SceneManager.GetActiveScene());
                STALevel map = root.AddComponent<STALevel>();
                map.levelName = doc.name; map.columns = doc.width; map.rows = doc.height; map.cellSize = doc.cellSize;
                map.palette = palette; map.terrainCollider = collider; map.importedFile = fileName; map.cells = doc.Cells();
                map.terrainRoot = Child(root.transform, "Terrain"); map.objectRoot = Child(root.transform, "Objects");
                var overrides = doc.Overrides();
                foreach (var item in overrides) map.terrainOverrides.Add(new STALevel.TerrainOverride { cell = item.Key, key = item.Value });
                var occupied = new HashSet<STACell>(map.cells);
                Func<STACell, string> overrideAt = p => { string key; return overrides.TryGetValue(p, out key) ? key : null; };
                foreach (STACell cell in map.cells)
                    STAMapActions.CreateTile(map, cell, STAGrid.Key(occupied, cell, overrideAt), map.terrainRoot);
                foreach (STAMapDocument.ObjectRecord record in doc.objects)
                {
                    Binding binding = linked[record.type];
                    Transform holder = Child(map.objectRoot, binding.label + " [" + record.id + "]");
                    holder.localPosition = new Vector3(record.CenterX, record.CenterY, objectDepth) * doc.cellSize;
                    holder.localRotation = Quaternion.Euler(0, 0, record.rotation);
                    holder.localScale = Vector3.one * doc.cellSize;
                    GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(binding.prefab, holder);
                    if (visual == null) throw new InvalidOperationException("Không tạo được object: " + record.id);
                    STAObject obj = holder.gameObject.AddComponent<STAObject>();
                    obj.sourcePrefab = binding.prefab; obj.role = binding.role; obj.footprint = new Vector2(record.width, record.height);
                    obj.count = record.count; obj.pairId = record.pairId ?? ""; obj.importedId = record.id; obj.importedType = record.type;
                    obj.ascendsRight = binding.label.IndexOf("left", StringComparison.OrdinalIgnoreCase) < 0;
                }
                Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Dựng map từ JSON");
                root.SetActive(true); Undo.RegisterCreatedObjectUndo(root, "Dựng map từ JSON");
                if (STAEditorUtility.SceneMap(previous))
                { Undo.RecordObject(previous.gameObject, "Ẩn level trước"); previous.gameObject.SetActive(false); STAEditorUtility.Dirty(previous); }
                RememberBindings(palette, bindings);
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(undoGroup);
                STAEditorUtility.Dirty(map); return map;
            }
            catch
            {
                if (undoGroup >= 0) Undo.RevertAllDownToGroup(undoGroup);
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                throw;
            }
        }
        private static Transform Child(Transform parent, string name)
        { var child = new GameObject(name); child.transform.SetParent(parent, false); return child.transform; }
        private static void RememberBindings(STAPalette palette, List<Binding> bindings)
        {
            if (palette == null) return;
            Undo.RecordObject(palette, "Nhớ liên kết JSON");
            if (palette.importBindings == null) palette.importBindings = new List<STAPalette.ImportBinding>();
            foreach (Binding binding in bindings)
            {
                STAPalette.ImportBinding saved = palette.importBindings.Find(item => item != null && item.type == binding.type);
                if (saved == null) { saved = new STAPalette.ImportBinding { type = binding.type }; palette.importBindings.Add(saved); }
                saved.prefab = binding.prefab; saved.role = binding.role;
            }
            EditorUtility.SetDirty(palette);
        }
    }
}
