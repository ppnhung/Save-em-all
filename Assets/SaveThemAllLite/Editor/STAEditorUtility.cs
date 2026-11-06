using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SaveThemAll.Lite.Editor
{
    internal static class STAEditorUtility
    {
        internal const string Content = "Assets/STA_Content";
        internal static void Folder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (path != "Assets" && !path.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("Thư mục phải nằm trong Assets.");
            string[] parts = path.Split('/'); string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                if (parts[i] == ".." || parts[i] == "." || string.IsNullOrEmpty(parts[i])) throw new InvalidOperationException("Đường dẫn không hợp lệ.");
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
        internal static string SafeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            foreach (char c in "<>:\"/\\|?*") name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "Level" : name.Trim().TrimEnd('.');
        }
        internal static void Dirty(UnityEngine.Object target)
        {
            EditorUtility.SetDirty(target);
            if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            Component c = target as Component;
            if (c != null && c.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        }
        internal static bool SceneMap(STALevel map)
        {
            return map != null && !EditorUtility.IsPersistent(map) && map.gameObject.scene.IsValid()
                && !EditorSceneManager.IsPreviewScene(map.gameObject.scene);
        }
        internal static void RequireMap(STALevel map)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Thoát Play Mode trước khi sửa map.");
            if (!SceneMap(map)) throw new InvalidOperationException("Chọn level trong Hierarchy của scene, không chọn prefab asset.");
            if (PrefabUtility.IsPartOfPrefabInstance(map.gameObject))
                throw new InvalidOperationException("Dùng Mở level trong tool để mở bản sửa độc lập, hoặc Unpack root level trước khi vẽ.");
            if (map.cellSize <= 0) throw new InvalidOperationException("Kích thước ô phải lớn hơn 0.");
        }
        internal static bool BodyMesh(MeshFilter mf)
        {
            const string prefix = "GrassPlatform";
            int index = mf.name.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            return mf.sharedMesh != null && index >= 0 && mf.name.Substring(index + prefix.Length).IndexOf("Grass", StringComparison.OrdinalIgnoreCase) < 0;
        }
        internal static bool BoundsIn(Transform container, Transform space, out Bounds bounds, bool terrainBody = false)
        {
            bounds = new Bounds(); bool found = false;
            MeshFilter[] filters = container.GetComponentsInChildren<MeshFilter>(true);
            bool bodyOnly = false;
            if (terrainBody) foreach (MeshFilter mf in filters) if (BodyMesh(mf)) bodyOnly = true;
            foreach (MeshFilter mf in filters)
            {
                if (mf.sharedMesh == null || (bodyOnly && !BodyMesh(mf))) continue;
                Renderer renderer = mf.GetComponent<Renderer>();
                if (!terrainBody && (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)) continue;
                Expand(ref bounds, ref found, mf.sharedMesh.bounds, space.worldToLocalMatrix * mf.transform.localToWorldMatrix);
            }
            if (!bodyOnly) foreach (SkinnedMeshRenderer skin in container.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skin.sharedMesh != null && (terrainBody || skin.enabled && skin.gameObject.activeInHierarchy))
                    Expand(ref bounds, ref found, skin.localBounds, space.worldToLocalMatrix * skin.transform.localToWorldMatrix);
            return found;
        }
        private static void Expand(ref Bounds total, ref bool found, Bounds b, Matrix4x4 matrix)
        {
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
            {
                Vector3 p = matrix.MultiplyPoint3x4(new Vector3(x == 0 ? b.min.x : b.max.x, y == 0 ? b.min.y : b.max.y, z == 0 ? b.min.z : b.max.z));
                if (!found) { total = new Bounds(p, Vector3.zero); found = true; } else total.Encapsulate(p);
            }
        }
        internal static void NormalizeTerrain(Transform wrapper, Transform visual)
        {
            Bounds b;
            if (!BoundsIn(wrapper, wrapper, out b, true) || b.size.x < .00001f) throw new InvalidOperationException("Không xác định được kích thước terrain.");
            visual.localScale *= 1f / b.size.x;
            BoundsIn(wrapper, wrapper, out b, true);
            visual.localPosition += new Vector3(-b.center.x, -.5f - b.min.y, -b.center.z);
        }
        internal static STARole GuessRole(string name)
        {
            string n = name.ToLowerInvariant().Replace("_", "").Replace("-", "").Replace(" ", "");
            if (name.StartsWith("Grass-", StringComparison.OrdinalIgnoreCase) && !n.Contains("background")) return STARole.Terrain;
            if (n.Contains("teleport")) return STARole.Teleport;
            if (n.Contains("enterpipe") || n.Contains("spawn") || n.Contains("entrance")) return STARole.Spawn;
            if (n.Contains("exit")) return STARole.Exit;
            if (n.Contains("cage")) return STARole.Cage;
            if (n.Contains("spike")) return STARole.Spikes;
            if (n.Contains("stairs") || n.Contains("staircase")) return STARole.Stairs;
            if (n.Contains("bridge")) return STARole.Bridge;
            if (n.Contains("fan")) return STARole.Fan;
            if (n.Contains("stone") || n.Contains("rock")) return STARole.Stone;
            if (n.Contains("lava") || n.Contains("toxic")) return STARole.Lava;
            if (n.Contains("key")) return STARole.Key;
            return STARole.Prop;
        }
        internal static void Property(SerializedObject so, string name, string label, string tip = "")
        { EditorGUILayout.PropertyField(so.FindProperty(name), new GUIContent(label, tip), true); }
        internal static readonly string[] RoleNames = { "Trang trí / khác", "Địa hình", "Cửa vào", "Cổng ra", "Lồng", "Chông", "Cầu thang", "Cầu", "Quạt", "Đá phá được", "Dịch chuyển", "Lava / chất độc", "Chìa khóa" };
        internal static readonly string[] ColliderNames = { "Không thêm", "Box 3D", "Trigger 3D", "Box 2D", "Trigger 2D" };
    }
}
