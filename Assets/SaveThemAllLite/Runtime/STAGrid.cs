using System;
using System.Collections.Generic;

namespace SaveThemAll.Lite
{
    [Serializable]
    public struct STACell : IEquatable<STACell>
    {
        public int x, y;
        public STACell(int x, int y) { this.x = x; this.y = y; }
        public bool Equals(STACell other) { return x == other.x && y == other.y; }
        public override bool Equals(object obj) { return obj is STACell && Equals((STACell)obj); }
        public override int GetHashCode() { unchecked { return (x * 397) ^ y; } }
    }

    public static class STAGrid
    {
        public sealed class EditPlan
        {
            public readonly List<STACell> cells = new List<STACell>();
            public readonly List<STACell> affected = new List<STACell>();
            public readonly List<string> missing = new List<string>();
            public bool changed;
            public bool CanApply { get { return changed && missing.Count == 0; } }
        }
        public static bool Inside(STACell cell, int width, int height)
        { return cell.x >= 0 && cell.y >= 0 && cell.x < width && cell.y < height; }
        public static int Mask(HashSet<STACell> cells, STACell cell)
        {
            int mask = 0;
            for (int i = 0; i < 8; i++)
                if (cells.Contains(new STACell(cell.x + STAAutoTileRules.DX[i], cell.y + STAAutoTileRules.DY[i]))) mask |= 1 << i;
            return mask;
        }
        public static EditPlan Plan(IList<STACell> current, STACell cell, bool solid,
            int width, int height, Func<string, bool> available, Func<STACell, string> overrideAt = null)
        {
            var result = new EditPlan();
            var cells = new HashSet<STACell>(current);
            result.cells.AddRange(cells);
            if (!Inside(cell, width, height)) return result;
            result.changed = solid ? cells.Add(cell) : cells.Remove(cell);
            if (!result.changed) return result;
            result.cells.Clear(); result.cells.AddRange(cells);
            result.cells.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
            {
                var p = new STACell(cell.x + x, cell.y + y);
                if (!Inside(p, width, height)) continue;
                result.affected.Add(p);
                if (!cells.Contains(p)) continue;
                string key = Key(cells, p, overrideAt);
                if (!available(key) && !result.missing.Contains(key)) result.missing.Add(key);
            }
            return result;
        }
        public static string Key(HashSet<STACell> cells, STACell cell, Func<STACell, string> overrideAt = null)
        {
            string key = overrideAt != null ? overrideAt(cell) : null;
            return string.IsNullOrEmpty(key) ? STAAutoTileRules.Resolve(Mask(cells, cell)) : key;
        }
        public static List<string> Missing(IList<STACell> current, Func<string, bool> available, Func<STACell, string> overrideAt = null)
        {
            var cells = new HashSet<STACell>(current); var missing = new List<string>();
            foreach (STACell p in cells)
            {
                string key = Key(cells, p, overrideAt);
                if (!available(key) && !missing.Contains(key)) missing.Add(key);
            }
            missing.Sort(StringComparer.Ordinal); return missing;
        }
    }
}
