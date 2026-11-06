using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SaveThemAll.Lite.Editor
{
    public sealed partial class STALiteWindow : EditorWindow
    {
        [SerializeField] private int tab;
        [SerializeField] private DefaultAsset sourceFolder;
        [SerializeField] private STAPalette palette;
        [SerializeField] private STALevel map;
        [SerializeField] private bool normalizeTerrain = true;
        [SerializeField] private string newName = "Level_01";
        [SerializeField] private int newColumns = 9, newRows = 16;
        private List<STAPrefabBuilder.Source> sources = new List<STAPrefabBuilder.Source>();
        private List<GameObject> savedLevels = new List<GameObject>();
        private int savedIndex, selectedAsset = -1, category, brush;
        private STAObject selectedObject;
        private string search = "", report = "";
        private bool assetSettings, gridSettings, showGrid = true;
        private Vector2 sourceScroll, paletteScroll, objectScroll, mainScroll;
        private GameObject addPrefab;
        private int strokeGroup = -1, ownedControl;
        private STACell lastCell;
        private bool hasLast;

        [MenuItem("Tools/Save Them All Lite/1. Create Prefabs")]
        public static void OpenPrefabs() { Open(0); }
        [MenuItem("Tools/Save Them All Lite/2. Map Editor")]
        public static void OpenMap() { Open(1); }
        private static void Open(int tab)
        {
            STALiteWindow w = GetWindow<STALiteWindow>("Save Them All Lite");
            w.minSize = new Vector2(740,550); w.tab = tab; w.SelectionChanged(); w.Show();
        }
        private void OnEnable()
        {
            SceneView.duringSceneGui += SceneGUI; Selection.selectionChanged += SelectionChanged; Undo.undoRedoPerformed += RepaintAll;
            if (palette == null) palette = AssetDatabase.LoadAssetAtPath<STAPalette>(STAEditorUtility.Content + "/Palette.asset");
            ReloadLevels(); SelectionChanged();
        }
        private void OnDisable()
        {
            SceneView.duringSceneGui -= SceneGUI; Selection.selectionChanged -= SelectionChanged; Undo.undoRedoPerformed -= RepaintAll;
            EndStroke();
        }
        private void RepaintAll() { Repaint(); SceneView.RepaintAll(); }
        private void SelectionChanged()
        {
            GameObject go = Selection.activeGameObject;
            selectedObject = go != null ? go.GetComponentInParent<STAObject>() : null;
            STALevel chosen = go != null ? go.GetComponentInParent<STALevel>() : null;
            if (STAEditorUtility.SceneMap(chosen)) { map = chosen; if (map.palette != null) palette = map.palette; }
            Repaint();
        }
        private void OnGUI()
        {
            float previous = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 165;
            try
            {
                EditorGUILayout.LabelField("SAVE THEM ALL LITE  •  1.1  •  JSON Import",EditorStyles.boldLabel);
                tab = GUILayout.Toolbar(tab,new[] { "1  Tạo prefab", "2  Dựng map" });
                if (Application.isPlaying) EditorGUILayout.HelpBox("Thoát Play Mode để chỉnh asset/map.",MessageType.Info);
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    if (tab == 0) PrefabGUI(); else MapGUI();
                }
                if (!string.IsNullOrEmpty(report)) EditorGUILayout.HelpBox(report,MessageType.Info);
            }
            catch (ExitGUIException) { throw; }
            catch (Exception ex) { report = ex.Message; Debug.LogException(ex); }
            finally { EditorGUIUtility.labelWidth = previous; }
        }
        private void PrefabGUI()
        {
            EditorGUILayout.HelpBox("Chọn thư mục item đã nhập vào Unity. Model sẽ thành prefab riêng; prefab có sẵn được thêm trực tiếp. Giữ material, Animator và hierarchy hiện có.",MessageType.Info);
            sourceFolder = (DefaultAsset)EditorGUILayout.ObjectField("Thư mục nguồn",sourceFolder,typeof(DefaultAsset),false);
            normalizeTerrain = EditorGUILayout.Toggle(new GUIContent("Căn terrain về 1 ô","Chỉ áp dụng model có tên Grass-*. Căn theo thân khối; không đổi mesh, UV hay material."),normalizeTerrain);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Quét thư mục",GUILayout.Height(28)))
            Run(() => { sources = STAPrefabBuilder.Scan(sourceFolder); report = "Tìm được " + sources.Count + " model/prefab có mesh."; });
            using (new EditorGUI.DisabledScope(sources.Count == 0))
                if (GUILayout.Button("Tạo prefab + cập nhật danh sách",GUILayout.Height(28)))
                Run(() => { report = STAPrefabBuilder.Build(sources,normalizeTerrain,out palette); selectedAsset = -1; });
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Chọn tất cả")) foreach (var source in sources) source.include = true;
            if (GUILayout.Button("Bỏ chọn")) foreach (var source in sources) source.include = false;
            EditorGUILayout.EndHorizontal();
            sourceScroll = EditorGUILayout.BeginScrollView(sourceScroll,GUILayout.MinHeight(220));
            foreach (var source in sources)
            {
                EditorGUILayout.BeginHorizontal(); source.include = EditorGUILayout.Toggle(source.include,GUILayout.Width(18));
                GUILayout.Label(AssetPreview.GetMiniThumbnail(source.model),GUILayout.Width(24),GUILayout.Height(24));
                EditorGUILayout.LabelField(new GUIContent(source.model != null ? source.model.name : "Missing",source.path));
                if (GUILayout.Button("Xem",GUILayout.Width(45))) EditorGUIUtility.PingObject(source.model);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.HelpBox("Kết quả: Assets/STA_Content/Prefabs. Chạy lại sẽ dùng lại prefab đã có; thiết lập bạn sửa trên prefab được giữ. Có thể quét thêm thư mục khác để bổ sung item.",MessageType.None);
            using (new EditorGUI.DisabledScope(palette == null))
                if (GUILayout.Button("Tiếp tục → Dựng map",GUILayout.Height(30))) tab = 1;
        }
        private void MapGUI()
        {
            mainScroll = EditorGUILayout.BeginScrollView(mainScroll);
            EditorGUILayout.BeginHorizontal();
            STALevel chosen = (STALevel)EditorGUILayout.ObjectField("Map trong scene",map,typeof(STALevel),true);
            if (chosen != map) { EndStroke(); map = chosen; selectedObject = null; if (map != null && map.palette != null) palette = map.palette; }
            if (GUILayout.Button("Canh góc nhìn",GUILayout.Width(115))) STAMapActions.Frame(map);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            newName = EditorGUILayout.TextField("Tên level mới",newName);
            newColumns = Mathf.Clamp(EditorGUILayout.IntField(newColumns,GUILayout.Width(45)),1,128);
            GUILayout.Label("×",GUILayout.Width(15)); newRows = Mathf.Clamp(EditorGUILayout.IntField(newRows,GUILayout.Width(45)),1,128);
            if (GUILayout.Button("Tạo level",GUILayout.Width(90)))
            Run(() => { DeactivateCurrent(); map = STAMapActions.Create(newName,newColumns,newRows,palette); brush = 0; report = "Level mới đã tạo. Chọn Tô terrain hoặc một prefab để đặt."; });
            EditorGUILayout.EndHorizontal();
            DrawSavedLevels();
            DrawImport();
            if (!STAEditorUtility.SceneMap(map))
            { EditorGUILayout.HelpBox("Mở file map JSON để dựng tự động, hoặc tạo level trống để vẽ tay.",MessageType.Info); EditorGUILayout.EndScrollView(); return; }
            if (!map.gameObject.activeInHierarchy)
            {
                EditorGUILayout.HelpBox("Level này đang ẩn trong scene. Chuyển sang level này để vẽ.",MessageType.Info);
                if (GUILayout.Button("Chuyển sang level này")) Run(ActivateMap);
            }
            DrawMapSettings();
            EditorGUILayout.BeginHorizontal();
            brush = GUILayout.Toolbar(brush,new[] { "Chọn", "Tô terrain", "Xóa terrain", "Đặt object" });
            showGrid = GUILayout.Toggle(showGrid,"Lưới",GUILayout.Width(55));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Vẽ trong Scene view: kéo chuột trái để tô/xóa terrain; click để đặt object. Esc về Chọn. Ctrl+Z hoàn tác. Ctrl+S lưu scene.",MessageType.None);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(GUILayout.Width(Mathf.Max(270,position.width * .4f)));
            DrawPalette(); EditorGUILayout.EndVertical();
            EditorGUILayout.BeginVertical();
            objectScroll = EditorGUILayout.BeginScrollView(objectScroll,GUILayout.MinHeight(340));
            if (selectedObject != null && selectedObject.GetComponentInParent<STALevel>() != map) selectedObject = null;
            STAObjectGUI.Draw(selectedObject);
            EditorGUILayout.EndScrollView(); EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal(); EditorGUILayout.EndScrollView();
        }
        private void DrawSavedLevels()
        {
            EditorGUILayout.BeginHorizontal();
            string[] labels = savedLevels.Count == 0 ? new[] { "Chưa có level đã lưu" } : savedLevels.ConvertAll(go => System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(go))).ToArray();
            savedIndex = EditorGUILayout.Popup("Level đã lưu",Mathf.Clamp(savedIndex,0,labels.Length - 1),labels);
            using (new EditorGUI.DisabledScope(savedLevels.Count == 0))
                if (GUILayout.Button("Mở",GUILayout.Width(48)))
                    Run(OpenSavedLevel);
            if (GUILayout.Button("↻",GUILayout.Width(28))) ReloadLevels();
            using (new EditorGUI.DisabledScope(!STAEditorUtility.SceneMap(map)))
                if (GUILayout.Button("Lưu level prefab",GUILayout.Width(125))) Run(SaveLevel);
            EditorGUILayout.EndHorizontal();
        }
        private void Run(Action action)
        {
            try { action(); }
            catch (ExitGUIException) { throw; }
            catch (Exception ex) { report = ex.Message; }
        }
        private void OpenSavedLevel()
        {
            GameObject asset = savedLevels[savedIndex];
            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(asset,SceneManager.GetActiveScene());
            if (root == null) throw new InvalidOperationException("Không mở được level prefab.");
            Undo.RegisterCreatedObjectUndo(root,"Mở level");
            PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.OutermostRoot,InteractionMode.UserAction);
            DeactivateCurrent(); root.SetActive(true); map = root.GetComponent<STALevel>(); palette = map.palette;
            Selection.activeGameObject = root; brush = 0; STAMapActions.Frame(map);
            report = "Đang sửa bản trong scene. Bấm Lưu level prefab để cập nhật file level.";
        }
        private void DeactivateCurrent()
        {
            EndStroke(); selectedObject = null;
            if (STAEditorUtility.SceneMap(map))
            { Undo.RecordObject(map.gameObject,"Ẩn level trước"); map.gameObject.SetActive(false); STAEditorUtility.Dirty(map); }
        }
        private void ActivateMap()
        {
            STAEditorUtility.RequireMap(map);
            foreach (GameObject root in map.gameObject.scene.GetRootGameObjects())
                foreach (STALevel other in root.GetComponentsInChildren<STALevel>(true))
                {
                    Undo.RecordObject(other.gameObject,"Chuyển level");
                    other.gameObject.SetActive(other == map); STAEditorUtility.Dirty(other);
                }
            Selection.activeGameObject = map.gameObject; STAMapActions.Frame(map); RepaintAll();
        }
        private void ReloadLevels()
        {
            savedLevels.Clear(); string folder = STAEditorUtility.Content + "/Levels";
            if (!AssetDatabase.IsValidFolder(folder)) return;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab",new[] { folder }))
            {
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go != null && go.GetComponent<STALevel>() != null) savedLevels.Add(go);
            }
            savedLevels.Sort((a,b) => string.Compare(a.name,b.name,StringComparison.OrdinalIgnoreCase));
        }
        private void SaveLevel()
        {
            STAEditorUtility.RequireMap(map); STAEditorUtility.Folder(STAEditorUtility.Content + "/Levels");
            string path = EditorUtility.SaveFilePanelInProject("Lưu level prefab",STAEditorUtility.SafeName(map.levelName),"prefab","Chọn tên file level",STAEditorUtility.Content + "/Levels");
            if (string.IsNullOrEmpty(path)) return;
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (AssetDatabase.LoadMainAssetAtPath(path) != null && (existing == null || existing.GetComponent<STALevel>() == null))
                throw new InvalidOperationException("File này là asset khác, hãy chọn tên mới cho level.");
            bool success; PrefabUtility.SaveAsPrefabAsset(map.gameObject,path,out success);
            if (!success) throw new InvalidOperationException("Không lưu được level prefab.");
            AssetDatabase.SaveAssets(); ReloadLevels(); report = "Đã lưu level: " + path + ". Ctrl+S để lưu cả scene đang làm.";
        }
        private void DrawMapSettings()
        {
            STAPalette next = (STAPalette)EditorGUILayout.ObjectField("Danh sách prefab",map.palette != null ? map.palette : palette,typeof(STAPalette),false);
            if (map.palette != next) { Undo.RecordObject(map,"Đổi palette"); map.palette = next; palette = next; selectedAsset = -1; STAEditorUtility.Dirty(map); }
            else palette = next;
            gridSettings = EditorGUILayout.Foldout(gridSettings,"Tên level / lưới / collider terrain",true);
            if (!gridSettings) return;
            EditorGUI.BeginChangeCheck();
            string levelName = EditorGUILayout.TextField("Tên level",map.levelName);
            int columns = Mathf.Clamp(EditorGUILayout.IntField("Số cột",map.columns),1,128);
            int rows = Mathf.Clamp(EditorGUILayout.IntField("Số hàng",map.rows),1,128);
            bool empty = map.cells.Count == 0 && (map.objectRoot == null || map.objectRoot.childCount == 0);
            float size;
            using (new EditorGUI.DisabledScope(!empty)) size = Mathf.Max(.01f,EditorGUILayout.FloatField(new GUIContent("Kích thước ô (unit)","Đổi trước khi bắt đầu vẽ/đặt object."),map.cellSize));
            STACollider mode = (STACollider)EditorGUILayout.Popup("Collider terrain",(int)map.terrainCollider,STAEditorUtility.ColliderNames);
            float top = EditorGUILayout.Slider("Đỉnh collider (ô)",map.collisionTop,-.45f,.5f);
            float depth = Mathf.Max(.01f,EditorGUILayout.FloatField("Độ dày collider Z (ô)",map.collisionDepth));
            if (EditorGUI.EndChangeCheck())
            {
                bool fits = true; foreach (STACell p in map.cells) if (!STAGrid.Inside(p,columns,rows)) fits = false;
                if (!fits) { report = "Không thu nhỏ lưới vì còn terrain nằm ngoài kích thước mới. Xóa các ô đó trước."; }
                else
                {
                    Undo.RecordObject(map,"Chỉnh lưới map"); Undo.RecordObject(map.gameObject,"Đổi tên level"); map.levelName = levelName; map.gameObject.name = STAEditorUtility.SafeName(levelName); map.columns = columns; map.rows = rows;
                    map.cellSize = size; map.terrainCollider = mode; map.collisionTop = top; map.collisionDepth = depth; STAEditorUtility.Dirty(map); SceneView.RepaintAll();
                }
            }
            if (GUILayout.Button("Cập nhật terrain / collider sau khi đổi thiết lập"))
            Run(() => { STAMapActions.Rebuild(map); report = "Đã cập nhật terrain; các object được giữ nguyên."; });
        }
        private void DrawPalette()
        {
            EditorGUILayout.LabelField("ASSET",EditorStyles.boldLabel);
            search = EditorGUILayout.TextField("Tìm tên",search);
            category = EditorGUILayout.Popup("Lọc",category,new[] { "Tất cả", "Object", "Terrain" });
            paletteScroll = EditorGUILayout.BeginScrollView(paletteScroll,GUILayout.Height(250));
            if (palette != null)
            {
                for (int i = 0; i < palette.entries.Count; i++)
                {
                    STAPalette.Entry entry = palette.entries[i];
                    if (category == 1 && entry.role == STARole.Terrain || category == 2 && entry.role != STARole.Terrain) continue;
                    if (!string.IsNullOrEmpty(search) && (entry.label ?? "").IndexOf(search,StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Texture preview = entry.prefab != null ? AssetPreview.GetAssetPreview(entry.prefab) : null;
                    if (preview == null && entry.prefab != null) preview = AssetPreview.GetMiniThumbnail(entry.prefab);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(preview,GUILayout.Width(42),GUILayout.Height(42));
                    GUI.backgroundColor = selectedAsset == i ? new Color(.6f,.9f,1) : Color.white;
                    if (GUILayout.Button(new GUIContent(entry.prefab != null ? entry.label : entry.label + " [Missing]",STAEditorUtility.RoleNames[(int)entry.role]),GUILayout.Height(42)))
                    { selectedAsset = i; brush = entry.role == STARole.Terrain ? 1 : 3; }
                    GUI.backgroundColor = Color.white; EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndScrollView();
            if (palette == null) EditorGUILayout.HelpBox("Sang tab Tạo prefab để tạo danh sách từ item của bạn.",MessageType.Info);
            else
            {
                assetSettings = EditorGUILayout.Foldout(assetSettings,"Chỉnh asset đang chọn",true);
                if (assetSettings && selectedAsset >= 0 && selectedAsset < palette.entries.Count)
                {
                    SerializedObject so = new SerializedObject(palette); so.Update();
                    SerializedProperty e = so.FindProperty("entries").GetArrayElementAtIndex(selectedAsset);
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("label"),new GUIContent("Tên hiển thị"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("prefab"),new GUIContent("Prefab"));
                    SerializedProperty role = e.FindPropertyRelative("role"); role.enumValueIndex = EditorGUILayout.Popup("Loại",role.enumValueIndex,STAEditorUtility.RoleNames);
                    if (role.enumValueIndex == (int)STARole.Terrain)
                        EditorGUILayout.PropertyField(e.FindPropertyRelative("terrainKey"),new GUIContent("Mã khối tự nối","Tên Grass-* được bảng quy tắc dùng. Ví dụ Grass-Single, Grass-TopCenter."));
                    so.ApplyModifiedProperties();
                    EditorGUILayout.HelpBox("Áp dụng khi đặt object mới. Object đã đặt có thông số riêng bên phải.",MessageType.None);
                }
                addPrefab = (GameObject)EditorGUILayout.ObjectField("Thêm prefab lẻ",addPrefab,typeof(GameObject),false);
                using (new EditorGUI.DisabledScope(addPrefab == null)) if (GUILayout.Button("Thêm vào danh sách")) Run(AddPrefab);
            }
        }
        private void AddPrefab()
        {
            string path = AssetDatabase.GetAssetPath(addPrefab);
            if (!path.EndsWith(".prefab",StringComparison.OrdinalIgnoreCase) || !PrefabUtility.IsPartOfPrefabAsset(addPrefab)) throw new InvalidOperationException("Chọn file .prefab trong Project. Với FBX, dùng tab Tạo prefab.");
            string guid = AssetDatabase.AssetPathToGUID(path);
            int index = palette.entries.FindIndex(e => e.prefab == addPrefab || e.sourceGuid == guid);
            if (index < 0)
            {
                Undo.RecordObject(palette,"Thêm prefab vào palette"); STARole role = STAEditorUtility.GuessRole(addPrefab.name);
                palette.entries.Add(new STAPalette.Entry { sourceGuid = guid, label = addPrefab.name, prefab = addPrefab, role = role, terrainKey = role == STARole.Terrain ? addPrefab.name : "" });
                EditorUtility.SetDirty(palette); AssetDatabase.SaveAssets(); index = palette.entries.Count - 1;
            }
            selectedAsset = index; assetSettings = true; report = "Đã chọn " + addPrefab.name + " trong danh sách.";
        }
        private void SceneGUI(SceneView view)
        {
            if (tab != 1 || Application.isPlaying || !STAEditorUtility.SceneMap(map) || !map.gameObject.activeInHierarchy) return;
            Event e = Event.current;
            if (showGrid && e.type == EventType.Repaint)
            {
                Color old = Handles.color; Handles.color = new Color(.7f,.9f,1,.22f);
                for (int x = 0; x <= map.columns; x++) Line(new Vector3(x-.5f,-.5f,0),new Vector3(x-.5f,map.rows-.5f,0));
                for (int y = 0; y <= map.rows; y++) Line(new Vector3(-.5f,y-.5f,0),new Vector3(map.columns-.5f,y-.5f,0));
                Handles.color = old;
            }
            if (selectedObject != null && selectedObject.GetComponentInParent<STALevel>() == map && e.type == EventType.Repaint)
            {
                Matrix4x4 before = Handles.matrix; Color color = Handles.color;
                Handles.matrix = map.transform.localToWorldMatrix; Handles.color = Color.cyan;
                Vector3 center = map.transform.InverseTransformPoint(selectedObject.transform.position);
                Handles.DrawWireCube(center,new Vector3(Mathf.Max(.1f,selectedObject.footprint.x),Mathf.Max(.1f,selectedObject.footprint.y),.03f) * map.cellSize);
                if (selectedObject.role == STARole.Spawn || selectedObject.role == STARole.Teleport)
                    Handles.DrawWireCube(center + selectedObject.outletOffset * map.cellSize,Vector3.one * (.12f * map.cellSize));
                Handles.matrix = before; Handles.color = color;
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { brush = 0; EndStroke(); e.Use(); Repaint(); }
            if (brush == 0 || e.alt) return;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(control);
            if (e.type == EventType.MouseUp && ownedControl != 0) { EndStroke(); e.Use(); return; }
            Plane plane = new Plane(map.transform.forward,map.transform.position); float distance;
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition); if (!plane.Raycast(ray,out distance)) return;
            Vector3 local = map.transform.InverseTransformPoint(ray.GetPoint(distance)) / map.cellSize;
            STACell cell = new STACell(Mathf.RoundToInt(local.x),Mathf.RoundToInt(local.y));
            if (!STAGrid.Inside(cell,map.columns,map.rows)) return;
            if (e.type == EventType.Repaint)
            {
                Matrix4x4 old = Handles.matrix; Color color = Handles.color;
                Handles.matrix = map.transform.localToWorldMatrix; Handles.color = brush == 2 || e.shift ? Color.red : Color.yellow;
                Handles.DrawWireCube(new Vector3(cell.x,cell.y,0) * map.cellSize,new Vector3(1,1,.02f) * map.cellSize);
                Handles.matrix = old; Handles.color = color;
            }
            if (e.type == EventType.MouseMove) view.Repaint();
            if (e.type != EventType.MouseDown && e.type != EventType.MouseDrag || e.button != 0) return;
            if (e.type == EventType.MouseDown)
            { Undo.IncrementCurrentGroup(); strokeGroup = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName(brush == 3 ? "Đặt object" : "Vẽ terrain"); ownedControl = control; GUIUtility.hotControl = control; hasLast = false; }
            if (ownedControl == 0) return;
            try
            {
                if (brush == 3)
                {
                    if (e.type == EventType.MouseDown)
                    {
                        if (palette == null || selectedAsset < 0 || selectedAsset >= palette.entries.Count) throw new InvalidOperationException("Chọn một prefab trong danh sách trước.");
                        selectedObject = STAMapActions.Place(map,palette.entries[selectedAsset],cell);
                    }
                }
                else
                {
                    bool solid = brush == 1 && !e.shift;
                    int steps = hasLast ? Mathf.Max(Mathf.Abs(cell.x-lastCell.x),Mathf.Abs(cell.y-lastCell.y)) : 0;
                    if (!hasLast || steps == 0) STAMapActions.Paint(map,cell,solid);
                    else for (int i = 1; i <= steps; i++)
                        STAMapActions.Paint(map,new STACell(Mathf.RoundToInt(Mathf.Lerp(lastCell.x,cell.x,(float)i/steps)),Mathf.RoundToInt(Mathf.Lerp(lastCell.y,cell.y,(float)i/steps))),solid);
                    lastCell = cell; hasLast = true;
                }
            }
            catch (Exception ex) { report = ex.Message; brush = 0; EndStroke(); }
            e.Use(); RepaintAll();
        }
        private void Line(Vector3 a, Vector3 b) { Handles.DrawLine(map.transform.TransformPoint(a * map.cellSize),map.transform.TransformPoint(b * map.cellSize)); }
        private void EndStroke()
        {
            if (strokeGroup >= 0) Undo.CollapseUndoOperations(strokeGroup);
            if (ownedControl != 0 && GUIUtility.hotControl == ownedControl) GUIUtility.hotControl = 0;
            strokeGroup = -1; ownedControl = 0; hasLast = false;
        }
    }
}
