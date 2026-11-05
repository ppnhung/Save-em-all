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
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].model == model) return entries[i].prefab;
            return null;
        }
    }
}
