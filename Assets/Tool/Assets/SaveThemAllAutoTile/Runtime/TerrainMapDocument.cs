using System;
using System.Collections.Generic;
using UnityEngine;

namespace SaveThemAll.AutoTile
{
    // Shared contract with Penguin Terrain Studio. Cell coordinates start at bottom-left.
    [Serializable]
    public sealed class TerrainMapDocument
    {
        public int schemaVersion;
        public string name;
        public int width, height;
        public float cellSize;
        public string origin;
        public TerrainRecord[] terrain;
        public MapObjectRecord[] objects;

        public static TerrainMapDocument Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 2097152)
                throw new ArgumentException("Empty JSON or file larger than 2 MB.");
            TerrainMapDocument doc = JsonUtility.FromJson<TerrainMapDocument>(json);
            if (doc == null) throw new ArgumentException("Invalid TerrainMap JSON.");
            doc.Validate();
            return doc;
        }

        public void Validate()
        {
            if (schemaVersion != 1 || origin != "bottom-left") throw new ArgumentException("Expected schemaVersion 1, origin bottom-left.");
            if (width < 1 || width > 64 || height < 1 || height > 64) throw new ArgumentException("Board must be 1-64 columns and rows.");
            if (!Finite(cellSize) || cellSize < 0.01f || cellSize > 100f) throw new ArgumentException("cellSize must be 0.01-100.");
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new ArgumentException("Map name must contain 1-80 characters.");
            if (terrain == null || objects == null || terrain.Length > 4096 || objects.Length > 4096) throw new ArgumentException("Missing or oversized terrain / objects array.");
            HashSet<string> modelNames = new HashSet<string>();
            for (int i = 0; i < 256; i++) modelNames.Add(TerrainRules.Resolve(i));
            HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
            foreach (TerrainRecord c in terrain)
            {
                if (c == null || !Contains(c.x, c.y, 1, 1)) throw new ArgumentException("Terrain cell is outside board.");
                if (!cells.Add(new Vector2Int(c.x, c.y))) throw new ArgumentException("Duplicate terrain cell.");
                if (!string.IsNullOrEmpty(c.overrideModel) && !modelNames.Contains(c.overrideModel)) throw new ArgumentException("Unknown terrain override: " + c.overrideModel);
            }
            HashSet<string> ids = new HashSet<string>();
            HashSet<Vector2Int> occupied = new HashSet<Vector2Int>();
            foreach (MapObjectRecord o in objects)
            {
                if (o == null || !KnownType(o.type)) throw new ArgumentException("Unknown object type.");
                if (string.IsNullOrEmpty(o.id) || o.id.Length > 100 || !ids.Add(o.id)) throw new ArgumentException("Missing or duplicate object ID.");
                if (!Contains(o.x, o.y, o.width, o.height)) throw new ArgumentException("Object outside board: " + o.id);
                if (o.rotation != 0 && o.rotation != 90 && o.rotation != 180 && o.rotation != 270) throw new ArgumentException("Invalid rotation: " + o.id);
                if (o.count < 0 || o.count > 999 || o.pairId == null || o.pairId.Length > 40) throw new ArgumentException("Invalid count or pair ID: " + o.id);
                if (!Finite(o.offsetX) || !Finite(o.offsetY) || Mathf.Abs(o.offsetX) > 1 || Mathf.Abs(o.offsetY) > 1) throw new ArgumentException("Invalid object offset.");
                for (int x = o.x; x < o.x + o.width; x++)
                    for (int y = o.y; y < o.y + o.height; y++)
                        if (!occupied.Add(new Vector2Int(x, y))) throw new ArgumentException("Objects overlap on grid.");
            }
        }
        public bool Contains(int x, int y, int w, int h)
        { return w >= 1 && h >= 1 && w <= 64 && h <= 64 && x >= 0 && y >= 0 && x <= width - w && y <= height - h; }
        private static bool Finite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
        public static readonly string[] ObjectTypes = { "lava", "stone", "spikes", "cage", "spawn", "exit", "portal", "fan", "bridge", "key" };
        public static bool KnownType(string type) { return Array.IndexOf(ObjectTypes, type) >= 0; }
    }
    [Serializable]
    public sealed class TerrainRecord { public int x, y; public string overrideModel; }
    [Serializable]
    public sealed class MapObjectRecord
    {
        public string id, type;
        public int x, y, width, height, rotation, count;
        public string pairId;
        public float offsetX, offsetY;
    }
}
