using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SaveThemAll.AutoTile.Editor
{
    public sealed class TerrainMapImportWindow : EditorWindow
    {
        [SerializeField] private TextAsset jsonAsset;
        [SerializeField] private TerrainCatalog terrainCatalog;
        [SerializeField] private MapObjectCatalog objectCatalog;
        [SerializeField] private TerrainMap.CollisionMode collisionMode = TerrainMap.CollisionMode.GridRows3D;
        [SerializeField] private bool createCamera;
        [SerializeField] private DefaultAsset prefabFolder;
        [SerializeField] private bool allowMissingPrefabs;
        private string bindingReport, terrainReport;
        private bool showBindings = true;
        private string diskJson, sourceName, error;
        private TerrainMapDocument document;
        private Vector2 scroll;

        [MenuItem("Tools/Save Them All/Import TerrainMap")]
        public static void Open() { GetWindow<TerrainMapImportWindow>("Import TerrainMap").minSize = new Vector2(520, 640); }
        private void OnEnable()
        {
            if (objectCatalog == null)
            {
                string[] objects = AssetDatabase.FindAssets("t:MapObjectCatalog");
                if (objects.Length == 1) objectCatalog = AssetDatabase.LoadAssetAtPath<MapObjectCatalog>(AssetDatabase.GUIDToAssetPath(objects[0]));
            }
            if (terrainCatalog == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:TerrainCatalog");
                if (guids.Length == 1) terrainCatalog = AssetDatabase.LoadAssetAtPath<TerrainCatalog>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
        }
        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Penguin Terrain Studio", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Import .terrainmap.json exported by the web editor. Build creates a new root in the active scene and supports Undo. Existing maps are not replaced.", MessageType.Info);
            EditorGUI.BeginChangeCheck();
            jsonAsset = (TextAsset)EditorGUILayout.ObjectField("JSON in Assets", jsonAsset, typeof(TextAsset), false);
            if (EditorGUI.EndChangeCheck()) { diskJson = null; sourceName = jsonAsset != null ? jsonAsset.name : null; ReadDocument(); }
            if (GUILayout.Button("Choose JSON file from disk..."))
            {
                string path = EditorUtility.OpenFilePanel("Open TerrainMap JSON", "", "json");
                if (!string.IsNullOrEmpty(path))
                {
                    try
                    {
                        if (new FileInfo(path).Length > 2097152) throw new ArgumentException("File exceeds 2 MB.");
                        diskJson = File.ReadAllText(path); jsonAsset = null; sourceName = Path.GetFileName(path); ReadDocument();
                    }
                    catch (Exception ex) { document = null; error = ex.Message; }
                }
            }
            if (!string.IsNullOrEmpty(sourceName)) EditorGUILayout.LabelField("Source", sourceName);
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            terrainCatalog = (TerrainCatalog)EditorGUILayout.ObjectField("Terrain Catalog", terrainCatalog, typeof(TerrainCatalog), false);
            if (EditorGUI.EndChangeCheck()) { terrainReport = null; error = null; }
            using (new EditorGUI.DisabledScope(terrainCatalog == null || Application.isPlaying))
                if (GUILayout.Button("Repair Terrain Catalog", GUILayout.Height(28))) RepairTerrain();
            List<string> missingTerrain = MissingTerrainModels();
            if (document != null && missingTerrain.Count > 0)
                EditorGUILayout.HelpBox("Thiếu " + missingTerrain.Count + " prefab địa hình: " +
                    string.Join(", ", missingTerrain.ToArray()) + ". Bấm Repair Terrain Catalog để khôi phục liên kết.", MessageType.Error);
            if (!string.IsNullOrEmpty(terrainReport)) EditorGUILayout.HelpBox(terrainReport, MessageType.Info);
            objectCatalog = (MapObjectCatalog)EditorGUILayout.ObjectField("Object Catalog", objectCatalog, typeof(MapObjectCatalog), false);
            prefabFolder = (DefaultAsset)EditorGUILayout.ObjectField("Prefab folder (optional)", prefabFolder, typeof(DefaultAsset), false);
            EditorGUILayout.LabelField("Để trống: tìm trong toàn bộ Assets. Chỉ gán các liên kết còn thiếu.", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(objectCatalog == null))
                if (GUILayout.Button("Auto-link missing prefabs", GUILayout.Height(28))) AutoLink();
            if (GUILayout.Button("Create + auto-link Object Catalog...")) CreateObjectCatalog();
            if (!string.IsNullOrEmpty(bindingReport)) EditorGUILayout.HelpBox(bindingReport, MessageType.Info);
            if (GUILayout.Button("Export your prefabs to the web palette...")) AssetPaletteExportWindow.Open();
            collisionMode = (TerrainMap.CollisionMode)EditorGUILayout.EnumPopup("Terrain colliders", collisionMode);
            createCamera = EditorGUILayout.Toggle("Create orthographic camera", createCamera);
            int missingCount = 0;
            if (document != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(document.name + "  |  " + document.width + " x " + document.height, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(document.terrain.Length + " terrain cells, " + document.objects.Length + " objects; " + document.cellSize + " units/cell");
                showBindings = EditorGUILayout.Foldout(showBindings, "Prefab links for this map", true);
                if (showBindings) DrawBindings();
                List<string> missing = MissingTypes(); missingCount = missing.Count;
                if (missingCount > 0) EditorGUILayout.HelpBox("Chưa có prefab: " + string.Join(", ", missing.ToArray()) + ". Bấm Auto-link missing prefabs hoặc kéo prefab vào các dòng bên trên. Nếu bộ nguồn chưa có model, cần bổ sung model đó.", MessageType.Warning);
                else EditorGUILayout.HelpBox("Đã có prefab cho tất cả loại đồ vật trong map này.", MessageType.Info);
            }
            EditorGUILayout.HelpBox("count / pairId are preserved on MapObjectInstance. Your gameplay scripts must consume them. Object width/height define occupancy, not prefab stretching. Prefab scale and collider setup stay under your control.", MessageType.Info);
            allowMissingPrefabs = EditorGUILayout.ToggleLeft("Allow missing prefabs (markers only)", allowMissingPrefabs);
            if (missingCount > 0 && !allowMissingPrefabs) EditorGUILayout.HelpBox("Build đang chờ đủ liên kết prefab. Chỉ bật tùy chọn markers nếu bạn muốn dựng tạm với các nhãn thay thế.", MessageType.Info);
            using (new EditorGUI.DisabledScope(Application.isPlaying || document == null || terrainCatalog == null || missingTerrain.Count > 0 || (missingCount > 0 && !allowMissingPrefabs)))
                if (GUILayout.Button("Build map in scene", GUILayout.Height(36))) Build();
            using (new EditorGUI.DisabledScope(Application.isPlaying || SelectedMap() == null))
                if (GUILayout.Button("Rebuild terrain in selected map")) RebuildSelectedTerrain();
            using (new EditorGUI.DisabledScope(Application.isPlaying || objectCatalog == null || SelectedMap() == null))
                if (GUILayout.Button("Repair missing objects in selected map")) RepairSelectedMap();
            EditorGUILayout.LabelField("Đã import map? Chọn root map trong Hierarchy rồi dùng Repair để bổ sung marker trống.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
        }
        private List<string> MissingTerrainModels()
        {
            var result = new List<string>();
            if (document == null) return result;
            var occupied = new HashSet<Vector2Int>();
            foreach (var cell in document.terrain)
                occupied.Add(new Vector2Int(cell.x, cell.y));
            foreach (var cell in document.terrain)
            {
                int mask = 0;
                for (int i = 0; i < 8; i++)
                    if (occupied.Contains(new Vector2Int(cell.x + TerrainRules.DX[i], cell.y + TerrainRules.DY[i]))) mask |= 1 << i;
                string key = string.IsNullOrEmpty(cell.overrideModel) ? TerrainRules.Resolve(mask) : cell.overrideModel;
                if ((terrainCatalog == null || terrainCatalog.Find(key) == null) && !result.Contains(key)) result.Add(key);
            }
            result.Sort(StringComparer.Ordinal); return result;
        }
        private void RepairTerrain()
        {
            try { terrainReport = TerrainKitSetup.RepairTerrainCatalog(terrainCatalog); error = null; }
            catch (Exception ex) { error = ex.Message; }
        }
        private void RebuildSelectedTerrain()
        {
            TerrainMap map = SelectedMap();
            if (map == null) return;
            try
            {
                // Never switch the selected map to another catalog without telling the user.
                if (map.catalog != terrainCatalog)
                    throw new InvalidOperationException("Terrain Catalog của map đang chọn khác ô bên trên. Kéo đúng catalog của map vào ô Terrain Catalog rồi bấm Repair.");
                map.LoadCaches();
                string missing;
                if (!map.HasRequiredPrefabs(out missing))
                    throw new InvalidOperationException("Chưa thể dựng lại terrain; còn thiếu: " + missing + ". Bấm Repair Terrain Catalog trước.");
                map.Rebuild();
                EditorUtility.SetDirty(map);
                EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
                TerrainMapEditor.FrameMap(map);
                terrainReport = "Đã dựng lại terrain từ dữ liệu ô của map đang chọn. GameplayObjects được giữ nguyên.";
                error = null;
            }
            catch (Exception ex) { error = ex.Message; }
        }
        private void ReadDocument()
        {
            error = null; document = null;
            try { string json = jsonAsset != null ? jsonAsset.text : diskJson; if (!string.IsNullOrEmpty(json)) document = TerrainMapDocument.Parse(json); }
            catch (Exception ex) { error = ex.Message; }
        }
        private void CreateObjectCatalog()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create object prefab bindings", "MapObjectCatalog", "asset", "Choose where to save prefab bindings.");
            if (string.IsNullOrEmpty(path)) return;
            MapObjectCatalog catalog = CreateInstance<MapObjectCatalog>();
            foreach (string type in TerrainMapDocument.ObjectTypes) catalog.entries.Add(new MapObjectCatalog.Entry { type = type });
            AssetDatabase.CreateAsset(catalog, path); AssetDatabase.SaveAssets(); objectCatalog = catalog;
            AutoLink();
            EditorGUIUtility.PingObject(catalog);
        }
        private List<string> RequiredTypes()
        {
            var result = new List<string>();
            if (document == null) result.AddRange(TerrainMapDocument.ObjectTypes);
            else foreach (MapObjectRecord record in document.objects) if (!result.Contains(record.type)) result.Add(record.type);
            result.Sort(StringComparer.Ordinal); return result;
        }
        private List<string> MissingTypes()
        {
            var result = new List<string>();
            foreach (string type in RequiredTypes())
            {
                MapObjectCatalog.Entry binding = ResolveBinding(type);
                if (binding == null || !ObjectCatalogBindingUtility.IsPrefab(binding.prefab)) result.Add(type);
            }
            return result;
        }
        private void AutoLink()
        {
            try
            {
                string folder = prefabFolder != null ? AssetDatabase.GetAssetPath(prefabFolder) : "Assets";
                bindingReport = ObjectCatalogBindingUtility.AutoLink(objectCatalog, RequiredTypes(), folder).ToString();
                error = null;
            }
            catch (Exception ex) { error = ex.Message; }
        }
        private void DrawBindings()
        {
            using (new EditorGUI.DisabledScope(objectCatalog == null))
            foreach (string type in RequiredTypes())
            {
                MapObjectCatalog.Entry binding = ResolveBinding(type);
                GameObject current = binding != null ? binding.prefab : null;
                EditorGUI.BeginChangeCheck();
                GameObject next = (GameObject)EditorGUILayout.ObjectField(new GUIContent(ObjectLabel(type), type), current, typeof(GameObject), false);
                if (EditorGUI.EndChangeCheck())
                {
                    try { ObjectCatalogBindingUtility.SetPrefab(objectCatalog, type, next); bindingReport = null; error = null; }
                    catch (Exception ex) { error = ex.Message; }
                }
            }
            if (objectCatalog == null) EditorGUILayout.HelpBox("Tạo hoặc chọn Object Catalog để lưu liên kết.", MessageType.Info);
        }
        private MapObjectCatalog.Entry ResolveBinding(string type)
        { return ObjectCatalogBindingUtility.Resolve(objectCatalog, type); }
        private static TerrainMap SelectedMap()
        {
            GameObject selected = Selection.activeGameObject;
            return selected != null && !EditorUtility.IsPersistent(selected) && selected.scene.IsValid() ? selected.GetComponentInParent<TerrainMap>() : null;
        }
        private static GameObject PlacePrefab(MapObjectCatalog.Entry binding, Transform parent)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(binding.prefab, parent);
            if (instance == null) throw new ArgumentException("Binding must be a prefab asset: " + binding.type);
            try
            {
                instance.transform.localPosition = binding.localOffset;
                instance.transform.localRotation = Quaternion.Euler(binding.localEulerAngles) * binding.prefab.transform.localRotation;
                instance.transform.localScale = Vector3.Scale(binding.prefab.transform.localScale, binding.scale);
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
                return instance;
            }
            catch { DestroyImmediate(instance); throw; }
        }
        private void RepairSelectedMap()
        {
            TerrainMap map = SelectedMap(); if (map == null) return;
            int repaired = 0, skipped = 0, failed = 0;
            var missing = new HashSet<string>();
            try
            {
                ObjectCatalogBindingUtility.ValidateCatalog(objectCatalog);
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Repair missing map objects");
                foreach (MapObjectInstance marker in map.GetComponentsInChildren<MapObjectInstance>(true))
                {
                    if (!marker.missingPrefab || marker.data == null) continue;
                    if (marker.transform.childCount > 0) { skipped++; continue; } // Preserve manually added children.
                    MapObjectCatalog.Entry binding = ResolveBinding(marker.data.type);
                    if (binding == null || !ObjectCatalogBindingUtility.IsPrefab(binding.prefab)) { missing.Add(marker.data.type); continue; }
                    try
                    {
                        GameObject child = PlacePrefab(binding, marker.transform);
                        Undo.RegisterCreatedObjectUndo(child, "Restore missing prefab");
                        Undo.RecordObject(marker, "Clear missing prefab marker"); marker.missingPrefab = false;
                        EditorUtility.SetDirty(marker);
                        if (PrefabUtility.IsPartOfPrefabInstance(marker)) PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
                        repaired++;
                    }
                    catch (Exception ex) { failed++; Debug.LogException(ex); }
                }
                Undo.CollapseUndoOperations(group);
                if (repaired > 0) EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
                bindingReport = "Đã bổ sung " + repaired + " object. Giữ " + skipped + " marker đã có object con. Lỗi: " + failed + "." + (missing.Count > 0 ? " Còn thiếu: " + string.Join(", ", new List<string>(missing).ToArray()) : "");
            }
            catch (Exception ex) { error = ex.Message; }
        }
        private string ObjectLabel(string type)
        {
            if (document != null && document.assets != null) foreach (MapAssetDefinition asset in document.assets) if (asset.id == type) return asset.label;
            switch (type)
            {
                case "spawn": return "spawn · Ống xuất phát";
                case "exit": return "exit · Cổng đích";
                case "cage": return "cage · Lồng nhốt";
                case "stone": return "stone · Đá phá được";
                case "spikes": return "spikes · Chông";
                default: return type;
            }
        }
        private void Preflight()
        {
            document.Validate();
            HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
            foreach (TerrainRecord c in document.terrain) cells.Add(new Vector2Int(c.x, c.y));
            foreach (TerrainRecord c in document.terrain)
            {
                int mask = 0;
                for (int i = 0; i < 8; i++) if (cells.Contains(new Vector2Int(c.x + TerrainRules.DX[i], c.y + TerrainRules.DY[i]))) mask |= 1 << i;
                string model = string.IsNullOrEmpty(c.overrideModel) ? TerrainRules.Resolve(mask) : c.overrideModel;
                if (terrainCatalog.Find(model) == null) throw new ArgumentException("Terrain Catalog missing " + model + ". Click Repair Terrain Catalog. Existing maps have not been changed.");
            }
            if (objectCatalog != null) ObjectCatalogBindingUtility.ValidateCatalog(objectCatalog);
            if (!allowMissingPrefabs)
            {
                List<string> missing = MissingTypes();
                if (missing.Count > 0) throw new ArgumentException("Chưa có prefab: " + string.Join(", ", missing.ToArray()));
            }
        }
        private void Build()
        {
            GameObject root = null;
            try
            {
                Preflight();
                root = new GameObject(document.name);
                TerrainMap map = root.AddComponent<TerrainMap>();
                map.catalog = terrainCatalog; map.width = document.width; map.height = document.height;
                map.cellSize = document.cellSize; map.collisionMode = collisionMode;
                foreach (TerrainRecord c in document.terrain) map.cells.Add(new TerrainMap.Cell { position = new Vector2Int(c.x, c.y), overrideModel = c.overrideModel });
                map.Rebuild();
                Transform objectsRoot = new GameObject("Gameplay Objects").transform;
                objectsRoot.SetParent(root.transform, false);
                foreach (MapObjectRecord record in document.objects)
                {
                    GameObject holder = new GameObject(ObjectLabel(record.type) + " [" + record.id + "]"); holder.transform.SetParent(objectsRoot, false);
                    holder.transform.localPosition = new Vector3((record.x + (record.width - 1) * 0.5f + record.offsetX) * document.cellSize, (record.y + (record.height - 1) * 0.5f + record.offsetY) * document.cellSize, 0);
                    holder.transform.localRotation = Quaternion.Euler(0, 0, record.rotation);
                    holder.transform.localScale = Vector3.one * document.cellSize;
                    MapObjectInstance instance = holder.AddComponent<MapObjectInstance>();
                    instance.data = JsonUtility.FromJson<MapObjectRecord>(JsonUtility.ToJson(record));
                    MapObjectCatalog.Entry binding = ResolveBinding(record.type);
                    instance.missingPrefab = binding == null || !ObjectCatalogBindingUtility.IsPrefab(binding.prefab);
                    if (!instance.missingPrefab) PlacePrefab(binding, holder.transform);
                }
                if (createCamera)
                {
                    Camera camera = new GameObject("Map Camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform, false);
                    camera.transform.localPosition = new Vector3((document.width - 1) * document.cellSize * 0.5f, (document.height - 1) * document.cellSize * 0.5f, -20);
                    camera.orthographic = true; camera.orthographicSize = (document.height + 2) * document.cellSize * 0.5f;
                    camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.21f, 0.36f, 0.51f);
                    camera.farClipPlane = 1000;
                }
                Undo.RegisterCreatedObjectUndo(root, "Import TerrainMap");
                EditorSceneManager.MarkSceneDirty(root.scene);
                Selection.activeGameObject = root;
                if (SceneView.lastActiveSceneView != null)
                {
                    SceneView.lastActiveSceneView.LookAt(root.transform.position + new Vector3((document.width - 1) * document.cellSize * 0.5f, (document.height - 1) * document.cellSize * 0.5f, 0), Quaternion.identity, Mathf.Max(document.width, document.height) * document.cellSize * 0.65f, true);
                }
                error = null;
                Debug.Log("Imported " + document.name + ". Save your scene. Missing object prefabs are markers; gameplay is supplied by your project.", root);
            }
            catch (Exception ex)
            {
                if (root != null) DestroyImmediate(root);
                error = ex.Message; Debug.LogException(ex);
            }
        }
    }
}
