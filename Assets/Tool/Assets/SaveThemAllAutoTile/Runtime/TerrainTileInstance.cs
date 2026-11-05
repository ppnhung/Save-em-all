using UnityEngine;

namespace SaveThemAll.AutoTile
{
    // Marker on generated instances only. User props live outside Visuals.
    [AddComponentMenu("")]
    public sealed class TerrainTileInstance : MonoBehaviour
    {
        public Vector2Int cell;
        public string model;
    }
}
