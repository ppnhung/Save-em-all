using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SaveThemAll.Lite.Editor
{
    internal static class STAPrefabBuilder
    {
        internal sealed class Source { public string path; public GameObject model; public bool include = true; }
        internal static List<Source> Scan(DefaultAsset folder)
        {
            string root = AssetDatabase.GetAssetPath(folder);
            if (!AssetDatabase.IsValidFolder(root) || (root != "Assets" && !root.StartsWith("Assets/")))
                throw new InvalidOperationException("Kéo thư mục chứa model/prefab trong Project vào ô Thư mục nguồn.");
            var sources = new List<Source>(); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!paths.Add(path) || path.StartsWith(STAEditorUtility.Content + "/", StringComparison.OrdinalIgnoreCase)) continue;
                bool model = AssetImporter.GetAtPath(path) is ModelImporter;
                if (!model && !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null || go.GetComponentInChildren<Renderer>(true) == null || go.GetComponent<STALevel>() != null) continue;
                sources.Add(new Source { path = path, model = go });
            }
            sources.Sort((a,b) => string.Compare(a.path,b.path,StringComparison.OrdinalIgnoreCase)); return sources;
        }
        internal static string Build(List<Source> sources, bool normalize, out STAPalette palette)
        {
            STAEditorUtility.Folder(STAEditorUtility.Content + "/Prefabs");
            string palettePath = STAEditorUtility.Content + "/Palette.asset";
            palette = AssetDatabase.LoadAssetAtPath<STAPalette>(palettePath);
            if (palette == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(palettePath) != null) throw new InvalidOperationException("Palette.asset đang là loại asset khác; đổi tên asset đó trước.");
                palette = ScriptableObject.CreateInstance<STAPalette>(); AssetDatabase.CreateAsset(palette, palettePath);
            }
            var report = new StringBuilder(); int created = 0, reused = 0, failed = 0;
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    Source s = sources[i]; if (!s.include || s.model == null) continue;
                    if (EditorUtility.DisplayCancelableProgressBar("Tạo prefab", s.model.name, (float)i / Math.Max(1,sources.Count)))
                    { report.AppendLine("Đã dừng; prefab tạo xong được giữ lại."); break; }
                    GameObject wrapper = null;
                    try
                    {
                        string guid = AssetDatabase.AssetPathToGUID(s.path);
                        string name = Path.GetFileNameWithoutExtension(s.path);
                        STARole role = STAEditorUtility.GuessRole(name);
                        bool existingPrefab = s.path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
                        string output = STAEditorUtility.Content + "/Prefabs/" + STAEditorUtility.SafeName(name) + "__" + guid + ".prefab";
                        GameObject prefab = existingPrefab ? s.model : AssetDatabase.LoadAssetAtPath<GameObject>(output);
                        if (prefab == null)
                        {
                            if (AssetDatabase.LoadMainAssetAtPath(output) != null) throw new InvalidOperationException("Đường dẫn output đã có asset khác.");
                            wrapper = new GameObject(name); SceneManager.MoveGameObjectToScene(wrapper, preview);
                            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(s.model, preview);
                            if (visual == null) throw new InvalidOperationException("Không tạo được model instance.");
                            visual.transform.SetParent(wrapper.transform, false);
                            if (normalize && role == STARole.Terrain) STAEditorUtility.NormalizeTerrain(wrapper.transform, visual.transform);
                            bool success;
                            prefab = PrefabUtility.SaveAsPrefabAsset(wrapper, output, out success);
                            if (!success || prefab == null) throw new InvalidOperationException("Unity không lưu được prefab.");
                            created++;
                        }
                        else reused++;
                        STAPalette.Entry entry = palette.entries.Find(e => e.sourceGuid == guid);
                        if (entry == null)
                        {
                            entry = new STAPalette.Entry { sourceGuid = guid, label = name, role = role, terrainKey = role == STARole.Terrain ? name : "" };
                            palette.entries.Add(entry);
                        }
                        entry.prefab = prefab; // Preserve any role/label/key the artist edited on previous runs.
                    }
                    catch (Exception ex) { failed++; report.AppendLine(s.path + ": " + ex.Message); Debug.LogException(ex); }
                    finally { if (wrapper != null) UnityEngine.Object.DestroyImmediate(wrapper); }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar(); EditorSceneManager.ClosePreviewScene(preview);
                EditorUtility.SetDirty(palette); AssetDatabase.SaveAssets();
            }
            return "Mới: " + created + " | Dùng lại: " + reused + " | Lỗi: " + failed + "\nPrefab: " + STAEditorUtility.Content + "/Prefabs\n" + report;
        }
    }
}
