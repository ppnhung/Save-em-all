using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SaveThemAll.AutoTile.Editor
{
    // Editor-only bridge: actual project prefabs -> portable web palette + prefab bindings.
    public sealed class AssetPaletteExportWindow : EditorWindow
    {
        [Serializable] private sealed class PaletteDocument
        {
            public string format = "penguin-asset-palette";
            public int version = 1;
            public MapAssetDefinition[] assets;
        }
        private sealed class Candidate
        {
            public string id, path, label;
            public GameObject prefab;
            public bool selected = true;
            public MapObjectRole role;
            public int width = 1, height = 1;
        }
        private sealed class Pending { public Candidate item; public double started; }
        [SerializeField] private DefaultAsset folder;
        [SerializeField] private MapObjectCatalog catalog;
        [SerializeField] private bool includeModels;
        private readonly List<Candidate> candidates = new List<Candidate>();
        private readonly List<Pending> pending = new List<Pending>();
        private readonly List<MapAssetDefinition> output = new List<MapAssetDefinition>();
        private List<Candidate> queue;
        private int cursor, noPreview;
        private bool exporting;
        private string destination, message, search = "";
        private Vector2 scroll;

        [MenuItem("Tools/Save Them All/Export Asset Palette")]
        public static void Open() { GetWindow<AssetPaletteExportWindow>("Asset Palette").minSize = new Vector2(620, 480); }
        private void OnEnable()
        {
            string selected = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (AssetDatabase.IsValidFolder(selected)) folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(selected);
        }
        private void OnDisable() { StopExport(); }
        private void OnGUI()
        {
            EditorGUILayout.LabelField("Your prefabs → Penguin Terrain Studio", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Scan a folder (including subfolders), export a palette, then import that JSON in the web Assets tab. Each prefab keeps its own stable ID. Existing catalog offsets, rotations and scales are preserved.", MessageType.Info);
            using (new EditorGUI.DisabledScope(exporting))
            {
                folder = (DefaultAsset)EditorGUILayout.ObjectField("Asset folder", folder, typeof(DefaultAsset), false);
                includeModels = EditorGUILayout.Toggle("Include model files (FBX...)", includeModels);
                catalog = (MapObjectCatalog)EditorGUILayout.ObjectField("Object Catalog", catalog, typeof(MapObjectCatalog), false);
                if (GUILayout.Button("Create Object Catalog...")) CreateCatalog();
                if (GUILayout.Button("Scan folder", GUILayout.Height(28))) Scan();
                EditorGUILayout.BeginHorizontal();
                search = EditorGUILayout.TextField("Find", search);
                if (GUILayout.Button("Select all", GUILayout.Width(85))) foreach (Candidate c in candidates) c.selected = true;
                if (GUILayout.Button("Select none", GUILayout.Width(90))) foreach (Candidate c in candidates) c.selected = false;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField(candidates.FindAll(c => c.selected).Count + " / " + candidates.Count + " selected. Footprints are grid occupancy, not mesh scale.");
                scroll = EditorGUILayout.BeginScrollView(scroll);
                foreach (Candidate c in candidates)
                {
                    if (!string.IsNullOrEmpty(search) && c.path.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    c.selected = EditorGUILayout.Toggle(c.selected, GUILayout.Width(20));
                    GUILayout.Label(AssetPreview.GetMiniThumbnail(c.prefab), GUILayout.Width(22), GUILayout.Height(22));
                    c.label = EditorGUILayout.TextField(c.label);
                    c.role = (MapObjectRole)EditorGUILayout.EnumPopup(c.role, GUILayout.Width(85));
                    c.width = Mathf.Clamp(EditorGUILayout.IntField(c.width, GUILayout.Width(32)), 1, 64);
                    GUILayout.Label("×", GUILayout.Width(12));
                    c.height = Mathf.Clamp(EditorGUILayout.IntField(c.height, GUILayout.Width(32)), 1, 64);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.LabelField(c.path, EditorStyles.miniLabel);
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndScrollView();
                using (new EditorGUI.DisabledScope(catalog == null || !candidates.Exists(c => c.selected)))
                    if (GUILayout.Button("Export palette + bind prefabs", GUILayout.Height(36))) StartExport();
            }
            if (exporting && GUILayout.Button("Cancel export")) { StopExport(); message = "Export cancelled. No file or catalog changes were saved."; }
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
            EditorGUILayout.HelpBox("Use role spawn / exit / portal etc. only when that matches your prefab. Role affects web labels/checks; it does not add gameplay scripts. Missing previews use a name badge on the web. Models without prefabs can be included, but contain only the model's existing components.", MessageType.None);
        }
        private void CreateCatalog()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create Object Catalog", "MapObjectCatalog", "asset", "Save the prefab bindings in your project.");
            if (string.IsNullOrEmpty(path)) return;
            catalog = CreateInstance<MapObjectCatalog>();
            foreach (string type in TerrainMapDocument.ObjectTypes) catalog.entries.Add(new MapObjectCatalog.Entry { type = type });
            AssetDatabase.CreateAsset(catalog, path); AssetDatabase.SaveAssets();
        }
        private void Scan()
        {
            try
            {
                string path = AssetDatabase.GetAssetPath(folder);
                if (!AssetDatabase.IsValidFolder(path) || !(path == "Assets" || path.StartsWith("Assets/", StringComparison.Ordinal))) throw new ArgumentException("Choose a folder inside Assets.");
                HashSet<string> guids = new HashSet<string>(AssetDatabase.FindAssets("t:Prefab", new[] { path }));
                // GameObject covers prefab assets in older Unity versions as well.
                guids.UnionWith(AssetDatabase.FindAssets("t:GameObject", new[] { path }));
                if (includeModels) guids.UnionWith(AssetDatabase.FindAssets("t:Model", new[] { path }));
                List<Candidate> found = new List<Candidate>();
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                    if (prefab == null) continue;
                    PrefabAssetType kind = PrefabUtility.GetPrefabAssetType(prefab);
                    if (kind == PrefabAssetType.NotAPrefab || (!includeModels && kind == PrefabAssetType.Model)) continue;
                    string id = "prefab_" + guid.ToLowerInvariant();
                    MapObjectCatalog.Entry entry = catalog != null ? catalog.Find(id) : null;
                    string label = entry != null && !string.IsNullOrWhiteSpace(entry.displayName) ? entry.displayName : prefab.name;
                    found.Add(new Candidate { id = id, path = assetPath, prefab = prefab, label = label, role = entry != null ? entry.role : MapObjectRole.prop, width = entry != null ? Mathf.Clamp(entry.width, 1, 64) : 1, height = entry != null ? Mathf.Clamp(entry.height, 1, 64) : 1 });
                }
                if (found.Count > 4096) throw new ArgumentException("More than 4096 assets. Choose a smaller folder and export multiple palettes.");
                found.Sort((a, b) => string.Compare(a.path, b.path, StringComparison.OrdinalIgnoreCase));
                candidates.Clear(); candidates.AddRange(found);
                message = found.Count + " assets found. Uncheck unwanted assets; choose a role and grid footprint if needed.";
            }
            catch (Exception ex) { message = ex.Message; }
        }
        private void StartExport()
        {
            try
            {
                if (catalog == null || !AssetDatabase.Contains(catalog)) throw new ArgumentException("Create or select an Object Catalog asset first.");
                HashSet<string> ids = new HashSet<string>();
                foreach (MapObjectCatalog.Entry entry in catalog.entries)
                    if (entry != null && !string.IsNullOrEmpty(entry.type) && !ids.Add(entry.type)) throw new ArgumentException("Duplicate catalog ID: " + entry.type);
                queue = candidates.FindAll(c => c.selected);
                foreach (Candidate c in queue)
                    if (c.prefab == null || string.IsNullOrWhiteSpace(c.label) || c.label.Length > 160) throw new ArgumentException("Each selected asset needs a prefab and a label of 1–160 characters.");
                destination = EditorUtility.SaveFilePanel("Export asset palette", "", "Penguin_Assets.assetpalette.json", "json");
                if (string.IsNullOrEmpty(destination)) return;
                cursor = 0; noPreview = 0; output.Clear(); pending.Clear(); exporting = true;
                EditorApplication.update += ExportStep;
            }
            catch (Exception ex) { message = ex.Message; }
        }
        private void ExportStep()
        {
            try
            {
                if (EditorUtility.DisplayCancelableProgressBar("Export Asset Palette", output.Count + " / " + queue.Count + " assets. Preparing previews…", (float)output.Count / queue.Count))
                { StopExport(); message = "Export cancelled. No file or catalog changes were saved."; return; }
                while (pending.Count < 16 && cursor < queue.Count)
                {
                    Candidate c = queue[cursor++];
                    AssetPreview.GetAssetPreview(c.prefab);
                    pending.Add(new Pending { item = c, started = EditorApplication.timeSinceStartup });
                }
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    Pending p = pending[i];
                    Texture2D preview = AssetPreview.GetAssetPreview(p.item.prefab);
                    if (preview == null && EditorApplication.timeSinceStartup - p.started < 1.5) continue;
                    Candidate c = p.item;
                    string thumbnail = null;
                    if (preview != null) { try { thumbnail = EncodePreview(preview); } catch { /* Asset stays usable without a preview. */ } }
                    if (string.IsNullOrEmpty(thumbnail)) noPreview++;
                    output.Add(new MapAssetDefinition { id = c.id, label = c.label.Trim(), role = c.role.ToString(), w = c.width, h = c.height, sourcePath = c.path, thumbnail = thumbnail });
                    pending.RemoveAt(i);
                }
                Repaint();
                if (cursor < queue.Count || pending.Count > 0) return;
                output.Sort((a, b) => string.Compare(a.sourcePath, b.sourcePath, StringComparison.OrdinalIgnoreCase));
                string json = JsonUtility.ToJson(new PaletteDocument { assets = output.ToArray() }, true);
                if (Encoding.UTF8.GetByteCount(json) > 64 * 1024 * 1024) throw new ArgumentException("Palette exceeds 64 MB. Export fewer assets per file.");
                File.WriteAllText(destination, json, new UTF8Encoding(false));
                Undo.RecordObject(catalog, "Bind exported asset palette");
                foreach (Candidate c in queue)
                {
                    MapObjectCatalog.Entry entry = catalog.Find(c.id);
                    if (entry == null) { entry = new MapObjectCatalog.Entry { type = c.id }; catalog.entries.Add(entry); }
                    entry.prefab = c.prefab; entry.displayName = c.label.Trim(); entry.role = c.role; entry.width = c.width; entry.height = c.height;
                }
                EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
                // Old TerrainMaps use spawn/cage/... while palette assets use prefab_GUID.
                // Add legacy aliases too; keep every existing explicit binding.
                string legacyReport;
                try { legacyReport = ObjectCatalogBindingUtility.AutoLink(catalog, TerrainMapDocument.ObjectTypes, AssetDatabase.GetAssetPath(folder), false).ToString(); }
                catch (Exception ex) { legacyReport = "Palette đã xuất. Chưa tự nối mã map cũ: " + ex.Message; }
                StopExport();
                message = "Exported " + output.Count + " assets. " + noPreview + " without preview. Import this file on the web: " + destination + "\n" + legacyReport;
                EditorUtility.RevealInFinder(destination);
            }
            catch (Exception ex) { StopExport(); message = "Export stopped: " + ex.Message; Debug.LogException(ex); }
        }
        private static string EncodePreview(Texture2D source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture temporary = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D readable = null;
            try
            {
                Graphics.Blit(source, temporary); RenderTexture.active = temporary;
                readable = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); readable.Apply();
                return "data:image/png;base64," + Convert.ToBase64String(readable.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(temporary); if (readable != null) DestroyImmediate(readable); }
        }
        private void StopExport()
        { EditorApplication.update -= ExportStep; exporting = false; EditorUtility.ClearProgressBar(); Repaint(); }
    }
}
