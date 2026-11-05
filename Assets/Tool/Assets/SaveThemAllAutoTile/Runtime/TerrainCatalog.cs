using System;
using System.Collections.Generic;
using UnityEngine;

namespace SaveThemAll.AutoTile
{
    [CreateAssetMenu(menuName = "Save Them All/Terrain Catalog")]
    public sealed class TerrainCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string model;
            public GameObject prefab;
        }

        public List<Entry> entries = new List<Entry>();

        public GameObject Find(string model)
        {
            if (entries == null) return null;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] != null && entries[i].model == model && entries[i].prefab != null)
                    return entries[i].prefab;
            return null;
        }
    }
}
