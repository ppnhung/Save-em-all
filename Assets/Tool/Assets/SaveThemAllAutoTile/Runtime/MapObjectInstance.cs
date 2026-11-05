using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SaveThemAll.AutoTile
{
    // Layout metadata only. Gameplay systems read these values and apply their own behavior.
    public sealed class MapObjectInstance : MonoBehaviour
    {
        public MapObjectRecord data;
        public bool missingPrefab;
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (data == null) return;
            Matrix4x4 previous = Gizmos.matrix;
            // The footprint does not rotate when the visual does. Same contract as the web editor.
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.parent != null ? transform.parent.rotation : Quaternion.identity, transform.lossyScale);
            Gizmos.color = missingPrefab ? new Color(1f, 0.65f, 0.2f, 0.8f) : new Color(0.4f, 0.9f, 0.8f, 0.5f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(data.width, data.height, 0.15f));
            Gizmos.matrix = previous;
            if (missingPrefab) Handles.Label(transform.position, data.type + " (assign prefab)");
        }
#endif
    }
}
