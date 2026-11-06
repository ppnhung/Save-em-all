using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SaveThemAll.Lite.Editor
{
    public sealed partial class STALiteWindow
    {
        [SerializeField] private bool importOpen = true, showImportBindings = true;
        [SerializeField] private float importDepth = -.65f;
        [SerializeField] private STACollider importCollider = STACollider.Box3D;
        private TextAsset importAsset;
        private STAMapDocument importDocument;
        private List<STAMapImporter.Binding> importBindings = new List<STAMapImporter.Binding>();
        private string importFile = "", importError = "";

        private void DrawImport()
        {
            importOpen = EditorGUILayout.Foldout(importOpen, "DỰNG TỰ ĐỘNG TỪ FILE MAP JSON", true);
            if (!importOpen) return;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Mở file → kiểm tra prefab → Dựng level → chọn object để chỉnh.", EditorStyles.wordWrappedMiniLabel);
            EditorGUI.BeginChangeCheck();
            STAPalette nextPalette = (STAPalette)EditorGUILayout.ObjectField("Palette để import", palette, typeof(STAPalette), false);
            if (EditorGUI.EndChangeCheck())
            {
                palette = nextPalette;
                if (importDocument != null) importBindings = STAMapImporter.Link(importDocument, palette);
            }
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            TextAsset nextAsset = (TextAsset)EditorGUILayout.ObjectField("JSON trong Project", importAsset, typeof(TextAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                importAsset = nextAsset;
                if (importAsset != null) ReadImport(importAsset.text, importAsset.name);
                else { importDocument = null; importBindings.Clear(); importError = ""; importFile = ""; }
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button("Mở file map…", GUILayout.Width(125))) { OpenImportFile(); GUIUtility.ExitGUI(); }
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(importError)) EditorGUILayout.HelpBox(importError, MessageType.Error);
            if (importDocument != null)
            {
                EditorGUILayout.LabelField(importDocument.name + "  •  " + importDocument.width + " × " + importDocument.height + "  •  " + importDocument.terrain.Length + " ô terrain / " + importDocument.objects.Length + " object", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Nguồn: " + importFile + "  |  " + importDocument.cellSize + " unit/ô", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.BeginHorizontal();
                showImportBindings = EditorGUILayout.Foldout(showImportBindings, "Liên kết prefab của map", true);
                if (GUILayout.Button("Tự nối lại", GUILayout.Width(95))) importBindings = STAMapImporter.Link(importDocument, palette);
                EditorGUILayout.EndHorizontal();
                int missingObjects = 0;
                foreach (STAMapImporter.Binding binding in importBindings)
                {
                    if (showImportBindings)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(new GUIContent(binding.label, binding.type), GUILayout.Width(145));
                        binding.prefab = (GameObject)EditorGUILayout.ObjectField(binding.prefab, typeof(GameObject), false);
                        binding.role = (STARole)EditorGUILayout.Popup((int)binding.role, STAEditorUtility.RoleNames, GUILayout.Width(120));
                        EditorGUILayout.EndHorizontal();
                    }
                    if (!STAMapImporter.IsPrefab(binding.prefab)) missingObjects++;
                }
                List<string> missingTerrain = STAMapImporter.MissingTerrain(importDocument, palette);
                if (missingTerrain.Count > 0)
                    EditorGUILayout.HelpBox("Thiếu hoặc trùng mã terrain: " + string.Join(", ", missingTerrain.ToArray()) + ". Tạo thêm prefab từ thư mục terrain hoặc sửa mã khối trong Palette.", MessageType.Warning);
                if (missingObjects > 0)
                    EditorGUILayout.HelpBox(missingObjects + " loại object chưa nối được. Kéo prefab từ Project vào các dòng trên. Mỗi dòng áp dụng cho mọi object cùng loại trong file.", MessageType.Warning);
                importDepth = EditorGUILayout.FloatField(new GUIContent("Chiều sâu object Z (ô)", "Mặc định -0.65 để object nằm phía trước terrain; 0 để giữ cùng mặt phẳng như importer cũ."), importDepth);
                importCollider = (STACollider)EditorGUILayout.Popup("Collider terrain", (int)importCollider, STAEditorUtility.ColliderNames);
                EditorGUILayout.LabelField("Giữ tỉ lệ và hướng model gốc; vùng ô trong JSON không kéo giãn mesh. Có thể chỉnh từng object sau khi dựng.", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(missingObjects > 0 || missingTerrain.Count > 0 || palette == null))
                    if (GUILayout.Button("Dựng level từ file", GUILayout.Height(32))) Run(BuildImportedLevel);
            }
            else EditorGUILayout.LabelField("Nhận .terrainmap.json / .json từ web Penguin Terrain Studio. Tệp .js cần chứa JSON thuần.", EditorStyles.wordWrappedMiniLabel);
            if (palette == null) EditorGUILayout.HelpBox("Tạo prefab ở tab 1 trước, rồi gán Palette để dựng map.", MessageType.Info);
            EditorGUILayout.EndVertical();
        }
        private void OpenImportFile()
        {
            string path = EditorUtility.OpenFilePanelWithFilters("Mở map JSON", "", new[] { "Map JSON", "json,js", "Tất cả file", "*" });
            if (string.IsNullOrEmpty(path)) return;
            importAsset = null; importDocument = null; importBindings.Clear(); importError = ""; importFile = Path.GetFileName(path);
            try
            {
                if (new FileInfo(path).Length > STAMapDocument.MaxBytes) throw new ArgumentException("File map lớn hơn 2 MB.");
                ReadImport(File.ReadAllText(path), importFile);
            }
            catch (Exception ex) { importError = ex.Message; }
        }
        private void ReadImport(string json, string fileName)
        {
            importDocument = null; importBindings.Clear(); importError = ""; importFile = fileName;
            try
            {
                STAMapDocument doc = STAMapImporter.Parse(json);
                List<STAMapImporter.Binding> links = STAMapImporter.Link(doc, palette);
                importDocument = doc; importBindings = links;
            }
            catch (Exception ex) { importError = ex.Message; }
        }
        private void BuildImportedLevel()
        {
            EndStroke();
            STALevel built = STAMapImporter.Build(importDocument, palette, importBindings, importFile, importDepth, importCollider, map);
            map = built; selectedObject = null; selectedAsset = -1; brush = 0; importOpen = false;
            Selection.activeGameObject = map.gameObject; STAMapActions.Frame(map); RepaintAll();
            AssetDatabase.SaveAssets();
            report = "Đã dựng " + importDocument.terrain.Length + " ô terrain + " + importDocument.objects.Length + " object từ " + importFile + ". Chọn object để chỉnh; bấm Lưu level prefab khi xong. Import lại tạo bản mới, giữ bản cũ trong Hierarchy.";
            GUIUtility.ExitGUI();
        }
    }
}
