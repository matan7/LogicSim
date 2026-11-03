using System;
using System.Collections.Generic;
using System.Linq;

namespace LogicSim.Simulation
{
    /// <summary>Left-to-right layered placement for circuits created without coordinates (e.g. by an AI agent).</summary>
    public static class AutoLayout
    {
        public const float ColumnSpacing = 10f;
        public const float RowSpacing = 6f;

        public static void Arrange(IList<CircuitComponent> components, IEnumerable<Wire> wires, float originX, float originY)
        {
            if (components.Count == 0)
                return;

            var ids = new HashSet<int>(components.Select(c => c.Id));
            var predecessors = components.ToDictionary(c => c.Id, c => new List<int>());
            foreach (var wire in wires)
            {
                if (ids.Contains(wire.From.ComponentId) && ids.Contains(wire.To.ComponentId) && wire.From.ComponentId != wire.To.ComponentId)
                    predecessors[wire.To.ComponentId].Add(wire.From.ComponentId);
            }

            // Longest path from the sources, bounded so that feedback loops terminate.
            var level = components.ToDictionary(c => c.Id, c => 0);
            for (int iteration = 0; iteration < components.Count; iteration++)
            {
                bool changed = false;
                foreach (var component in components)
                {
                    if (component.Spec.IsInterfaceInput || component.Kind == ComponentKind.Clock || component.Kind == ComponentKind.Constant)
                        continue;
                    int best = 0;
                    foreach (var p in predecessors[component.Id])
                        best = Math.Max(best, level[p] + 1);
                    if (best != level[component.Id] && best <= components.Count)
                    {
                        level[component.Id] = best;
                        changed = true;
                    }
                }
                if (!changed)
                    break;
            }

            int maxLevel = level.Values.Max();
            foreach (var component in components)
            {
                if (component.Spec.IsSink)
                    level[component.Id] = maxLevel + 1;
            }

            foreach (var column in components.GroupBy(c => level[c.Id]).OrderBy(g => g.Key))
            {
                int row = 0;
                foreach (var component in column.OrderBy(c => c.Id))
                {
                    component.X = originX + column.Key * ColumnSpacing;
                    component.Y = originY - row * RowSpacing;
                    row++;
                }
            }
        }
    }
}
