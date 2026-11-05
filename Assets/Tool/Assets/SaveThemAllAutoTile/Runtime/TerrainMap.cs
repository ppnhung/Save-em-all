using System;
using System.Collections.Generic;
using UnityEngine;

namespace SaveThemAll.AutoTile
{
    [DisallowMultipleComponent]
    public sealed class TerrainMap : MonoBehaviour
    {
        public enum CollisionMode { None, GridRows3D, GridRows2D }

        [Serializable]
        public sealed class Cell
        {
            public Vector2Int position;
            [Tooltip("Empty = automatic. Exact catalog model name = visual override.")]
            public string overrideModel;
        }

        public TerrainCatalog catalog;
        [Min(1)] public int width = 9;
        [Min(1)] public int height = 12;
        [Min(0.01f)] public float cellSize = 1f;
        public CollisionMode collisionMode = CollisionMode.GridRows3D;
        [Min(0.01f)] public float collisionDepth = 1f;
        [Range(0, 31)] public int collisionLayer;
        [Tooltip("Grid collider top relative to the cell center, in cell units.")]
        [Range(-0.45f, 0.5f)] public float collisionTop = 0.5f;
        [HideInInspector] public List<Cell> cells = new List<Cell>();
        [SerializeField, HideInInspector] private Transform visualRoot;
        [SerializeField, HideInInspector] private Transform collisionRoot;

        private readonly Dictionary<Vector2Int, Cell> data = new Dictionary<Vector2Int, Cell>();
        private readonly Dictionary<Vector2Int, TerrainTileInstance> instances = new Dictionary<Vector2Int, TerrainTileInstance>();
        private bool cacheReady;
        public int CellCount { get { return cells.Count; } }

        private void Awake()
        {
            LoadCaches();
            if (Application.isPlaying && (visualRoot == null || instances.Count != data.Count)) Rebuild();
        }

        public bool InBounds(Vector2Int p)
        {
            return p.x >= 0 && p.x < width && p.y >= 0 && p.y < height;
        }

        public bool HasCell(Vector2Int p)
        {
            EnsureCache();
            return InBounds(p) && data.ContainsKey(p);
        }

        public string OverrideAt(Vector2Int p)
        {
            EnsureCache();
            Cell c;
            return data.TryGetValue(p, out c) ? c.overrideModel : null;
        }

        public int MaskAt(Vector2Int p)
        {
            EnsureCache();
            int mask = 0;
            for (int i = 0; i < 8; i++)
                if (HasCell(p + new Vector2Int(TerrainRules.DX[i], TerrainRules.DY[i]))) mask |= 1 << i;
            return mask;
        }

        public string ModelAt(Vector2Int p)
        {
            EnsureCache();
            Cell c;
            if (!data.TryGetValue(p, out c) || !InBounds(p)) return null;
            return string.IsNullOrEmpty(c.overrideModel) ? TerrainRules.Resolve(MaskAt(p)) : c.overrideModel;
        }

        public Vector3 CellToWorld(Vector2Int p)
        {
            return transform.TransformPoint(new Vector3(p.x * cellSize, p.y * cellSize, 0));
        }

        public Vector2Int WorldToCell(Vector3 world)
        {
            Vector3 p = transform.InverseTransformPoint(world) / Mathf.Max(0.01f, cellSize);
            return new Vector2Int(Mathf.FloorToInt(p.x + 0.5f), Mathf.FloorToInt(p.y + 0.5f));
        }

        // Safe to call from gameplay. Only the affected 3x3 visuals are refreshed.
        // Collision rows are rebuilt, which is inexpensive for small puzzle maps.
        public bool SetCell(Vector2Int p, bool solid, string overrideModel = null)
        {
            EnsureCache();
            if (!InBounds(p)) return false;
            Cell existing;
            bool found = data.TryGetValue(p, out existing);
            string key = string.IsNullOrWhiteSpace(overrideModel) ? null : overrideModel.Trim();
            if (solid && catalog != null && catalog.Find(key ?? TerrainRules.Resolve(MaskAt(p))) == null)
            {
                Debug.LogError("Missing terrain prefab: " + key + ". Run Setup Kit or fix the catalog.", this);
                return false;
            }
            if (solid && found && string.Equals(existing.overrideModel ?? "", key ?? "")) return false;
            if (!solid && !found) return false;
            if (solid)
            {
                if (found) existing.overrideModel = key;
                else
                {
                    Cell c = new Cell { position = p, overrideModel = key };
                    cells.Add(c);
                    data.Add(p, c);
                }
            }
            else { cells.Remove(existing); data.Remove(p); }
            EnsureRoots();
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++) RefreshVisual(p + new Vector2Int(x, y));
            RebuildCollision();
            return true;
        }

        // Rows are supplied from top to bottom. '#' = terrain; '.' = empty.
        public void LoadLayout(string[] rows)
        {
            if (rows == null || rows.Length == 0) throw new ArgumentException("Layout has no rows.");
            int newWidth = rows[0].Length;
            if (newWidth == 0) throw new ArgumentException("Layout has no columns.");
            for (int i = 0; i < rows.Length; i++)
                if (rows[i].Length != newWidth) throw new ArgumentException("Layout rows must have equal width.");
            width = newWidth; height = rows.Length;
            cells.Clear();
            for (int row = 0; row < height; row++)
                for (int x = 0; x < width; x++)
                    if (rows[row][x] == '#') cells.Add(new Cell { position = new Vector2Int(x, height - 1 - row) });
            Rebuild();
        }

        [ContextMenu("Rebuild Terrain")]
        public void Rebuild()
        {
            width = Mathf.Max(1, width); height = Mathf.Max(1, height); cellSize = Mathf.Max(0.01f, cellSize);
            LoadCaches();
            EnsureRoots();
            ClearChildren(visualRoot);
            instances.Clear();
            foreach (Vector2Int p in data.Keys) if (InBounds(p)) RefreshVisual(p);
            RebuildCollision();
        }

        public void LoadCaches()
        {
            data.Clear(); instances.Clear();
            foreach (Cell c in cells) if (c != null) data[c.position] = c;
            if (visualRoot != null)
                foreach (TerrainTileInstance t in visualRoot.GetComponentsInChildren<TerrainTileInstance>(true))
                    instances[t.cell] = t;
            cacheReady = true;
        }

        private void EnsureCache() { if (!cacheReady) LoadCaches(); }

        private void EnsureRoots()
        {
            if (visualRoot == null) visualRoot = MakeRoot("Visuals (generated)");
            if (collisionRoot == null) collisionRoot = MakeRoot("Collision (generated)");
        }

        private Transform MakeRoot(string rootName)
        {
            Transform t = new GameObject(rootName).transform;
            t.SetParent(transform, false);
            return t;
        }

        private void RefreshVisual(Vector2Int p)
        {
            string key = ModelAt(p);
            TerrainTileInstance old;
            if (instances.TryGetValue(p, out old))
            {
                if (old != null && old.model == key) return;
                if (old != null) RemoveObject(old.gameObject);
                instances.Remove(p);
            }
            if (key == null) return;
            GameObject prefab = catalog == null ? null : catalog.Find(key);
            if (prefab == null)
            {
                Debug.LogError("Missing terrain prefab: " + key + ". Run Setup Kit or fix the catalog.", this);
                return;
            }
            GameObject go;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, visualRoot);
            else
#endif
                go = Instantiate(prefab, visualRoot);
            go.name = p.x + "," + p.y + " | " + key;
            go.transform.localPosition = new Vector3(p.x * cellSize, p.y * cellSize, 0);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * cellSize;
            TerrainTileInstance tag = go.GetComponent<TerrainTileInstance>();
            if (tag == null) tag = go.AddComponent<TerrainTileInstance>();
            tag.cell = p; tag.model = key;
            instances[p] = tag;
        }

        private void RebuildCollision()
        {
            EnsureRoots();
            ClearChildren(collisionRoot);
            if (collisionMode == CollisionMode.None) return;
            for (int y = 0; y < height; y++)
            {
                int x = 0;
                while (x < width)
                {
                    if (!HasCell(new Vector2Int(x, y))) { x++; continue; }
                    int first = x;
                    while (x < width && HasCell(new Vector2Int(x, y))) x++;
                    int count = x - first;
                    GameObject go = new GameObject("Row " + y + " [" + first + ".." + (x - 1) + "]");
                    go.layer = collisionLayer;
                    go.transform.SetParent(collisionRoot, false);
                    // Default top = 0.5 keeps neighboring collision rows contiguous.
                    // Optional offsets are for a custom controller, not mesh-exact collision.
                    go.transform.localPosition = new Vector3((first + (count - 1) * 0.5f) * cellSize,
                        (y + (collisionTop - 0.5f) * 0.5f) * cellSize, 0);
                    Vector3 size = new Vector3(count * cellSize, (collisionTop + 0.5f) * cellSize,
                        collisionDepth * cellSize);
                    if (collisionMode == CollisionMode.GridRows3D) go.AddComponent<BoxCollider>().size = size;
                    else go.AddComponent<BoxCollider2D>().size = new Vector2(size.x, size.y);
                }
            }
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--) RemoveObject(parent.GetChild(i).gameObject);
        }

        private static void RemoveObject(GameObject go)
        {
            // Disable first so deferred Destroy cannot leave duplicate colliders this frame.
            go.SetActive(false);
            go.transform.SetParent(null, true);
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
    }
}
