using UnityEngine;

namespace SaveThemAll.Lite
{
    // Authoring data only. A gameplay script can read these values later.
    // No automatic spawning, movement, damage, or animation is run by this component.
    [DisallowMultipleComponent]
    public sealed class STAObject : MonoBehaviour
    {
        public GameObject sourcePrefab;
        [HideInInspector] public string importedId, importedType;
        public STARole role;
        public Vector2 footprint = Vector2.one;
        public string note;
        [Min(0)] public int count = 6;
        [Min(.05f)] public float interval = .35f;
        [Min(0)] public float initialDelay;
        public bool startRight = true;
        public Vector3 outletOffset;
        public string pairId = "A";
        [Min(0)] public float force = 6;
        public Vector2 direction = Vector2.up;
        [Min(0)] public float range = 3;
        [Min(1)] public int hits = 1;
        public bool ascendsRight = true;
        public STACollider colliderMode;
        [HideInInspector] public BoxCollider authoredCollider3D;
        [HideInInspector] public BoxCollider2D authoredCollider2D;
    }
}
