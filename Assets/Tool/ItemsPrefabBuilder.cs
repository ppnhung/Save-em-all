// Save Them All — one regular prefab per imported FBX.
// Place this file in an Editor folder. No dependency on the AutoTile Kit.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SaveThemAll.PrefabTools.Editor
{
    public sealed class ItemsPrefabBuilder : EditorWindow
    {
        [SerializeField] private DefaultAsset sourceFolder;
        [SerializeField] private string outputFolder = "Assets/Items_Prefabs";
        private readonly List<string> models = new List<string>();
        private string scannedFolder, status = "Chọn thư mục Items rồi bấm Quét FBX.";
        private string report = "";
        private Vector2 scroll;
        private bool building;

        [MenuItem("Tools/Save Them All/Create Prefabs From FBX")]
        public static void Open()
        {
            GetWindow<ItemsPrefabBuilder>("Tạo prefab hàng loạt").minSize = new Vector2(520, 430);
        }
        private void OnEnable()
        {
            string selected = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (AssetDatabase.IsValidFolder(selected))
                sourceFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(selected);
        }
        private void OnGUI()
        {
            EditorGUILayout.LabelField("FBX → Prefab", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Mỗi FBX có mesh được tạo thành một prefab riêng, bao gồm cả thư mục con. Prefab giữ material, hierarchy, rig và liên kết model hiện có. PNG được dùng qua material, không tạo thành prefab 3D.", MessageType.Info);
            using (new EditorGUI.DisabledScope(building || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUI.BeginChangeCheck();
                sourceFolder = (DefaultAsset)EditorGUILayout.ObjectField("Thư mục nguồn", sourceFolder, typeof(DefaultAsset), false);
                if (EditorGUI.EndChangeCheck()) { models.Clear(); scannedFolder = null; status = "Bấm Quét FBX để đọc thư mục mới."; }
                outputFolder = EditorGUILayout.TextField("Lưu prefab tại", outputFolder);
                EditorGUILayout.HelpBox("Mặc định: Assets/Items_Prefabs. Giữ cây thư mục như nguồn. Prefab đã có sẽ được bỏ qua khi chạy lại.", MessageType.None);
                if (GUILayout.Button("1. Quét FBX", GUILayout.Height(30))) Scan();
                EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);
                scroll = EditorGUILayout.BeginScrollView(scroll);
                foreach (string path in models)
                    EditorGUILayout.LabelField(path.Substring(scannedFolder.Length + 1), EditorStyles.miniLabel);
                EditorGUILayout.EndScrollView();
                using (new EditorGUI.DisabledScope(models.Count == 0))
                    if (GUILayout.Button("2. Tạo " + models.Count + " prefab", GUILayout.Height(38))) Build();
                if (!string.IsNullOrEmpty(report))
                {
                    EditorGUILayout.HelpBox(report, MessageType.Info);
                    if (GUILayout.Button("Mở thư mục prefab")) ShowOutput();
                }
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorGUILayout.HelpBox("Dừng Play Mode trước khi tạo prefab.", MessageType.Warning);
        }
        private string SourcePath()
        {
            string path = AssetDatabase.GetAssetPath(sourceFolder);
            if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path) || !(path == "Assets" || path.StartsWith("Assets/", StringComparison.Ordinal)))
                throw new ArgumentException("Kéo thư mục Items bên trong Assets vào ô Thư mục nguồn.");
            return path;
        }
        private static string CleanOutput(string path)
        {
            path = (path ?? "").Trim().Replace('\\', '/').TrimEnd('/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) throw new ArgumentException("Thư mục kết quả phải nằm trong Assets, ví dụ Assets/Items_Prefabs.");
            foreach (string part in path.Split('/'))
            {
                if (string.IsNullOrWhiteSpace(part) || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" "))
                    throw new ArgumentException("Đường dẫn thư mục kết quả không hợp lệ.");
                foreach (char c in part) if (char.IsControl(c) || "<>:\"|?*".IndexOf(c) >= 0) throw new ArgumentException("Đường dẫn chứa ký tự không hợp lệ.");
            }
            return path;
        }
        private static int MeshCount(GameObject model)
        {
            int count = 0;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null) count++;
            foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.sharedMesh != null) count++;
            return count;
        }
        private void Scan()
        {
            models.Clear(); scannedFolder = null; report = "";
            try
            {
                scannedFolder = SourcePath();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string[] guids = AssetDatabase.FindAssets("t:Model", new[] { scannedFolder });
                int noMesh = 0;
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) || !seen.Add(path)) continue;
                    GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (model == null || MeshCount(model) == 0) { noMesh++; continue; }
                    models.Add(path);
                }
                models.Sort(StringComparer.OrdinalIgnoreCase);
                status = "Tìm thấy " + models.Count + " FBX có mesh. " + noMesh + " FBX không có mesh được bỏ qua.";
            }
            catch (Exception ex) { models.Clear(); scannedFolder = null; status = ex.Message; }
        }
        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(folder))))
                throw new IOException("Không tạo được thư mục " + folder);
        }
        private void Build()
        {
            if (building || EditorApplication.isPlayingOrWillChangePlaymode) return;
            Scene preview = default(Scene);
            int created = 0, skipped = 0, failed = 0;
            bool cancelled = false;
            var details = new StringBuilder();
            building = true;
            try
            {
                if (SourcePath() != scannedFolder) throw new InvalidOperationException("Thư mục nguồn đã đổi. Hãy quét lại.");
                outputFolder = CleanOutput(outputFolder);
                EnsureFolder(outputFolder);
                // Use an isolated preview scene. Do not save/replace the user's active scene.
                preview = EditorSceneManager.NewPreviewScene();
                foreach (string path in models)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Tạo prefab từ FBX", path, (float)(created + skipped + failed) / models.Count))
                    { cancelled = true; break; }
                    GameObject wrapper = null;
                    try
                    {
                        string relative = path.Substring(scannedFolder.Length + 1);
                        string destination = outputFolder + "/" + Path.ChangeExtension(relative, ".prefab").Replace('\\', '/');
                        if (AssetDatabase.LoadMainAssetAtPath(destination) != null || File.Exists(destination))
                        { skipped++; details.AppendLine("EXISTS  " + destination); continue; }
                        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (model == null || MeshCount(model) == 0) throw new InvalidOperationException("Model chưa import xong hoặc không có mesh: " + path);
                        EnsureFolder(Path.GetDirectoryName(destination).Replace('\\', '/'));
                        wrapper = new GameObject(Path.GetFileNameWithoutExtension(path));
                        SceneManager.MoveGameObjectToScene(wrapper, preview);
                        GameObject child = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
                        if (child == null) throw new InvalidOperationException("Không tạo được instance: " + path);
                        child.transform.SetParent(wrapper.transform, false);
                        child.name = model.name;
                        child.transform.localPosition = model.transform.localPosition;
                        child.transform.localRotation = model.transform.localRotation;
                        child.transform.localScale = model.transform.localScale;
                        // The plain wrapper becomes a regular .prefab; the FBX remains nested.
                        // No mesh/material overrides, collider, gameplay script or controller is added.
                        bool success;
                        GameObject saved = PrefabUtility.SaveAsPrefabAsset(wrapper, destination, out success);
                        if (!success || saved == null || MeshCount(saved) == 0)
                            throw new IOException("Không lưu được prefab có mesh: " + destination);
                        created++;
                        details.AppendLine("CREATED " + destination);
                    }
                    catch (Exception ex) { failed++; details.AppendLine("ERROR   " + path + " : " + ex.Message); Debug.LogError(ex.Message); }
                    finally { if (wrapper != null) DestroyImmediate(wrapper); }
                }
                AssetDatabase.SaveAssets();
                report = (cancelled ? "Đã dừng. " : "Hoàn tất. ") + "Tạo mới: " + created + " | Đã có: " + skipped + " | Lỗi: " + failed + "\nKết quả: " + outputFolder + "\nXem Console để biết từng đường dẫn.";
                status = report;
                Debug.Log("[Items Prefab Builder] " + report + "\n" + details);
                ShowOutput();
            }
            catch (Exception ex) { report = ex.Message; Debug.LogException(ex); }
            finally
            {
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                EditorUtility.ClearProgressBar(); building = false; Repaint();
            }
        }
        private void ShowOutput()
        {
            UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(outputFolder);
            if (folder != null) { Selection.activeObject = folder; EditorGUIUtility.PingObject(folder); }
        }
    }
}
