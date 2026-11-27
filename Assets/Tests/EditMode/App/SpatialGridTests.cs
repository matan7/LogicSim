using System.Collections.Generic;
using LogicSim.Views;
using NUnit.Framework;
using UnityEngine;

namespace LogicSim.Tests.App
{
    /// <summary>The grid that replaced the physics colliders for picking and area selection.</summary>
    public class SpatialGridTests
    {
        private sealed class Item
        {
            public readonly string Name;
            public Item(string name) => Name = name;
            public override string ToString() => Name;
        }

        private static List<Item> At(SpatialGrid<Item> grid, float x, float y)
        {
            var results = new List<Item>();
            grid.Query(new Vector2(x, y), results);
            return results;
        }

        private static List<Item> In(SpatialGrid<Item> grid, Rect area)
        {
            var results = new List<Item>();
            grid.Query(area, results);
            return results;
        }

        [Test]
        public void PointQueries_FindTheRectanglesThatContainThePoint_AcrossCells()
        {
            var grid = new SpatialGrid<Item>(4f);
            var body = new Item("body");
            var negative = new Item("negative");
            grid.Set(body, new Rect(2, -3, 5, 4)); // spans cells x 0..1, y -1..0
            grid.Set(negative, new Rect(-10, -10, 2, 2));

            Assert.That(At(grid, 3, -1), Is.EqualTo(new[] { body }));
            Assert.That(At(grid, 6.5f, 0.5f), Is.EqualTo(new[] { body }));
            Assert.That(At(grid, -9, -9), Is.EqualTo(new[] { negative }));
            Assert.That(At(grid, 8, 0), Is.Empty);
            Assert.That(At(grid, 0, 0), Is.Empty);
        }

        [Test]
        public void Set_ReplacesAndRemove_ForgetsAnOwner()
        {
            var grid = new SpatialGrid<Item>(4f);
            var item = new Item("moved");
            grid.Set(item, new Rect(0, 0, 1, 1));
            grid.Set(item, new Rect(20, 20, 1, 1));

            Assert.That(At(grid, 0.5f, 0.5f), Is.Empty, "the old rectangle is gone");
            Assert.That(At(grid, 20.5f, 20.5f), Is.EqualTo(new[] { item }));
            Assert.That(grid.Count, Is.EqualTo(1));

            grid.Remove(item);
            Assert.That(At(grid, 20.5f, 20.5f), Is.Empty);
            Assert.That(grid.Count, Is.Zero);
        }

        [Test]
        public void AnOwnerWithSeveralRectangles_IsFoundOnceAndRemovedWhole()
        {
            var grid = new SpatialGrid<Item>(4f);
            var wire = new Item("wire");
            var segments = new[] { new Rect(0, 0, 10, 0.5f), new Rect(9.5f, 0, 0.5f, 10), new Rect(0, 0, 0.5f, 0.5f) };
            grid.Set(wire, segments, 2);

            Assert.That(At(grid, 9.7f, 0.2f), Is.EqualTo(new[] { wire }), "where two segments overlap");
            Assert.That(At(grid, 9.7f, 9), Is.EqualTo(new[] { wire }));
            Assert.That(At(grid, 5, 5), Is.Empty, "between the segments");
            Assert.That(In(grid, new Rect(-1, -1, 30, 30)), Is.EqualTo(new[] { wire }));

            grid.Remove(wire);
            Assert.That(In(grid, new Rect(-1, -1, 30, 30)), Is.Empty);
        }

        [Test]
        public void LongSegments_AreFiledOnACoarseLevel_AndFoundEverywhere()
        {
            // A wire across a laid out benchmark circuit: 100,000 units long. On the finest level alone this would be
            // filed under 12,500 cells per segment.
            var grid = new SpatialGrid<Item>(8f);
            var wire = new Item("long");
            var gate = new Item("gate");
            var segments = new[] { Rect.MinMaxRect(0, -0.25f, 100000, 0.25f), Rect.MinMaxRect(99999.75f, -5000, 100000.25f, 0) };
            grid.Set(wire, segments, 2);
            grid.Set(gate, new Rect(50000, -2, 5, 4));

            Assert.That(At(grid, 12345, 0), Is.EqualTo(new[] { wire }));
            Assert.That(At(grid, 100000, -2500), Is.EqualTo(new[] { wire }));
            Assert.That(At(grid, 50002, 0), Is.EquivalentTo(new[] { wire, gate }), "a fine and a coarse level at once");
            Assert.That(At(grid, 50002, 1), Is.EqualTo(new[] { gate }));
            Assert.That(In(grid, Rect.MinMaxRect(70000, -100, 70010, -10)), Is.Empty);
            Assert.That(In(grid, Rect.MinMaxRect(70000, -1, 70010, 1)), Is.EqualTo(new[] { wire }));

            grid.Remove(wire);
            Assert.That(At(grid, 12345, 0), Is.Empty);
            Assert.That(At(grid, 50002, 0), Is.EqualTo(new[] { gate }));
        }

        [Test]
        public void ManyMoves_LeaveOnlyTheLastPlace()
        {
            // Removal is lazy; enough dead entries trigger a sweep. Answers must not change either way.
            var grid = new SpatialGrid<Item>(4f);
            var mover = new Item("mover");
            var still = new Item("still");
            grid.Set(still, new Rect(0, 0, 3, 3));
            for (int i = 0; i < 3000; i++)
                grid.Set(mover, new Rect(i % 50, i % 7, 2, 2));
            grid.Set(mover, new Rect(100, 100, 2, 2));

            Assert.That(grid.Count, Is.EqualTo(2));
            Assert.That(At(grid, 1, 1), Is.EqualTo(new[] { still }));
            Assert.That(At(grid, 101, 101), Is.EqualTo(new[] { mover }));
            Assert.That(In(grid, Rect.MinMaxRect(-10, -10, 60, 20)), Is.EqualTo(new[] { still }));
        }

        [Test]
        public void AreaQueries_FindOverlappingOwners_AlsoForHugeAreas()
        {
            var grid = new SpatialGrid<Item>(4f);
            var a = new Item("a");
            var b = new Item("b");
            var far = new Item("far");
            grid.Set(a, new Rect(0, 0, 2, 2));
            grid.Set(b, new Rect(5, 5, 2, 2));
            grid.Set(far, new Rect(1000, 1000, 2, 2));

            Assert.That(In(grid, Rect.MinMaxRect(1, 1, 6, 6)), Is.EquivalentTo(new[] { a, b }));
            Assert.That(In(grid, Rect.MinMaxRect(2.5f, 2.5f, 4.5f, 4.5f)), Is.Empty);
            // More cells than filed cells: tested against every cell instead.
            Assert.That(In(grid, Rect.MinMaxRect(-5000, -5000, 5000, 5000)), Is.EquivalentTo(new[] { a, b, far }));
        }
    }
}
