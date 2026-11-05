using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SaveThemAll.AutoTile.Editor
{
    public static partial class TerrainKitSetup
    {
        [MenuItem("Tools/Save Them All/Repair Selected Terrain Catalog")]
        public static void RepairSelectedTerrainCatalog()
        {
            try { Debug.Log(RepairTerrainCatalog(GetSelectedCatalog())); }
            catch (Exception ex) { Debug.LogError(ex.Message); }
        }

        // Fill missing bindings in the existing asset. Never replace the catalog or
        // touch Object Catalog, gameplay objects, source models or existing prefabs.
        public static string RepairTerrainCatalog(TerrainCatalog catalog)
        {
            if (catalog == null) throw new InvalidOperationException("Select a Terrain Catalog first.");
            string catalogPath = AssetDatabase.GetAssetPath(catalog);
            if (string.IsNullOrEmpty(catalogPath) || !catalogPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("Terrain Catalog must be a saved asset inside Assets.");
            string kitRoot = FindKitRoot();
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string raw in Directory.GetFiles(kitRoot + "/Models", "*.fbx", SearchOption.AllDirectories))
            {
                string path = raw.Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(path);
                if (sources.ContainsKey(name)) throw new InvalidOperationException("Duplicate source FBX: " + name);
                sources.Add(name, path);
            }
            // A partial Models copy must not silently produce an incomplete catalog.
            for (int mask = 0; mask < 256; mask++)
                if (!sources.ContainsKey(TerrainRules.Resolve(mask)))
                    throw new InvalidOperationException("Missing kit source: " + TerrainRules.Resolve(mask) +
                        ". Copy the Models folder from the ZIP into this kit folder (keep existing .meta files).");
            var missing = new List<string>();
            foreach (string name in sources.Keys) if (catalog.Find(name) == null) missing.Add(name);
            missing.Sort(StringComparer.Ordinal);
            if (missing.Count == 0) return "Terrain Catalog đã đủ liên kết của bộ kit. Không cần tạo lại prefab.";
            // Resolve all model/texture dependencies before creating output or editing the catalog.
            foreach (string name in missing)
            {
                string geometry = name == "Grass-Edge-Seam1BottomSRight" ? sources["Grass-Edge-Seam1BottomSLeft"] : sources[name];
                if (AssetDatabase.LoadAssetAtPath<GameObject>(geometry) == null)
                    throw new InvalidOperationException("Unity has not imported: " + geometry);
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(Path.ChangeExtension(sources[name], ".png")) == null)
                    throw new InvalidOperationException("Missing PNG for " + name + ". Restore the kit Models folder first.");
            }
            EnsureFolder(kitRoot + "/Generated");
            string output = AssetDatabase.GenerateUniqueAssetPath(kitRoot + "/Generated/CatalogRepair");
            EnsureFolder(output); EnsureFolder(output + "/Prefabs"); EnsureFolder(output + "/Materials");
            Shader shader = CreateVolumeShader(kitRoot, output + "/Materials");
            var materials = new Dictionary<string, Material>();
            var prepared = new List<TerrainCatalog.Entry>();
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                for (int i = 0; i < missing.Count; i++)
                {
                    string name = missing[i];
                    EditorUtility.DisplayProgressBar("Repair Terrain Catalog", name, (float)i / missing.Count);
                    GameObject prefab = CreateNormalizedPrefab(name, sources[name], sources, output, shader, materials, preview);
                    prepared.Add(new TerrainCatalog.Entry { model = name, prefab = prefab });
                }
            }
            finally { EditorUtility.ClearProgressBar(); EditorSceneManager.ClosePreviewScene(preview); }
            // Only commit references after every requested prefab has been created.
            // Generated assets remain on disk if the user undoes the catalog binding.
            Undo.RecordObject(catalog, "Repair Terrain Catalog");
            if (catalog.entries == null) catalog.entries = new List<TerrainCatalog.Entry>();
            foreach (TerrainCatalog.Entry item in prepared)
            {
                TerrainCatalog.Entry existing = catalog.entries.Find(e => e != null && e.model == item.model);
                if (existing == null) catalog.entries.Add(item);
                else if (existing.prefab == null) existing.prefab = item.prefab;
            }
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(catalog);
            return "Đã khôi phục " + prepared.Count + " liên kết terrain trong catalog đang chọn. " +
                "Map đã có trong scene: chọn root map rồi bấm Rebuild terrain in selected map. " +
                "Map chưa dựng: bấm Build map in scene. Prefab mới: " + output;
        }
    }
}
