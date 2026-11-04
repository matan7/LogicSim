using System.Collections.Generic;
using Core;
using Core.AppState;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogicSim.UI
{
    /// <summary>
    /// Breadcrumb at the top of the canvas while an IC is open: "Main > Full adder > Half adder", where it gets its
    /// values from, and Done / Discard buttons. Clicking a crumb goes back to that level (keeping the changes).
    /// </summary>
    public class HierarchyBar : MonoBehaviour
    {
        private RectTransform _root;
        private RectTransform _crumbs;
        private TextMeshProUGUI _state;
        private Button _discard;
        private readonly List<GameObject> _crumbObjects = new List<GameObject>();
        private float _nextRefresh;

        public static HierarchyBar Create(Canvas canvas)
        {
            var rect = UiFactory.CreateRect("Hierarchy Bar", canvas.transform);
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.offsetMin = new Vector2(330, -58);
            rect.offsetMax = new Vector2(-20, -10);
            var bar = rect.gameObject.AddComponent<HierarchyBar>();
            bar.Build(rect);
            return bar;
        }

        private void Build(RectTransform rect)
        {
            _root = rect;
            UiFactory.AddImage(rect, UiFactory.PanelColor);
            var row = UiFactory.CreateHorizontalGroup(rect, 8);
            UiFactory.Stretch(row, 10, 10, 6, 6);
            var group = row.GetComponent<HorizontalLayoutGroup>();
            group.childAlignment = TextAnchor.MiddleLeft;

            _crumbs = UiFactory.CreateHorizontalGroup(row, 4);
            _crumbs.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            var crumbFitter = _crumbs.gameObject.AddComponent<ContentSizeFitter>();
            crumbFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            _crumbs.sizeDelta = new Vector2(10, 36);

            _state = UiFactory.CreateText(row, string.Empty, 16, UiFactory.MutedText, TextAlignmentOptions.MidlineLeft);
            _state.textWrappingMode = TextWrappingModes.NoWrap;
            _state.rectTransform.sizeDelta = new Vector2(420, 36);

            UiFactory.CreateButton(row, "Done", () => AppCommands.CloseSubcircuit(apply: true), 100, 36);
            _discard = UiFactory.CreateButton(row, "Discard", () => AppCommands.CloseSubcircuit(apply: false), 110, 36);

            SystemCore.CircuitService.LevelChanged += Rebuild;
            SystemCore.AppStateService.StateChanged += OnStateChanged;
            Rebuild();
        }

        private void OnDestroy()
        {
            if (SystemCore.CircuitService != null)
                SystemCore.CircuitService.LevelChanged -= Rebuild;
            if (SystemCore.AppStateService != null)
                SystemCore.AppStateService.StateChanged -= OnStateChanged;
        }

        private void OnStateChanged(ApplicationState state) => RefreshState();

        private void Rebuild()
        {
            var hierarchy = SystemCore.CircuitService.Hierarchy;
            _root.gameObject.SetActive(!hierarchy.IsAtRoot);
            foreach (var crumb in _crumbObjects)
                Destroy(crumb);
            _crumbObjects.Clear();
            if (hierarchy.IsAtRoot)
                return;

            var levels = hierarchy.Levels;
            for (int i = 0; i < levels.Count; i++)
            {
                if (i > 0)
                {
                    var separator = UiFactory.CreateText(_crumbs, ">", 18, UiFactory.MutedText, TextAlignmentOptions.Center);
                    separator.rectTransform.sizeDelta = new Vector2(16, 36);
                    _crumbObjects.Add(separator.gameObject);
                }
                int depth = i;
                var name = hierarchy.GetName(levels[i]);
                bool current = i == levels.Count - 1;
                var button = UiFactory.CreateButton(_crumbs, name, current ? (System.Action)null : () => AppCommands.CloseTo(depth, apply: true),
                    Mathf.Clamp(24 + name.Length * 10, 70, 220), 36);
                button.interactable = !current;
                _crumbObjects.Add(button.gameObject);
            }
            RefreshState();
        }

        private void Update()
        {
            if (_root.gameObject.activeSelf && Time.unscaledTime >= _nextRefresh)
                RefreshState();
        }

        private void RefreshState()
        {
            _nextRefresh = Time.unscaledTime + 0.25f;
            var service = SystemCore.CircuitService;
            var hierarchy = service.Hierarchy;
            if (hierarchy.IsAtRoot)
                return;
            var level = hierarchy.Current;
            bool simulating = SystemCore.AppStateService.IsSimulating;
            bool modified = hierarchy.IsModified(level);
            int instances = CountInstances(hierarchy, level.DefinitionId);
            string text;
            if (simulating)
            {
                text = hierarchy.GetInstancePath(level) != null
                    ? "<color=#5CFF73>Live values of this instance</color>"
                    : "No values: opened from the library";
            }
            else if (modified)
            {
                text = $"<color=#FFD24A>Modified</color> - Done updates {instances} instance{(instances == 1 ? "" : "s")}";
            }
            else
            {
                text = $"Editing IC, used {instances} time{(instances == 1 ? "" : "s")}";
            }
            _state.richText = true;
            _state.text = text;
            _discard.interactable = modified && !simulating;
        }

        /// <summary>Instances in the document and in all definitions (each definition counted once).</summary>
        private static int CountInstances(LogicSim.Simulation.CircuitHierarchy hierarchy, string definitionId)
        {
            int count = 0;
            foreach (var c in hierarchy.Root.InstancesOf(definitionId))
                count++;
            foreach (var definition in hierarchy.Library.Definitions)
                foreach (var c in definition.Circuit.InstancesOf(definitionId))
                    count++;
            return count;
        }
    }
}
