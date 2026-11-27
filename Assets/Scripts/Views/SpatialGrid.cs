using System.Collections.Generic;
using UnityEngine;

namespace LogicSim.Views
{
    /// <summary>
    /// Grids over world space for pointer hit tests and area selection. Every owner files one or more rectangles (a
    /// component's body, a pin's hit box, the segments of a wire). It replaces a physics collider per element: no
    /// GameObject, no physics sync, and nothing for the editor's gizmos to draw.
    /// <para>
    /// There is one grid per level, each with cells <see cref="LevelFactor"/> times larger than the one before. A
    /// rectangle goes to the finest level where it touches at most three cells per axis, so a wire that crosses a laid
    /// out circuit of 100,000 units is filed a few times on a coarse level instead of thousands of times on the finest.
    /// Queries look at every level.
    /// </para>
    /// <para>
    /// Removing is lazy: the owner's filing is marked dead and skipped by queries, and dead entries are swept out once
    /// there are as many as live ones. A coarse cell can hold thousands of long wire segments, and searching it on every
    /// move would make re-routing all wires quadratic.
    /// </para>
    /// </summary>
    public sealed class SpatialGrid<T> where T : class
    {
        public const int LevelFactor = 8;
        public const int Levels = 7;

        /// <summary>One filing of an owner; all entries of the filing share it.</summary>
        private sealed class Filing
        {
            public readonly T Owner;
            public readonly Rect[] Rects;
            public bool Alive = true;

            public Filing(T owner, Rect[] rects)
            {
                Owner = owner;
                Rects = rects;
            }
        }

        private readonly struct Entry
        {
            public readonly Filing Filing;
            public readonly Rect Rect;

            public Entry(Filing filing, Rect rect)
            {
                Filing = filing;
                Rect = rect;
            }
        }

        private readonly float[] _cellSizes = new float[Levels];
        private readonly Dictionary<long, List<Entry>>[] _cells = new Dictionary<long, List<Entry>>[Levels];
        private readonly Dictionary<T, Filing> _owners = new Dictionary<T, Filing>();
        private readonly HashSet<T> _seen = new HashSet<T>();
        private int _liveEntries;
        private int _deadEntries;

        /// <param name="cellSize">Cell size of the finest level, in world units.</param>
        public SpatialGrid(float cellSize = 8f)
        {
            for (int level = 0; level < Levels; level++)
            {
                _cellSizes[level] = cellSize;
                _cells[level] = new Dictionary<long, List<Entry>>();
                cellSize *= LevelFactor;
            }
        }

        /// <summary>Number of owners filed.</summary>
        public int Count => _owners.Count;

        public bool Contains(T owner) => _owners.ContainsKey(owner);

        /// <summary>Files <paramref name="owner"/> under one rectangle, replacing what it had.</summary>
        public void Set(T owner, Rect rect)
        {
            Remove(owner);
            File(new Filing(owner, new[] { rect }));
        }

        /// <summary>Files <paramref name="owner"/> under the first <paramref name="count"/> rectangles, replacing what it had.</summary>
        public void Set(T owner, IReadOnlyList<Rect> rects, int count)
        {
            Remove(owner);
            if (count <= 0)
                return;
            var copy = new Rect[count];
            for (int i = 0; i < count; i++)
                copy[i] = rects[i];
            File(new Filing(owner, copy));
        }

        public void Remove(T owner)
        {
            if (owner == null || !_owners.TryGetValue(owner, out var filing))
                return;
            _owners.Remove(owner);
            filing.Alive = false;
            int entries = EntryCount(filing);
            _liveEntries -= entries;
            _deadEntries += entries;
            if (_deadEntries > 1024 && _deadEntries > _liveEntries)
                Sweep();
        }

        public void Clear()
        {
            foreach (var cells in _cells)
                cells.Clear();
            _owners.Clear();
            _liveEntries = 0;
            _deadEntries = 0;
        }

        /// <summary>Adds the owners with a rectangle that contains <paramref name="point"/> to <paramref name="results"/>, each once.</summary>
        public void Query(Vector2 point, List<T> results)
        {
            for (int level = 0; level < Levels; level++)
            {
                var cells = _cells[level];
                if (cells.Count == 0 || !cells.TryGetValue(Key(Cell(point.x, level), Cell(point.y, level)), out var list))
                    continue;
                foreach (var entry in list)
                    if (entry.Filing.Alive && entry.Rect.Contains(point) && !results.Contains(entry.Filing.Owner))
                        results.Add(entry.Filing.Owner);
            }
        }

        /// <summary>Adds the owners with a rectangle that overlaps <paramref name="area"/> to <paramref name="results"/>, each once.</summary>
        public void Query(Rect area, List<T> results)
        {
            _seen.Clear();
            foreach (var owner in results)
                _seen.Add(owner);
            for (int level = 0; level < Levels; level++)
            {
                var cells = _cells[level];
                if (cells.Count == 0)
                    continue;
                CellRange(area, level, out int x0, out int y0, out int x1, out int y1);
                // A huge area (zoomed far out) is cheaper to test against every filed cell than cell by cell.
                if ((double)(x1 - x0 + 1) * (y1 - y0 + 1) > cells.Count)
                {
                    foreach (var list in cells.Values)
                        Collect(list, area, results);
                    continue;
                }
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                        if (cells.TryGetValue(Key(x, y), out var list))
                            Collect(list, area, results);
            }
        }

        private void Collect(List<Entry> list, Rect area, List<T> results)
        {
            foreach (var entry in list)
                if (entry.Filing.Alive && entry.Rect.Overlaps(area) && _seen.Add(entry.Filing.Owner))
                    results.Add(entry.Filing.Owner);
        }

        private void File(Filing filing)
        {
            _owners[filing.Owner] = filing;
            foreach (var rect in filing.Rects)
            {
                int level = LevelOf(rect);
                var cells = _cells[level];
                CellRange(rect, level, out int x0, out int y0, out int x1, out int y1);
                for (int x = x0; x <= x1; x++)
                {
                    for (int y = y0; y <= y1; y++)
                    {
                        long key = Key(x, y);
                        if (!cells.TryGetValue(key, out var list))
                        {
                            list = new List<Entry>(4);
                            cells.Add(key, list);
                        }
                        list.Add(new Entry(filing, rect));
                        _liveEntries++;
                    }
                }
            }
        }

        private int EntryCount(Filing filing)
        {
            int count = 0;
            foreach (var rect in filing.Rects)
            {
                CellRange(rect, LevelOf(rect), out int x0, out int y0, out int x1, out int y1);
                count += (x1 - x0 + 1) * (y1 - y0 + 1);
            }
            return count;
        }

        /// <summary>Drops the entries of removed filings, and the cells left empty.</summary>
        private void Sweep()
        {
            var empty = new List<long>();
            foreach (var cells in _cells)
            {
                empty.Clear();
                foreach (var pair in cells)
                {
                    pair.Value.RemoveAll(entry => !entry.Filing.Alive);
                    if (pair.Value.Count == 0)
                        empty.Add(pair.Key);
                }
                foreach (var key in empty)
                    cells.Remove(key);
            }
            _deadEntries = 0;
        }

        /// <summary>The finest level where the rectangle touches at most three cells per axis (the coarsest for anything larger).</summary>
        private int LevelOf(Rect rect)
        {
            float size = Mathf.Max(rect.width, rect.height);
            for (int level = 0; level < Levels - 1; level++)
                if (size <= 2 * _cellSizes[level])
                    return level;
            return Levels - 1;
        }

        private int Cell(float coordinate, int level)
        {
            // Clamped so that a stray huge (or NaN) coordinate cannot make a loop over cells run (nearly) forever.
            float cell = coordinate / _cellSizes[level];
            return float.IsNaN(cell) ? 0 : Mathf.FloorToInt(Mathf.Clamp(cell, -1e6f, 1e6f));
        }

        private void CellRange(Rect rect, int level, out int x0, out int y0, out int x1, out int y1)
        {
            x0 = Cell(rect.xMin, level);
            y0 = Cell(rect.yMin, level);
            x1 = Cell(rect.xMax, level);
            y1 = Cell(rect.yMax, level);
        }

        private static long Key(int x, int y) => ((long)x << 32) | (uint)y;
    }
}
