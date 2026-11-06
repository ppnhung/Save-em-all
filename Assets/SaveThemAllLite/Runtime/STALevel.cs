using System.Collections.Generic;
using UnityEngine;

namespace SaveThemAll.Lite
{
    [DisallowMultipleComponent]
    public sealed class STALevel : MonoBehaviour
    {
        [System.Serializable]
        public sealed class TerrainOverride { public STACell cell; public string key; }
        public string levelName = "Level_01";
        [Range(1,128)] public int columns = 9;
        [Range(1,128)] public int rows = 16;
        [Min(.01f)] public float cellSize = 1;
        public STAPalette palette;
        public STACollider terrainCollider = STACollider.Box3D;
        [Range(-.45f,.5f)] public float collisionTop = .5f;
        [Min(.01f)] public float collisionDepth = 1;
        [HideInInspector] public List<STACell> cells = new List<STACell>();
        [HideInInspector] public List<TerrainOverride> terrainOverrides = new List<TerrainOverride>();
        [HideInInspector] public string importedFile;
        [HideInInspector] public Transform terrainRoot;
        [HideInInspector] public Transform objectRoot;
        public Vector3 CellPoint(STACell cell)
        { return transform.TransformPoint(new Vector3(cell.x, cell.y, 0) * cellSize); }
        public string OverrideAt(STACell cell)
        {
            if (terrainOverrides != null) foreach (TerrainOverride item in terrainOverrides)
                if (item != null && item.cell.Equals(cell)) return item.key;
            return null;
        }
    }
}
