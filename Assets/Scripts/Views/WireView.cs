using System.Collections.Generic;
using Core.Selecting;
using UnityEngine;

namespace LogicSim.Views
{
    /// <summary>
    /// Orthogonally routed wire. <see cref="CircuitView"/> files its segments in a spatial grid for picking; the
    /// <see cref="WireRenderer"/> draws it with all other wires. Without that renderer (no GPU support) the wire draws
    /// itself with a LineRenderer.
    /// </summary>
    public class WireView : MonoBehaviour, ISelectionView
    {
        public const float LineWidth = 0.18f;
        /// <summary>How far from its center line a wire is picked.</summary>
        public const float HitRadius = 0.25f;

        private WireRenderer _renderer;
        private LineRenderer _line;
        private bool _selected;
        private bool? _value;
        private readonly Vector3[] _points = new Vector3[6];
        private int _pointCount;

        public int WireId { get; private set; }

        /// <summary>Last point of the route: the input pin it ends at.</summary>
        public Vector3 End => _pointCount > 0 ? _points[_pointCount - 1] : transform.position;

        /// <summary>Slot in the renderer, -1 while not drawn. The renderer updates it when slots move.</summary>
        internal int Slot { get; set; } = -1;

        public static WireView Create(Transform parent, int wireId, WireRenderer renderer)
        {
            var go = new GameObject("Wire #" + wireId);
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<WireView>();
            view.WireId = wireId;
            view._renderer = renderer;
            if (renderer == null)
            {
                view._line = go.AddComponent<LineRenderer>();
                ConfigureLine(view._line, VisualStyle.WireSortingOrder);
            }
            view.AddToRenderer();
            view.ApplyColor();
            return view;
        }

        public static void ConfigureLine(LineRenderer line, int sortingOrder)
        {
            line.useWorldSpace = true;
            line.widthMultiplier = LineWidth;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.sharedMaterial = VisualStyle.LineMaterial;
            line.sortingOrder = sortingOrder;
            line.alignment = LineAlignment.View;
        }

        private void AddToRenderer()
        {
            if (_renderer == null || Slot >= 0)
                return;
            Slot = _renderer.Add(this);
            if (_pointCount > 0)
                _renderer.SetRoute(Slot, _points, _pointCount);
            _renderer.SetSelected(Slot, _selected);
        }

        private void OnEnable()
        {
            // Views are created enabled; this only matters if one is disabled and enabled again.
            if (WireId != 0)
                AddToRenderer();
        }

        // CircuitView disables views before destroying them, so they disappear in the same frame.
        private void OnDisable()
        {
            if (_renderer == null || Slot < 0)
                return;
            _renderer.Remove(Slot);
            Slot = -1;
        }

        /// <summary>Routes from an output to an input with right angles, around <paramref name="obstacles"/>.</summary>
        public void SetEndpoints(Vector3 from, Vector3 to, IReadOnlyList<Rect> obstacles = null)
        {
            _pointCount = Route(from, to, _points, obstacles);
            if (_line != null)
            {
                _line.positionCount = _pointCount;
                for (int i = 0; i < _pointCount; i++)
                    _line.SetPosition(i, _points[i]);
            }
            if (Slot >= 0)
                _renderer.SetRoute(Slot, _points, _pointCount);
        }

        /// <summary>Writes the segments' hit boxes (each widened by <see cref="HitRadius"/>); returns how many.</summary>
        public int GetHitRects(Rect[] rects)
        {
            int count = 0;
            for (int i = 0; i + 1 < _pointCount && count < rects.Length; i++)
            {
                Vector2 a = _points[i], b = _points[i + 1];
                rects[count++] = Rect.MinMaxRect(Mathf.Min(a.x, b.x) - HitRadius, Mathf.Min(a.y, b.y) - HitRadius,
                    Mathf.Max(a.x, b.x) + HitRadius, Mathf.Max(a.y, b.y) + HitRadius);
            }
            return count;
        }

        /// <summary>
        /// Route from an output (left) to an input (right). When the input lies behind the output, the wire leaves to the
        /// right, crosses over and enters from the left; the crossing runs outside the <paramref name="obstacles"/> (the
        /// bodies of the two components), so that a gate wired to itself is not crossed.
        /// </summary>
        /// <returns>Number of points written.</returns>
        public static int Route(Vector3 from, Vector3 to, Vector3[] points, IReadOnlyList<Rect> obstacles = null)
        {
            from.z = 0;
            to.z = 0;
            if (Mathf.Approximately(from.y, to.y) && to.x > from.x || Mathf.Approximately(from.x, to.x))
            {
                points[0] = from;
                points[1] = to;
                return 2;
            }
            if (to.x >= from.x + 1f)
            {
                float midX = Mathf.Round((from.x + to.x) / 2f);
                if (midX <= from.x)
                    midX = from.x + 0.5f;
                points[0] = from;
                points[1] = new Vector3(midX, from.y);
                points[2] = new Vector3(midX, to.y);
                points[3] = to;
                return 4;
            }
            float left = to.x - 1f, right = from.x + 1f;
            float midY = CrossingY((from.y + to.y) / 2f, Mathf.Min(left, right), Mathf.Max(left, right), obstacles);
            points[0] = from;
            points[1] = new Vector3(right, from.y);
            points[2] = new Vector3(right, midY);
            points[3] = new Vector3(left, midY);
            points[4] = new Vector3(left, to.y);
            points[5] = to;
            return 6;
        }

        /// <summary>The grid row closest to <paramref name="middle"/> whose span from xMin to xMax misses every obstacle.</summary>
        private static float CrossingY(float middle, float xMin, float xMax, IReadOnlyList<Rect> obstacles)
        {
            float best = Mathf.Round(middle);
            if (obstacles == null || obstacles.Count == 0 || !Blocked(best, xMin, xMax, obstacles))
                return best;
            float bestDistance = float.MaxValue;
            foreach (var obstacle in obstacles)
            {
                foreach (float y in new[] { Mathf.Ceil(obstacle.yMax) + 1f, Mathf.Floor(obstacle.yMin) - 1f })
                {
                    float distance = Mathf.Abs(y - middle);
                    if (distance < bestDistance && !Blocked(y, xMin, xMax, obstacles))
                    {
                        best = y;
                        bestDistance = distance;
                    }
                }
            }
            return best;
        }

        private static bool Blocked(float y, float xMin, float xMax, IReadOnlyList<Rect> obstacles)
        {
            foreach (var r in obstacles)
                if (y > r.yMin - 0.5f && y < r.yMax + 0.5f && xMax > r.xMin && xMin < r.xMax)
                    return true;
            return false;
        }

        /// <summary>Signal value to show, or null outside the simulation. Only used without the wire renderer.</summary>
        public void SetValue(bool? value)
        {
            if (_value == value)
                return;
            _value = value;
            ApplyColor();
        }

        public void SetSelected(bool selected)
        {
            _selected = selected;
            ApplyColor();
        }

        private void ApplyColor()
        {
            if (Slot >= 0)
                _renderer.SetSelected(Slot, _selected);
            if (_line == null)
                return;
            Color color = _selected ? VisualStyle.SelectionColor
                : _value.HasValue ? VisualStyle.SignalColor(_value.Value)
                : VisualStyle.WireColor;
            _line.startColor = color;
            _line.endColor = color;
            _line.widthMultiplier = _selected ? LineWidth * 1.6f : LineWidth;
        }
    }
}
