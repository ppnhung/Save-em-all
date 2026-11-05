using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SaveThemAll.AutoTile.Editor
{
    // Exact normalized aliases only. Never substitute a visually unrelated item.
    internal static class ObjectPrefabNames
    {
        internal static readonly Dictionary<string, string[]> Aliases = new Dictionary<string, string[]>
        {
            { "spawn", new[] { "Spawn", "EnterPipe", "SpawnPipe", "EntryPipe", "EntrancePipe" } },
            { "exit", new[] { "Exit", "ExitPipe", "ExitGate", "GoalGate" } },
            { "cage", new[] { "Cage", "PenguinCage", "Cage_anim1" } },
            { "stone", new[] { "Stone", "StoneBlock", "BreakableStone", "BreakableBlock", "DigBlock", "RockBlock" } },
            { "spikes", new[] { "Spikes", "Spike", "IceSpikes", "SpikeTrap" } },
            { "lava", new[] { "Lava", "LavaSingle", "LavaSingle-Floating" } },
            { "portal", new[] { "Portal", "TeleportPortal", "TeleportPipe" } },
            { "fan", new[] { "Fan", "WindFan" } },
            { "bridge", new[] { "Bridge", "WoodBridge", "WoodenBridge" } },
            { "key", new[] { "Key", "GoldenKey", "PenguinKey" } }
        };
        internal static string Normalize(string name)
        {
            var text = new StringBuilder();
            foreach (char c in (name ?? "")) if (char.IsLetterOrDigit(c)) text.Append(char.ToLowerInvariant(c));
            return text.ToString();
        }
        internal static int Score(string type, string name)
        {
            string[] aliases;
            if (!Aliases.TryGetValue(type, out aliases)) return -1;
            string normalized = Normalize(name);
            for (int i = 0; i < aliases.Length; i++)
            {
                string alias = Normalize(aliases[i]);
                if (normalized == alias) return 1000 - i * 10;
                if (normalized == "pf" + alias || normalized == alias + "prefab") return 999 - i * 10;
            }
            return -1;
        }
    }

    internal static class ObjectCatalogBindingUtility
    {
        internal sealed class Report
        {
            public int linked, preserved;
            public readonly List<string> unresolved = new List<string>();
            public readonly List<string> details = new List<string>();
            public override string ToString()
            {
                return "Đã nối " + linked + " loại; giữ " + preserved + " liên kết đang có." +
                    (unresolved.Count == 0 ? "" : " Còn thiếu/chưa rõ: " + string.Join(", ", unresolved.ToArray()) + ".") +
                    (details.Count == 0 ? "" : "\n" + string.Join("\n", details.ToArray()));
            }
        }
        internal static bool IsPrefab(GameObject prefab)
        {
            return prefab != null && AssetDatabase.Contains(prefab) && PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.NotAPrefab;
        }
        internal static void ValidateCatalog(MapObjectCatalog catalog)
        {
            if (catalog == null) throw new ArgumentException("Chọn hoặc tạo Object Catalog trước.");
            if (catalog.entries == null) throw new ArgumentException("Object Catalog thiếu danh sách entries.");
            var ids = new HashSet<string>();
            foreach (MapObjectCatalog.Entry e in catalog.entries)
                if (e != null && !string.IsNullOrEmpty(e.type) && !ids.Add(e.type)) throw new ArgumentException("Object Catalog bị trùng type: " + e.type);
        }
        internal static MapObjectCatalog.Entry Resolve(MapObjectCatalog catalog, string type)
        {
            MapObjectCatalog.Entry entry = catalog != null ? catalog.Find(type) : null;
            if (entry != null && entry.prefab != null) return entry;
            if (!MapAssetDefinition.IsPrefabId(type)) return null;
            string path = AssetDatabase.GUIDToAssetPath(type.Substring(7));
            if (string.IsNullOrEmpty(path)) return null;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!IsPrefab(prefab)) return null;
            // An empty GUID entry must not hide a valid GUID fallback. Preserve its adjustments.
            return new MapObjectCatalog.Entry { type = type, prefab = prefab,
                localOffset = entry != null ? entry.localOffset : Vector3.zero,
                localEulerAngles = entry != null ? entry.localEulerAngles : Vector3.zero,
                scale = entry != null ? entry.scale : Vector3.one };
        }
        internal static MapObjectCatalog.Entry SetPrefab(MapObjectCatalog catalog, string type, GameObject prefab)
        {
            ValidateCatalog(catalog);
            if (prefab != null && !IsPrefab(prefab)) throw new ArgumentException("Chọn prefab/model asset trong Project, không chọn object ở Hierarchy.");
            Undo.RecordObject(catalog, "Assign map object prefab");
            MapObjectCatalog.Entry entry = catalog.Find(type);
            if (entry == null) { entry = NewEntry(type); catalog.entries.Add(entry); }
            entry.prefab = prefab;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return entry;
        }
        private static MapObjectCatalog.Entry NewEntry(string type)
        {
            var e = new MapObjectCatalog.Entry { type = type, width = type == "bridge" ? 2 : 1 };
            if (TerrainMapDocument.KnownType(type)) e.role = (MapObjectRole)Enum.Parse(typeof(MapObjectRole), type);
            return e;
        }
        internal static Report AutoLink(MapObjectCatalog catalog, IEnumerable<string> requestedTypes, string folder = "Assets", bool recordUndo = true)
        {
            ValidateCatalog(catalog);
            if (string.IsNullOrEmpty(folder)) folder = "Assets";
            if (!(folder == "Assets" || folder.StartsWith("Assets/", StringComparison.Ordinal)) || !AssetDatabase.IsValidFolder(folder)) throw new ArgumentException("Chọn thư mục prefab nằm trong Assets.");
            var prefabs = new List<GameObject>();
            var seenPaths = new HashSet<string>();
            var guids = new HashSet<string>(AssetDatabase.FindAssets("t:Prefab", new[] { folder }));
            guids.UnionWith(AssetDatabase.FindAssets("t:GameObject", new[] { folder }));
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || !seenPaths.Add(path)) continue;
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (IsPrefab(prefab)) prefabs.Add(prefab);
            }
            var report = new Report();
            var planned = new Dictionary<string, GameObject>();
            foreach (string type in new HashSet<string>(requestedTypes))
            {
                MapObjectCatalog.Entry existing = catalog.Find(type);
                if (existing != null && IsPrefab(existing.prefab)) { report.preserved++; continue; }
                if (existing != null && existing.prefab != null) { report.unresolved.Add(type); report.details.Add(type + ": liên kết đang có không phải prefab asset; hãy gán lại."); continue; }
                MapObjectCatalog.Entry byGuid = Resolve(catalog, type);
                if (byGuid != null && IsPrefab(byGuid.prefab)) { planned[type] = byGuid.prefab; continue; }
                // An explicit gameplay role in this catalog is stronger evidence than a filename.
                var roleMatches = new HashSet<GameObject>();
                if (TerrainMapDocument.KnownType(type))
                    foreach (MapObjectCatalog.Entry entry in catalog.entries)
                        if (entry != null && entry.role.ToString() == type && IsPrefab(entry.prefab)) roleMatches.Add(entry.prefab);
                if (roleMatches.Count == 1) { foreach (GameObject prefab in roleMatches) planned[type] = prefab; continue; }
                if (roleMatches.Count > 1) { report.unresolved.Add(type); report.details.Add(type + ": nhiều prefab được gán cùng role; chọn một prefab trong bảng liên kết."); continue; }
                int best = -1;
                var matches = new List<GameObject>();
                foreach (GameObject prefab in prefabs)
                {
                    int score = ObjectPrefabNames.Score(type, Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(prefab)));
                    if (score < 0 || score < best) continue;
                    if (score > best) { best = score; matches.Clear(); }
                    matches.Add(prefab);
                }
                if (matches.Count == 1) planned[type] = matches[0];
                else
                {
                    report.unresolved.Add(type);
                    report.details.Add(type + (matches.Count == 0 ? ": chưa tìm thấy prefab phù hợp trong " + folder + "." : ": có nhiều prefab trùng tên phù hợp; cần chọn đúng mục."));
                }
            }
            // Resolve everything first, then fill only empty entries; never replace a valid binding.
            if (planned.Count > 0)
            {
                if (recordUndo) Undo.RecordObject(catalog, "Auto-link missing map prefabs");
                foreach (KeyValuePair<string, GameObject> pair in planned)
                {
                    MapObjectCatalog.Entry entry = catalog.Find(pair.Key);
                    if (entry == null) { entry = NewEntry(pair.Key); catalog.entries.Add(entry); }
                    entry.prefab = pair.Value;
                    report.linked++;
                    report.details.Add(pair.Key + " → " + AssetDatabase.GetAssetPath(pair.Value));
                }
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
            }
            return report;
        }
    }
}
