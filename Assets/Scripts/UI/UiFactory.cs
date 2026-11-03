using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogicSim.UI
{
    /// <summary>Builds uGUI elements from code (panels, buttons, texts, inputs) in the app's style.</summary>
    public static class UiFactory
    {
        public static readonly Color PanelColor = new Color(0.07f, 0.10f, 0.15f, 0.97f);
        public static readonly Color PanelBorder = new Color(0.30f, 0.38f, 0.50f, 1f);
        public static readonly Color ButtonColor = new Color(0.16f, 0.21f, 0.29f, 1f);
        public static readonly Color ButtonHover = new Color(0.22f, 0.30f, 0.42f, 1f);
        public static readonly Color AccentColor = new Color(0.35f, 0.65f, 1f, 1f);
        public static readonly Color TextColor = new Color(0.92f, 0.94f, 0.97f, 1f);
        public static readonly Color MutedText = new Color(0.65f, 0.66f, 0.68f, 1f);
        public static readonly Color Overlay = new Color(0, 0, 0, 0.55f);

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rect, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        public static Image AddImage(RectTransform rect, Color color, bool raycastTarget = true)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        public static TextMeshProUGUI CreateText(Transform parent, string text, float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Left)
        {
            var rect = CreateRect("Text", parent);
            var tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            return tmp;
        }

        public static Button CreateButton(Transform parent, string label, Action onClick, float width = 140, float height = 36)
        {
            var rect = CreateRect("Button " + label, parent);
            rect.sizeDelta = new Vector2(width, height);
            var image = AddImage(rect, Color.white);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = ButtonColor;
            colors.highlightedColor = ButtonHover;
            colors.pressedColor = AccentColor;
            colors.selectedColor = ButtonColor;
            colors.disabledColor = new Color(ButtonColor.r, ButtonColor.g, ButtonColor.b, 0.4f);
            button.colors = colors;
            if (onClick != null)
                button.onClick.AddListener(() => onClick());

            var layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = height;

            var text = CreateText(rect, label, 18, TextColor, TextAlignmentOptions.Center);
            Stretch(text.rectTransform, 6, 6, 2, 2);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return button;
        }

        public static TMP_InputField CreateInputField(Transform parent, string initialText, float width, float height)
        {
            var root = CreateRect("Input", parent);
            root.sizeDelta = new Vector2(width, height);
            var background = AddImage(root, new Color(0.03f, 0.05f, 0.08f, 1f));
            var layout = root.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = height;

            var area = CreateRect("Text Area", root);
            Stretch(area, 10, 10, 4, 4);
            area.gameObject.AddComponent<RectMask2D>();

            var text = CreateText(area, string.Empty, 20, TextColor);
            Stretch(text.rectTransform);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.alignment = TextAlignmentOptions.MidlineLeft;

            var placeholder = CreateText(area, "Name", 20, MutedText);
            Stretch(placeholder.rectTransform);
            placeholder.fontStyle = FontStyles.Italic;
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;

            var input = root.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = background;
            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.fontAsset = text.font;
            input.pointSize = 20;
            input.caretColor = TextColor;
            input.selectionColor = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.4f);
            input.text = initialText ?? string.Empty;
            return input;
        }

        /// <summary>Vertical list container that grows with its children.</summary>
        public static RectTransform CreateVerticalGroup(Transform parent, float spacing, RectOffset padding = null)
        {
            var rect = CreateRect("Group", parent);
            var group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.padding = padding ?? new RectOffset(0, 0, 0, 0);
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        public static RectTransform CreateHorizontalGroup(Transform parent, float spacing)
        {
            var rect = CreateRect("Row", parent);
            var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.childControlWidth = false;
            group.childControlHeight = false;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            group.childAlignment = TextAnchor.MiddleRight;
            var layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 40;
            return rect;
        }
    }
}
