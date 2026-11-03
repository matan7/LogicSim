using Core.Selecting;
using UnityEngine;

namespace LogicSim.Views
{
    /// <summary>Orthogonally routed wire; selectable and colored by its signal during the simulation.</summary>
    [RequireComponent(typeof(LineRenderer))]
    public class WireView : MonoBehaviour, ISelectable
    {
        public const float LineWidth = 0.18f;

        private LineRenderer _line;
        private EdgeCollider2D _collider;
        private bool _selected;
        private bool? _value;
        private readonly Vector3[] _points = new Vector3[6];

        public int WireId { get; private set; }
        public Transform Transform => transform;

        public static WireView Create(Transform parent, int wireId)
        {
            var go = new GameObject("Wire #" + wireId);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            ConfigureLine(line, VisualStyle.WireSortingOrder);
            var view = go.AddComponent<WireView>();
            view.WireId = wireId;
            view._line = line;
            view._collider = go.AddComponent<EdgeCollider2D>();
            view._collider.edgeRadius = 0.25f;
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

        /// <summary>Routes from an output (left) to an input (right) with right angles.</summary>
        public void SetEndpoints(Vector3 from, Vector3 to)
        {
            int count = Route(from, to, _points);
            _line.positionCount = count;
            for (int i = 0; i < count; i++)
                _line.SetPosition(i, _points[i]);

            var points = new Vector2[count];
            for (int i = 0; i < count; i++)
                points[i] = _points[i];
            _collider.points = points;
        }

        /// <returns>Number of points written.</returns>
        public static int Route(Vector3 from, Vector3 to, Vector3[] points)
        {
            from.z = 0;
            to.z = 0;
            if (Mathf.Approximately(from.y, to.y) || Mathf.Approximately(from.x, to.x))
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
            // Input lies behind the output: leave to the right, cross over, enter from the left.
            float midY = Mathf.Round((from.y + to.y) / 2f);
            points[0] = from;
            points[1] = new Vector3(from.x + 1f, from.y);
            points[2] = new Vector3(from.x + 1f, midY);
            points[3] = new Vector3(to.x - 1f, midY);
            points[4] = new Vector3(to.x - 1f, to.y);
            points[5] = to;
            return 6;
        }

        public void SetValue(bool? value)
        {
            if (_value == value)
                return;
            _value = value;
            ApplyColor();
        }

        public void Select()
        {
            _selected = true;
            ApplyColor();
        }

        public void Deselect()
        {
            _selected = false;
            ApplyColor();
        }

        private void ApplyColor()
        {
            Color color = _selected ? VisualStyle.SelectionColor
                : _value.HasValue ? VisualStyle.SignalColor(_value.Value)
                : VisualStyle.WireColor;
            _line.startColor = color;
            _line.endColor = color;
            _line.widthMultiplier = _selected ? LineWidth * 1.6f : LineWidth;
        }
    }
}
