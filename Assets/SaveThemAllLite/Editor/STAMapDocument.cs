using System;
using System.Collections.Generic;

namespace SaveThemAll.Lite.Editor
{
    // Penguin Terrain Studio JSON v1. Pure data: importing never executes JavaScript.
    [Serializable]
    public sealed class STAMapDocument
    {
        public int schemaVersion;
        public string name, origin;
        public int width, height;
        public float cellSize;
        public TerrainRecord[] terrain;
        public ObjectRecord[] objects;
        public AssetDefinition[] assets;
        internal const int MaxBytes = 2097152;

        [Serializable]
        public sealed class TerrainRecord { public int x, y; public string overrideModel; }
        [Serializable]
        public sealed class ObjectRecord
        {
            public string id, type, pairId;
            public int x, y, width, height, rotation, count;
            public float offsetX, offsetY;
            public float CenterX { get { return x + (width - 1) * .5f + offsetX; } }
            public float CenterY { get { return y + (height - 1) * .5f + offsetY; } }
        }
        [Serializable]
        public sealed class AssetDefinition
        {
            public string id, label, role, sourcePath, thumbnail;
            public int w, h;
        }
        private static readonly string[] KnownTypes =
            { "prop", "lava", "stone", "spikes", "cage", "spawn", "exit", "portal", "fan", "bridge", "key", "stairs" };
        internal static bool KnownType(string type) { return Array.IndexOf(KnownTypes, type) >= 0; }
        internal static bool IsPrefabId(string id)
        {
            if (id == null || id.Length != 39 || !id.StartsWith("prefab_", StringComparison.Ordinal)) return false;
            for (int i = 7; i < id.Length; i++)
                if (!(id[i] >= '0' && id[i] <= '9') && !(id[i] >= 'a' && id[i] <= 'f')) return false;
            return true;
        }
        internal AssetDefinition Asset(string type)
        {
            if (assets != null) foreach (AssetDefinition asset in assets)
                if (asset != null && asset.id == type) return asset;
            return null;
        }
        internal List<string> RequiredTypes()
        {
            var types = new List<string>();
            foreach (ObjectRecord obj in objects) if (!types.Contains(obj.type)) types.Add(obj.type);
            types.Sort(StringComparer.Ordinal); return types;
        }
        internal List<STACell> Cells()
        {
            var result = new List<STACell>();
            foreach (TerrainRecord cell in terrain) result.Add(new STACell(cell.x, cell.y));
            return result;
        }
        internal Dictionary<STACell, string> Overrides()
        {
            var result = new Dictionary<STACell, string>();
            foreach (TerrainRecord cell in terrain)
                if (!string.IsNullOrEmpty(cell.overrideModel)) result.Add(new STACell(cell.x, cell.y), cell.overrideModel);
            return result;
        }
        internal List<string> MissingTerrain(Func<string, bool> available)
        {
            var overrides = Overrides();
            return STAGrid.Missing(Cells(), available, p => { string key; return overrides.TryGetValue(p, out key) ? key : null; });
        }
        internal void Validate()
        {
            if (schemaVersion != 1 || origin != "bottom-left")
                throw new ArgumentException("Cần file TerrainMap JSON schemaVersion 1, origin bottom-left xuất từ web dựng map.");
            if (width < 1 || width > 128 || height < 1 || height > 128)
                throw new ArgumentException("Kích thước map phải từ 1 đến 128 ô mỗi chiều.");
            if (!Finite(cellSize) || cellSize < .01f || cellSize > 100)
                throw new ArgumentException("cellSize phải từ 0.01 đến 100.");
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
                throw new ArgumentException("Tên map phải có 1–80 ký tự.");
            if (terrain == null || objects == null || terrain.Length > 16384 || objects.Length > 4096)
                throw new ArgumentException("Thiếu mảng terrain/objects, hoặc vượt giới hạn 16.384 ô / 4.096 object.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < 256; i++) keys.Add(STAAutoTileRules.Resolve(i));
            var cells = new HashSet<STACell>();
            foreach (TerrainRecord cell in terrain)
            {
                if (cell == null || !Contains(cell.x, cell.y, 1, 1)) throw new ArgumentException("Có ô terrain nằm ngoài map.");
                if (!cells.Add(new STACell(cell.x, cell.y))) throw new ArgumentException("Trùng ô terrain: " + cell.x + ", " + cell.y);
                if (!string.IsNullOrEmpty(cell.overrideModel) && !keys.Contains(cell.overrideModel))
                    throw new ArgumentException("Mã khối terrain không hợp lệ: " + cell.overrideModel);
            }
            var types = new HashSet<string>(StringComparer.Ordinal);
            if (assets != null)
            {
                if (assets.Length > 4096) throw new ArgumentException("Quá nhiều loại asset.");
                foreach (AssetDefinition asset in assets)
                {
                    if (asset == null || !IsPrefabId(asset.id) || !types.Add(asset.id))
                        throw new ArgumentException("ID prefab trong assets không hợp lệ hoặc bị trùng.");
                    if (string.IsNullOrWhiteSpace(asset.label) || asset.label.Length > 160 || !KnownType(asset.role) ||
                        asset.w < 1 || asset.w > 128 || asset.h < 1 || asset.h > 128 || (asset.sourcePath != null && asset.sourcePath.Length > 1024))
                        throw new ArgumentException("Thông tin asset không hợp lệ: " + asset.id);
                }
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ObjectRecord obj in objects)
            {
                if (obj == null || (!KnownType(obj.type) && !types.Contains(obj.type)))
                    throw new ArgumentException("Loại object không được khai báo trong assets.");
                if (string.IsNullOrWhiteSpace(obj.id) || obj.id.Length > 100 || !ids.Add(obj.id))
                    throw new ArgumentException("Object bị thiếu hoặc trùng ID.");
                if (!Contains(obj.x, obj.y, obj.width, obj.height)) throw new ArgumentException("Object nằm ngoài map: " + obj.id);
                if (obj.rotation != 0 && obj.rotation != 90 && obj.rotation != 180 && obj.rotation != 270)
                    throw new ArgumentException("Góc xoay trong JSON phải là 0/90/180/270: " + obj.id);
                if (obj.count < 0 || obj.count > 999 || (obj.pairId != null && obj.pairId.Length > 40))
                    throw new ArgumentException("Số lượng hoặc mã cặp không hợp lệ: " + obj.id);
                if (!Finite(obj.offsetX) || !Finite(obj.offsetY) || Math.Abs(obj.offsetX) > 1 || Math.Abs(obj.offsetY) > 1)
                    throw new ArgumentException("Offset X/Y phải từ -1 đến 1 ô: " + obj.id);
            }
        }
        private bool Contains(int x, int y, int w, int h)
        { return w >= 1 && h >= 1 && w <= width && h <= height && x >= 0 && y >= 0 && x <= width - w && y <= height - h; }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
