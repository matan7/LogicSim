using System.Collections.Generic;
using LogicSim.Views;
using NUnit.Framework;
using UnityEngine;

namespace LogicSim.Tests.App
{
    public class WireRoutingTests
    {
        private static List<(Vector2 a, Vector2 b)> Segments(Vector3 from, Vector3 to, params Rect[] obstacles)
        {
            var points = new Vector3[6];
            int count = WireView.Route(from, to, points, obstacles);
            var segments = new List<(Vector2, Vector2)>();
            for (int i = 0; i + 1 < count; i++)
                segments.Add((points[i], points[i + 1]));
            Assert.That(points[0], Is.EqualTo(from));
            Assert.That(points[count - 1], Is.EqualTo(to));
            return segments;
        }

        /// <summary>True when an axis-parallel segment passes through the inside of the rectangle.</summary>
        private static bool Crosses((Vector2 a, Vector2 b) s, Rect r)
        {
            float xMin = Mathf.Min(s.a.x, s.b.x), xMax = Mathf.Max(s.a.x, s.b.x);
            float yMin = Mathf.Min(s.a.y, s.b.y), yMax = Mathf.Max(s.a.y, s.b.y);
            return xMax > r.xMin + 0.01f && xMin < r.xMax - 0.01f && yMax > r.yMin + 0.01f && yMin < r.yMax - 0.01f;
        }

        [Test]
        public void AGateWiredToItself_IsRoutedAroundItsBody()
        {
            // A 2-input gate at (10, 0): body 5 x 4, output (15, -2), second input (10, -3).
            var body = Rect.MinMaxRect(10, -4, 15, 0);
            foreach (var s in Segments(new Vector3(15, -2), new Vector3(10, -3), body))
                Assert.That(Crosses(s, body), Is.False, $"{s.a} -> {s.b} crosses the gate");

            // NOT gate: output and input on the same row; the straight line would run through the body.
            var not = Rect.MinMaxRect(20, -4, 25, 0);
            var segments = Segments(new Vector3(25, -2), new Vector3(20, -2), not);
            Assert.That(segments.Count, Is.EqualTo(5));
            foreach (var s in segments)
                Assert.That(Crosses(s, not), Is.False, $"{s.a} -> {s.b} crosses the NOT gate");
        }

        [Test]
        public void ABackwardWire_CrossesBetweenTwoComponentsWhenThereIsRoom()
        {
            // Output of an upper gate back to the input of a lower one: the crossing stays between them.
            var upper = Rect.MinMaxRect(10, -4, 15, 0);
            var lower = Rect.MinMaxRect(2, -14, 7, -10);
            var segments = Segments(new Vector3(15, -2), new Vector3(2, -12), upper, lower);
            Assert.That(segments[2].a.y, Is.EqualTo(-7f), "crossing row");
            foreach (var s in segments)
                Assert.That(Crosses(s, upper) || Crosses(s, lower), Is.False);
        }

        [Test]
        public void AForwardWire_IsUnchanged()
        {
            var segments = Segments(new Vector3(5, -2), new Vector3(13, -6));
            Assert.That(segments.Count, Is.EqualTo(3));
            Assert.That(segments[1].a, Is.EqualTo(new Vector2(9, -2)));
        }
    }
}
