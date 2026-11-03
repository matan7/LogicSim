using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LogicSim.UI
{
    /// <summary>Centered dialog over a dimmed background. Content goes into <see cref="Body"/>.</summary>
    public sealed class ModalPanel
    {
        public RectTransform Root { get; private set; }
        public RectTransform Body { get; private set; }
        public RectTransform Buttons { get; private set; }
        public event Action Closed;

        public bool IsOpen => Root != null;

        public static ModalPanel Create(Transform canvas, string title, float width, float height)
        {
            var panel = new ModalPanel();

            panel.Root = UiFactory.CreateRect("Dialog " + title, canvas);
            UiFactory.Stretch(panel.Root);
            UiFactory.AddImage(panel.Root, UiFactory.Overlay);
            panel.Root.SetAsLastSibling();

            var frame = UiFactory.CreateRect("Frame", panel.Root);
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(width, height);
            UiFactory.AddImage(frame, UiFactory.PanelBorder);

            var inner = UiFactory.CreateRect("Inner", frame);
            UiFactory.Stretch(inner, 2, 2, 2, 2);
            UiFactory.AddImage(inner, UiFactory.PanelColor);

            var titleText = UiFactory.CreateText(inner, title, 26, UiFactory.TextColor);
            titleText.fontStyle = FontStyles.Bold;
            var titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.pivot = new Vector2(0.5f, 1);
            titleRect.offsetMin = new Vector2(24, -56);
            titleRect.offsetMax = new Vector2(-24, -14);

            var viewport = UiFactory.CreateRect("Viewport", inner);
            UiFactory.Stretch(viewport, 24, 24, 64, 70);
            viewport.gameObject.AddComponent<RectMask2D>();
            UiFactory.AddImage(viewport, new Color(0, 0, 0, 0));

            panel.Body = UiFactory.CreateVerticalGroup(viewport, 8);
            panel.Body.anchorMin = new Vector2(0, 1);
            panel.Body.anchorMax = new Vector2(1, 1);
            panel.Body.pivot = new Vector2(0.5f, 1);
            panel.Body.anchoredPosition = Vector2.zero;
            panel.Body.sizeDelta = new Vector2(0, 0);

            var scroller = viewport.gameObject.AddComponent<WheelScroller>();
            scroller.Content = panel.Body;
            scroller.Viewport = viewport;

            panel.Buttons = UiFactory.CreateHorizontalGroup(inner, 12);
            panel.Buttons.anchorMin = new Vector2(0, 0);
            panel.Buttons.anchorMax = new Vector2(1, 0);
            panel.Buttons.pivot = new Vector2(0.5f, 0);
            panel.Buttons.offsetMin = new Vector2(24, 16);
            panel.Buttons.offsetMax = new Vector2(-24, 56);
            return panel;
        }

        public TextMeshProUGUI AddText(string text, float size = 20, bool monospace = false)
        {
            var tmp = UiFactory.CreateText(Body, monospace ? "<mspace=0.6em>" + text + "</mspace>" : text, size, UiFactory.TextColor);
            tmp.richText = true;
            return tmp;
        }

        public TextMeshProUGUI AddHeading(string text)
        {
            var tmp = UiFactory.CreateText(Body, text, 18, UiFactory.AccentColor);
            tmp.fontStyle = FontStyles.Bold;
            return tmp;
        }

        public Button AddListButton(string label, Action onClick)
        {
            var button = UiFactory.CreateButton(Body, label, onClick, 0, 38);
            var text = button.GetComponentInChildren<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.MidlineLeft;
            return button;
        }

        public Button AddButton(string label, Action onClick, float width = 140)
        {
            return UiFactory.CreateButton(Buttons, label, onClick, width, 38);
        }

        public void Close()
        {
            if (Root == null)
                return;
            UnityEngine.Object.Destroy(Root.gameObject);
            Root = null;
            Closed?.Invoke();
        }
    }
}
