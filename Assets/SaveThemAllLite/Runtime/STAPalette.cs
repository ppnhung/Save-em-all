using System;
using System.Collections.Generic;
using UnityEngine;

namespace SaveThemAll.Lite
{
    public enum STARole { Prop, Terrain, Spawn, Exit, Cage, Spikes, Stairs, Bridge, Fan, Stone, Teleport, Lava, Key }
    public enum STACollider { None, Box3D, Trigger3D, Box2D, Trigger2D }
    [CreateAssetMenu(menuName = "Save Them All Lite/Palette")]
    public sealed class STAPalette : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string sourceGuid;
            public string label;
            public GameObject prefab;
            public STARole role;
            public string terrainKey;
        }
        public List<Entry> entries = new List<Entry>();
        [Serializable]
        public sealed class ImportBinding
        { public string type; public GameObject prefab; public STARole role; }
        [HideInInspector] public List<ImportBinding> importBindings = new List<ImportBinding>();
        public GameObject Terrain(string key)
        {
            GameObject found = null;
            foreach (Entry e in entries)
            {
                if (e.role != STARole.Terrain || !string.Equals(e.terrainKey, key, StringComparison.OrdinalIgnoreCase)) continue;
                if (e.prefab == null) continue;
                if (found != null && found != e.prefab) return null; // An ambiguous binding must not silently win.
                found = e.prefab;
            }
            return found;
        }
    }
}
