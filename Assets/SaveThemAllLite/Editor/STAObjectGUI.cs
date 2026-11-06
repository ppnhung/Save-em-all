using UnityEditor;
using UnityEngine;

namespace SaveThemAll.Lite.Editor
{
    internal static class STAObjectGUI
    {
        internal static void Draw(STAObject obj)
        {
            if (obj == null) { EditorGUILayout.HelpBox("Chọn một object đã đặt trong Scene hoặc Hierarchy để chỉnh.", MessageType.Info); return; }
            STALevel map = obj.GetComponentInParent<STALevel>();
            float cell = map != null ? Mathf.Max(.01f,map.cellSize) : 1;
            EditorGUILayout.LabelField("OBJECT ĐANG CHỌN",EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck(); string name = EditorGUILayout.TextField("Tên",obj.name);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(obj.gameObject,"Đổi tên object"); obj.name = name; STAEditorUtility.Dirty(obj); }
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("Prefab gốc",obj.sourcePrefab,typeof(GameObject),false);
            if (!string.IsNullOrEmpty(obj.importedId)) EditorGUILayout.LabelField("ID trong file map",obj.importedId);
            EditorGUI.BeginChangeCheck();
            Vector3 position = EditorGUILayout.Vector3Field(new GUIContent("Vị trí X/Y/Z (ô)","Tính từ gốc map; Z là chiều sâu."),obj.transform.localPosition / cell);
            Vector3 rotation = EditorGUILayout.Vector3Field("Xoay X/Y/Z (độ)",obj.transform.localEulerAngles);
            Vector3 scale = EditorGUILayout.Vector3Field("Tỉ lệ X/Y/Z (×)",obj.transform.localScale / cell);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(obj.transform,"Chỉnh transform object");
                obj.transform.localPosition = position * cell; obj.transform.localEulerAngles = rotation;
                obj.transform.localScale = new Vector3(NonZero(scale.x),NonZero(scale.y),NonZero(scale.z)) * cell;
                STAEditorUtility.Dirty(obj.transform); SceneView.RepaintAll();
            }
            SerializedObject so = new SerializedObject(obj); so.Update();
            SerializedProperty role = so.FindProperty("role");
            role.enumValueIndex = EditorGUILayout.Popup("Loại object",role.enumValueIndex,STAEditorUtility.RoleNames);
            STAEditorUtility.Property(so,"footprint","Vùng chiếm ô","Dùng để xem khung và nút Vừa vùng ô; không tự kéo giãn mesh.");
            switch ((STARole)role.enumValueIndex)
            {
                case STARole.Spawn:
                    STAEditorUtility.Property(so,"count","Số penguin");
                    STAEditorUtility.Property(so,"interval","Khoảng cách sinh (giây)");
                    STAEditorUtility.Property(so,"initialDelay","Chờ con đầu (giây)");
                    STAEditorUtility.Property(so,"startRight","Bắt đầu sang phải");
                    STAEditorUtility.Property(so,"outletOffset","Điểm ra X/Y/Z (ô)","Theo trục map, tính từ pivot object."); break;
                case STARole.Cage: STAEditorUtility.Property(so,"count","Số chim trong lồng"); break;
                case STARole.Exit: STAEditorUtility.Property(so,"count","Số chim cần cứu"); break;
                case STARole.Fan:
                    STAEditorUtility.Property(so,"force","Lực đẩy");
                    STAEditorUtility.Property(so,"direction","Hướng X/Y");
                    STAEditorUtility.Property(so,"range","Tầm tác động (ô)"); break;
                case STARole.Teleport:
                    STAEditorUtility.Property(so,"pairId","Mã cặp dịch chuyển");
                    STAEditorUtility.Property(so,"outletOffset","Điểm ra X/Y/Z (ô)"); break;
                case STARole.Stone: STAEditorUtility.Property(so,"hits","Số lần tác động"); break;
                case STARole.Stairs: STAEditorUtility.Property(so,"ascendsRight","Đi lên về bên phải"); break;
            }
            STAEditorUtility.Property(so,"note","Ghi chú");
            SerializedProperty mode = so.FindProperty("colliderMode");
            mode.enumValueIndex = EditorGUILayout.Popup("Collider của tool",mode.enumValueIndex,STAEditorUtility.ColliderNames);
            so.ApplyModifiedProperties();
            if (obj.authoredCollider3D != null)
            {
                BoxCollider box = obj.authoredCollider3D;
                EditorGUI.BeginChangeCheck();
                Vector3 center = EditorGUILayout.Vector3Field("Tâm collider 3D",box.center);
                Vector3 size = EditorGUILayout.Vector3Field("Kích thước collider 3D",box.size);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(box,"Chỉnh collider 3D"); box.center = center;
                    box.size = new Vector3(Mathf.Max(.001f,size.x),Mathf.Max(.001f,size.y),Mathf.Max(.001f,size.z));
                    STAEditorUtility.Dirty(box);
                }
            }
            if (obj.authoredCollider2D != null)
            {
                BoxCollider2D box = obj.authoredCollider2D;
                EditorGUI.BeginChangeCheck();
                Vector2 offset = EditorGUILayout.Vector2Field("Tâm collider 2D",box.offset);
                Vector2 size = EditorGUILayout.Vector2Field("Kích thước collider 2D",box.size);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(box,"Chỉnh collider 2D"); box.offset = offset;
                    box.size = new Vector2(Mathf.Max(.001f,size.x),Mathf.Max(.001f,size.y));
                    STAEditorUtility.Dirty(box);
                }
            }
            using (new EditorGUI.DisabledScope(!STAEditorUtility.SceneMap(map) || Application.isPlaying))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Vừa vùng ô")) Run(() => STAMapActions.Fit(obj));
                if (GUILayout.Button("Chạm sàn")) Run(() => STAMapActions.Ground(obj));
                EditorGUILayout.EndHorizontal();
                if (GUILayout.Button("Tạo / cập nhật collider theo mesh")) Run(() => STAMapActions.UpdateCollider(obj));
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Chọn root object")) Selection.activeGameObject = obj.gameObject;
                if (GUILayout.Button("Nhân bản")) Run(() => STAMapActions.Duplicate(obj));
                if (GUILayout.Button("Xóa")) { Undo.DestroyObjectImmediate(obj.gameObject); if (map != null) STAEditorUtility.Dirty(map); GUIUtility.ExitGUI(); }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.HelpBox("Count, lực đẩy, mã cặp… là dữ liệu thiết kế. Bộ này chưa chạy spawn/di chuyển hay logic gameplay. Collider có sẵn trong prefab được giữ; kiểm tra trước khi thêm collider của tool.",MessageType.None);
        }
        private static void Run(System.Action action)
        {
            try { action(); }
            catch (System.Exception ex) { EditorUtility.DisplayDialog("Chưa thực hiện",ex.Message,"Đóng"); }
        }
        private static float NonZero(float v) { return Mathf.Abs(v) < .001f ? (v < 0 ? -.001f : .001f) : v; }
    }
    [CustomEditor(typeof(STAObject))]
    public sealed class STAObjectInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            try { using (new EditorGUI.DisabledScope(Application.isPlaying)) STAObjectGUI.Draw((STAObject)target); }
            catch (ExitGUIException) { throw; }
            catch (System.Exception ex) { EditorGUILayout.HelpBox(ex.Message,MessageType.Warning); }
        }
    }
    [CustomEditor(typeof(STALevel))]
    public sealed class STALevelInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            STALevel level = (STALevel)target;
            EditorGUILayout.LabelField(level.levelName,EditorStyles.boldLabel);
            EditorGUILayout.LabelField(level.columns + " × " + level.rows + " | " + level.cells.Count + " ô terrain");
            if (GUILayout.Button("Mở Map Editor")) STALiteWindow.OpenMap();
            EditorGUILayout.HelpBox("Chỉnh level trong Map Editor. Ctrl+S lưu scene; Lưu level prefab tạo file level riêng.",MessageType.Info);
        }
    }
}
