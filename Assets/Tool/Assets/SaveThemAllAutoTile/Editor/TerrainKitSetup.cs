using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SaveThemAll.AutoTile.Editor
{
    public static partial class TerrainKitSetup
    {
        [MenuItem("Tools/Save Them All/Setup Kit + Create Demo")]
        public static void Setup()
        {
            string kitRoot = FindKitRoot();
            string[] paths = Directory.GetFiles(kitRoot + "/Models", "*.fbx", SearchOption.AllDirectories);
            Array.Sort(paths, StringComparer.Ordinal);
            if (paths.Length == 0) throw new InvalidOperationException("No FBX models found in the kit.");
            var sourceByName = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string sourcePath in paths)
            {
                string modelName = Path.GetFileNameWithoutExtension(sourcePath);
                if (sourceByName.ContainsKey(modelName))
                    throw new InvalidOperationException("Duplicate FBX name inside this kit: " + modelName);
                sourceByName.Add(modelName, sourcePath.Replace('\\', '/'));
            }
            // Resolve inputs before creating output assets, including nested Models folders.
            for (int mask = 0; mask < 256; mask++)
                if (!sourceByName.ContainsKey(TerrainRules.Resolve(mask)))
                    throw new InvalidOperationException("Missing required FBX: " + TerrainRules.Resolve(mask) + " under " + kitRoot + "/Models");
            Debug.Log("Using terrain kit: " + kitRoot);
            EnsureFolder(kitRoot + "/Generated");
            string output = AssetDatabase.GenerateUniqueAssetPath(kitRoot + "/Generated/Kit");
            EnsureFolder(output); EnsureFolder(output + "/Prefabs"); EnsureFolder(output + "/Materials");
            Shader volumeShader = CreateVolumeShader(kitRoot, output + "/Materials");
            TerrainCatalog catalog = ScriptableObject.CreateInstance<TerrainCatalog>();
            var materialByHash = new Dictionary<string, Material>();
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    string path = paths[i].Replace('\\', '/');
                    string name = Path.GetFileNameWithoutExtension(path);
                    EditorUtility.DisplayProgressBar("Save Them All", "Preparing " + name, (float)i / paths.Length);
                    GameObject prefab = CreateNormalizedPrefab(name, path, sourceByName, output,
                        volumeShader, materialByHash, previewScene);
                    catalog.entries.Add(new TerrainCatalog.Entry { model = name, prefab = prefab });
                }
                AssetDatabase.CreateAsset(catalog, output + "/TerrainCatalog.asset");
                AssetDatabase.SaveAssets();
                ValidateCatalog(catalog);
                CreateDemo(catalog, output);
                Debug.Log("Terrain kit created at " + output + ". 256 neighbor cases, 68 automatic model variants; remaining models available as overrides. Original FBX files and UVs are unchanged.");
            }
            finally { EditorUtility.ClearProgressBar(); EditorSceneManager.ClosePreviewScene(previewScene); }
        }

        // Setup and recovery share the same geometry, seam and material rules.
        private static GameObject CreateNormalizedPrefab(string name, string path,
            Dictionary<string, string> sourceByName, string output, Shader volumeShader,
            Dictionary<string, Material> materialByHash, Scene previewScene)
        {
            // The supplied BottomSRight body has a different boundary profile.
            // Build its counterpart from BottomSLeft in the generated prefab only.
            bool mirrorCompatibleBody = name == "Grass-Edge-Seam1BottomSRight";
            string geometryPath = mirrorCompatibleBody
                ? sourceByName["Grass-Edge-Seam1BottomSLeft"] : path;
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(geometryPath);
            if (source == null) throw new InvalidOperationException("Unity has not imported: " + geometryPath);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Path.ChangeExtension(path, ".png"));
            if (texture == null) throw new InvalidOperationException("Missing PNG for " + name);
            string hash;
            using (SHA256 sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.ChangeExtension(path, ".png"))));
            Material material;
            if (!materialByHash.TryGetValue(hash, out material))
            {
                material = MakeMaterial(texture, volumeShader);
                AssetDatabase.CreateAsset(material, output + "/Materials/Atlas_" + materialByHash.Count + ".mat");
                materialByHash.Add(hash, material);
            }

            GameObject wrapper = new GameObject(name);
            SceneManager.MoveGameObjectToScene(wrapper, previewScene);
            try
            {
                GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(source, previewScene);
                visual.transform.SetParent(wrapper.transform, false);
                if (mirrorCompatibleBody)
                {
                    Vector3 scale = visual.transform.localScale; scale.x *= -1; visual.transform.localScale = scale;
                }
                if (!name.Contains("Background")) NormalizeBody(wrapper.transform, visual.transform);
                foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] assigned = new Material[renderer.sharedMaterials.Length];
                    for (int j = 0; j < assigned.Length; j++) assigned[j] = material;
                    renderer.sharedMaterials = assigned;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(wrapper, output + "/Prefabs/" + name + ".prefab");
                if (prefab == null) throw new InvalidOperationException("Could not save prefab: " + name);
                return prefab;
            }
            finally { UnityEngine.Object.DestroyImmediate(wrapper); }
        }

        private static string FindKitRoot()
        {
            const string suffix = "/Editor/TerrainKitSetup.cs";
            var candidates = new List<string>();
            string[] scripts = AssetDatabase.FindAssets("TerrainKitSetup t:MonoScript", new[] { "Assets" });
            foreach (string guid in scripts)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (!path.EndsWith(suffix, StringComparison.Ordinal)) continue;
                string root = path.Substring(0, path.Length - suffix.Length);
                if (AssetDatabase.IsValidFolder(root + "/Models") && !candidates.Contains(root))
                    candidates.Add(root);
            }
            if (candidates.Count == 1) return candidates[0];
            if (candidates.Count == 0)
                throw new InvalidOperationException("Cannot locate the terrain kit. Keep Editor/TerrainKitSetup.cs, Runtime and Models together inside the same kit folder, anywhere under Assets.");
            throw new InvalidOperationException("Multiple terrain kit copies found: " + string.Join(", ", candidates.ToArray()) + ". Keep one imported copy and run Setup again.");
        }

        private static Shader CreateVolumeShader(string kitRoot, string outputFolder)
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null && !pipeline.GetType().Name.Contains("UniversalRender"))
                throw new InvalidOperationException("Soft volume shading supports Built-in or URP. Use a project shader for other pipelines.");
            string templatePath = kitRoot + "/Shaders/TerrainSoftVolume_" + (pipeline == null ? "BuiltIn" : "URP") + ".txt";
            if (!File.Exists(templatePath))
                throw new FileNotFoundException("Copy the new Shaders folder next to Editor, Runtime and Models: " + templatePath);
            EnsureFolder(outputFolder);
            string shaderPath = outputFolder + "/TerrainSoftVolume.shader";
            string shaderName = "SaveThemAll/TerrainSoftVolume_" + AssetDatabase.AssetPathToGUID(outputFolder);
            File.WriteAllText(shaderPath, File.ReadAllText(templatePath).Replace("__SHADER_NAME__", shaderName));
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Terrain soft-volume shader could not compile. Check Console: " + shaderPath);
            return shader;
        }

        private static Material MakeMaterial(Texture2D texture, Shader shader)
        {
            Material material = new Material(shader);
            ConfigureVolumeMaterial(material, shader, texture, Color.white);
            return material;
        }

        private static void ConfigureVolumeMaterial(Material material, Shader shader, Texture texture, Color color)
        {
            material.shader = shader;
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", color);
            material.SetFloat("_ShadeStrength", .35f);
            material.SetVector("_LightDirection", new Vector4(-.4f, .8f, -.5f, 0));
        }

        // Use the body (GrassPlatform), never the overhanging top cap, for cell dimensions.
        private static void NormalizeBody(Transform wrapper, Transform visual)
        {
            Bounds body = BodyBounds(wrapper);
            if (body.size.x < .0001f) throw new InvalidOperationException("Invalid body bounds: " + wrapper.name);
            visual.localScale *= 1f / body.size.x;
            body = BodyBounds(wrapper);
            visual.localPosition += new Vector3(-body.center.x, -.5f - body.min.y, -body.center.z);
        }

        private static Bounds BodyBounds(Transform root)
        {
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            bool hasBody = false;
            foreach (MeshFilter mf in filters) if (IsBodyMesh(mf)) hasBody = true;
            bool initialized = false;
            Bounds result = new Bounds();
            foreach (MeshFilter mf in filters)
            {
                if (mf.sharedMesh == null || (hasBody && !IsBodyMesh(mf))) continue;
                Bounds b = mf.sharedMesh.bounds;
                Matrix4x4 matrix = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                {
                    Vector3 p = matrix.MultiplyPoint3x4(new Vector3(x == 0 ? b.min.x : b.max.x,
                        y == 0 ? b.min.y : b.max.y, z == 0 ? b.min.z : b.max.z));
                    if (!initialized) { result = new Bounds(p, Vector3.zero); initialized = true; }
                    else result.Encapsulate(p);
                }
            }
            if (!initialized) throw new InvalidOperationException("No mesh found: " + root.name);
            return result;
        }

        private static bool IsBodyMesh(MeshFilter mesh)
        {
            if (mesh.sharedMesh == null) return false;
            const string prefix = "GrassPlatform";
            int start = mesh.name.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return false;
            // Several seam caps are named GrassPlatform_Merge_14Grass etc.
            // They share the prefix but must NEVER set body width or depth.
            string suffix = mesh.name.Substring(start + prefix.Length);
            return suffix.IndexOf("Grass", StringComparison.OrdinalIgnoreCase) < 0;
        }

        [MenuItem("Tools/Save Them All/Repair Selected Map Corners")]
        public static void RepairSelectedCorners()
        {
            TerrainCatalog catalog = GetSelectedCatalog();
            if (catalog == null)
            {
                Debug.LogWarning("Select Terrain Map or its TerrainCatalog asset first.");
                return;
            }
            string catalogPath = AssetDatabase.GetAssetPath(catalog);
            string output = Path.GetDirectoryName(catalogPath).Replace('\\', '/');
            if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("Save the catalog asset first.");
            Shader shader = CreateVolumeShader(FindKitRoot(), output + "/Materials");
            var materials = new HashSet<Material>();
            int repaired = 0;
            try
            {
                for (int i = 0; i < catalog.entries.Count; i++)
                {
                    TerrainCatalog.Entry entry = catalog.entries[i];
                    if (entry.prefab == null) continue;
                    string path = AssetDatabase.GetAssetPath(entry.prefab);
                    if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Repair requires generated wrapper prefabs, not source FBX: " + path);
                    EditorUtility.DisplayProgressBar("Repair terrain corners", entry.model, (float)i / catalog.entries.Count);
                    GameObject contents = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        if (!entry.model.Contains("Background"))
                        {
                            if (contents.transform.childCount != 1)
                                throw new InvalidOperationException("Unexpected wrapper hierarchy; keep one visual root: " + path);
                            NormalizeBody(contents.transform, contents.transform.GetChild(0));
                            repaired++;
                        }
                        foreach (Renderer renderer in contents.GetComponentsInChildren<Renderer>(true))
                            foreach (Material material in renderer.sharedMaterials)
                                if (material != null) materials.Add(material);
                        if (PrefabUtility.SaveAsPrefabAsset(contents, path) == null)
                            throw new IOException("Could not save repaired prefab: " + path);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(contents); }
                }
                foreach (Material material in materials)
                {
                    Texture texture = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
                    if (texture == null && material.HasProperty("_MainTex")) texture = material.GetTexture("_MainTex");
                    Color color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") :
                        (material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white);
                    Undo.RecordObject(material, "Enable terrain soft volume shading");
                    ConfigureVolumeMaterial(material, shader, texture, color);
                    EditorUtility.SetDirty(material);
                }
                AssetDatabase.SaveAssets();
                ValidateCatalog(catalog);
                foreach (TerrainMap map in UnityEngine.Object.FindObjectsOfType<TerrainMap>(true))
                {
                    if (map.catalog != catalog || EditorUtility.IsPersistent(map) || !map.gameObject.scene.IsValid()) continue;
                    map.Rebuild();
                    EditorUtility.SetDirty(map);
                    EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
                }
                SceneView.RepaintAll();
                Debug.Log("Repaired " + repaired + " terrain prefab alignments and enabled soft volume shading. Existing cell layout and gameplay objects are preserved. Save the scene when ready.", catalog);
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        private static TerrainCatalog GetSelectedCatalog()
        {
            TerrainCatalog catalog = Selection.activeObject as TerrainCatalog;
            if (catalog == null && Selection.activeGameObject != null)
            {
                TerrainMap map = Selection.activeGameObject.GetComponentInParent<TerrainMap>();
                if (map != null) catalog = map.catalog;
            }
            return catalog;
        }

        private static void CreateDemo(TerrainCatalog catalog, string output)
        {
            // Additive: leave all currently open scenes and their unsaved work intact.
            Scene previousScene = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            GameObject root = new GameObject("Terrain Map - select to paint");
            TerrainMap map = root.AddComponent<TerrainMap>();
            map.catalog = catalog;
            map.LoadLayout(new[] {
                "#.......#", "#.......#", "###..####", "........#",
                "######.##", "#.......#", "#..###..#", "#..#....#",
                "#########", "#.......#", "#.......#", "#########"
            });
            GameObject objects = new GameObject("Gameplay Objects (place your assets here)");
            AddMarker(objects.transform, "Lava slot", new Vector3(6, 7, 0));
            AddMarker(objects.transform, "Exit", new Vector3(7, 9.5f, 0));
            AddMarker(objects.transform, "Cage upper", new Vector3(1, 9.5f, 0));
            AddMarker(objects.transform, "Cage lower", new Vector3(6, 3.5f, 0));
            AddMarker(objects.transform, "Spikes", new Vector3(4, 3.5f, 0));
            AddMarker(objects.transform, "Spawn", new Vector3(1, .5f, 0));
            GameObject cameraObject = new GameObject("Terrain Preview Camera");
            Camera cam = cameraObject.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(map.height * .5f + .7f, map.width / (2f * (9f / 16f)) + .7f);
            cam.transform.position = new Vector3((map.width - 1) * .5f, (map.height - 1) * .5f, -20);
            cam.transform.rotation = Quaternion.identity;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.23f, .39f, .58f);
            cam.nearClipPlane = .1f; cam.farClipPlane = 50;
            cam.tag = "MainCamera";
            // No AudioListener or global project-setting changes.
            string scenePath = output + "/TerrainDemo.unity";
            if (!EditorSceneManager.SaveScene(scene, scenePath))
                throw new IOException("Could not save demo scene: " + scenePath);
            EditorSceneManager.CloseScene(scene, true);
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            EditorGUIUtility.PingObject(Selection.activeObject);
            Debug.Log("Double-click the selected TerrainDemo scene to open it, then select Terrain Map and enable Paint.");
        }

        private static void AddMarker(Transform parent, string name, Vector3 position)
        {
            GameObject go = new GameObject(name + " [marker only]");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
        }

        [MenuItem("Tools/Save Them All/Validate Selected Map or Catalog")]
        public static void ValidateSelected()
        {
            TerrainCatalog catalog = Selection.activeObject as TerrainCatalog;
            if (catalog == null && Selection.activeGameObject != null)
            {
                TerrainMap map = Selection.activeGameObject.GetComponentInParent<TerrainMap>();
                if (map != null) catalog = map.catalog;
            }
            if (catalog == null) { Debug.LogWarning("Select a Terrain Map or TerrainCatalog asset first."); return; }
            ValidateCatalog(catalog);
        }

        private static void ValidateCatalog(TerrainCatalog catalog)
        {
            var keys = new HashSet<string>();
            foreach (TerrainCatalog.Entry e in catalog.entries)
                if (!keys.Add(e.model)) throw new InvalidOperationException("Duplicate model in catalog: " + e.model);
            for (int mask = 0; mask < 256; mask++)
            {
                string key = TerrainRules.Resolve(mask);
                GameObject prefab = catalog.Find(key);
                if (prefab == null) throw new InvalidOperationException("Missing model for mask " + mask + ": " + key);
                Bounds body = BodyBounds(prefab.transform);
                if (Mathf.Abs(body.size.x - 1f) > .002f || Mathf.Abs(body.min.y + .5f) > .002f ||
                    Mathf.Abs(body.center.x) > .002f || Mathf.Abs(body.center.z) > .002f)
                    throw new InvalidOperationException("Body is not aligned to a 1-unit cell: " + key);
            }
            Debug.Log("PASS: all 256 neighbor cases resolve to prefabs with aligned 1-unit bodies.", catalog);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
