using UnityEngine;
using UnityEngine.EventSystems;

namespace LogicSim.UI
{
    /// <summary>Scrolls a content rect with the mouse wheel (without drag-to-scroll, which would fight palette dragging).</summary>
    public class WheelScroller : MonoBehaviour, IScrollHandler
    {
        public RectTransform Content;
        public RectTransform Viewport;
        public float PixelsPerTick = 60f;

        public void OnScroll(PointerEventData eventData)
        {
            if (Content == null || Viewport == null)
                return;
            float delta = eventData.scrollDelta.y;
            // Some platforms report wheel ticks, others pixels.
            float pixels = Mathf.Abs(delta) < 10f ? delta * PixelsPerTick : delta;
            float max = Mathf.Max(0, Content.rect.height - Viewport.rect.height);
            var position = Content.anchoredPosition;
            position.y = Mathf.Clamp(position.y - pixels, 0, max);
            Content.anchoredPosition = position;
        }
    }
}
