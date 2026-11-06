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
        private string diskJson, sourceName, error;
        private TerrainMapDocument document;
        private Vector2 scroll;

        [MenuItem("Tools/Save Them All/Import TerrainMap")]
        public static void Open() { GetWindow<TerrainMapImportWindow>("Import TerrainMap").minSize = new Vector2(400, 430); }
        private void OnEnable()
        {
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
            terrainCatalog = (TerrainCatalog)EditorGUILayout.ObjectField("Terrain Catalog", terrainCatalog, typeof(TerrainCatalog), false);
            objectCatalog = (MapObjectCatalog)EditorGUILayout.ObjectField("Object Catalog", objectCatalog, typeof(MapObjectCatalog), false);
            if (GUILayout.Button("Create empty Object Catalog...")) CreateObjectCatalog();
            collisionMode = (TerrainMap.CollisionMode)EditorGUILayout.EnumPopup("Terrain colliders", collisionMode);
            createCamera = EditorGUILayout.Toggle("Create orthographic camera", createCamera);
            if (document != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(document.name + "  |  " + document.width + " x " + document.height, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(document.terrain.Length + " terrain cells, " + document.objects.Length + " objects; " + document.cellSize + " units/cell");
                HashSet<string> missing = new HashSet<string>();
                foreach (MapObjectRecord o in document.objects) if (objectCatalog == null || objectCatalog.Find(o.type) == null || objectCatalog.Find(o.type).prefab == null) missing.Add(o.type);
                if (missing.Count > 0) EditorGUILayout.HelpBox("No prefab for: " + string.Join(", ", new List<string>(missing).ToArray()) + ". These objects become labeled Scene markers. Bind gameplay prefabs in Object Catalog and import again.", MessageType.Warning);
            }
            EditorGUILayout.HelpBox("count / pairId are preserved on MapObjectInstance. Your gameplay scripts must consume them. Object width/height define occupancy, not prefab stretching. Prefab scale and collider setup stay under your control.", MessageType.Info);
            using (new EditorGUI.DisabledScope(document == null || terrainCatalog == null))
                if (GUILayout.Button("Build map in scene", GUILayout.Height(36))) Build();
            EditorGUILayout.EndScrollView();
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
            EditorGUIUtility.PingObject(catalog);
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
                if (terrainCatalog.Find(model) == null) throw new ArgumentException("Terrain Catalog missing " + model + ". Run Setup Kit first.");
            }
            if (objectCatalog != null)
            {
                HashSet<string> types = new HashSet<string>();
                foreach (MapObjectCatalog.Entry e in objectCatalog.entries)
                    if (e != null && !string.IsNullOrEmpty(e.type) && !types.Add(e.type)) throw new ArgumentException("Duplicate Object Catalog entry: " + e.type);
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
                    GameObject holder = new GameObject(record.type + " [" + record.id + "]"); holder.transform.SetParent(objectsRoot, false);
                    holder.transform.localPosition = new Vector3((record.x + (record.width - 1) * 0.5f + record.offsetX) * document.cellSize, (record.y + (record.height - 1) * 0.5f + record.offsetY) * document.cellSize, 0);
                    holder.transform.localRotation = Quaternion.Euler(0, 0, record.rotation);
                    holder.transform.localScale = Vector3.one * document.cellSize;
                    MapObjectInstance instance = holder.AddComponent<MapObjectInstance>();
                    instance.data = JsonUtility.FromJson<MapObjectRecord>(JsonUtility.ToJson(record));
                    MapObjectCatalog.Entry binding = objectCatalog != null ? objectCatalog.Find(record.type) : null;
                    instance.missingPrefab = binding == null || binding.prefab == null;
                    if (!instance.missingPrefab)
                    {
                        GameObject prefab = (GameObject)PrefabUtility.InstantiatePrefab(binding.prefab, holder.transform);
                        if (prefab == null) throw new ArgumentException("Binding must be a prefab asset: " + record.type);
                        prefab.transform.localPosition = binding.localOffset;
                        prefab.transform.localRotation = Quaternion.Euler(binding.localEulerAngles) * binding.prefab.transform.localRotation;
                        prefab.transform.localScale = Vector3.Scale(binding.prefab.transform.localScale, binding.scale);
                    }
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
