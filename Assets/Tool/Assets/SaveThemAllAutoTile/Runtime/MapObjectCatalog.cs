using System;
using System.Collections.Generic;
using UnityEngine;

namespace SaveThemAll.AutoTile
{
    [CreateAssetMenu(menuName = "Save Them All/Map Object Catalog")]
    public sealed class MapObjectCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Exact web ID: lava, stone, spikes, cage, spawn, exit, portal, fan, bridge, key")]
            public string type;
            public GameObject prefab;
            [Tooltip("Offset from footprint center, in cell units; use this to correct the prefab pivot.")]
            public Vector3 localOffset;
            public Vector3 localEulerAngles;
            public Vector3 scale = Vector3.one;
        }
        public List<Entry> entries = new List<Entry>();
        public Entry Find(string type)
        {
            foreach (Entry e in entries) if (e != null && e.type == type) return e;
            return null;
        }
    }
}
