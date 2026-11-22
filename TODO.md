# TODO

Open work on branch `claude/optimistic-noether-1l3qr9`. Items are removed from this list when they are committed.
Details for the Unity editor work: `docs/UNITY_SESSION.md`.

## Without the Unity editor
- [ ] Selection still stores views (`ISelectable`). Storing component/wire ids would let undo and MCP restore a selection without views. Optional; the inspector no longer polls.
- [ ] Per-kind component behaviors. Done: opcode, aliases and property help live in `ComponentSpec`. Still spread
  per kind: property editors (`UI/InspectorPanel.cs`), labels and visuals (`Views/GenericComponentView.cs`), MCP
  property arguments (`Mcp/LogicSimTools.Helpers.cs`, `LogicSimTools.Neurons.cs`), DTO fields in
  `CircuitSerializer`, pin counts of NeuralBlock/Subcircuit in `Circuit`. Next step: one behavior object per kind for
  properties (parse, serialize, describe); the view part needs the Unity editor to verify.

## Needs the Unity editor (see docs/UNITY_SESSION.md)
- [ ] Fix the 16 CS0618 warnings (`FindObjectsByType` with `FindObjectsSortMode` in `UI/AppUi.cs`, `FindFirstObjectByType` in `Views/VisualStyle.cs`).
- [ ] Editor test comparing `ComputeLutBuilder` with `BuildLutOnCpu` (left over from step 3). It needs the app code in
  an asmdef (step 5); an eval run on 10 random networks found 0 differing bits.
- [ ] Letter shortcuts other than Undo/Redo bind physical keys (`<Keyboard>/c`, `a`, `s`, ...). Fine on QWERTY/QWERTZ;
  on AZERTY Ctrl+A is on the Q key. Character bindings (`#(A)`) would follow the layout but do not resolve on
  non-Latin layouts, so decide per shortcut.
- [ ] ReLU blocks often fail to learn small tables (XOR, 2-6-1: 5 of 10 seeds; sigmoid 2-4-1: 10 of 10). Try He
  initialisation and a small positive bias for ReLU, or retry training from a few seeds and keep the best.
- [ ] Make unique on the only instance of an IC still asks for a name and makes a copy; say it is already unique.
- [ ] Step 5: asmdefs for the app code, no string lookups (generated input class, serialized UI references), PlayMode
  tests, then view performance (below).
- [ ] View performance for large circuits (step 4 found views to be the bottleneck). Play mode with
  `BenchmarkCircuits.Random(1, 10000, 16)` (10,016 components, 15,197 wires), editor, i7-5820K: about 120 ms per frame.
  - `CircuitView.Update` + `LateUpdate`: about 23 ms (per-view value and color updates).
  - TextMeshPro: about 5,900 labels re-laid out per frame, about 21 ms. Likely the label color changes of clocks and
    other toggling parts (`GenericComponentView.ShowValues`); a TMP color change rebuilds the mesh. Show the value
    without touching the TMP color, or only for visible views.
  - Loading the circuit creates 25k GameObjects in about 10 s.
  - Wires: draw them as one mesh and use a spatial index for pointer hit tests instead of one `EdgeCollider2D` each.
  - Editor only: the Scene view's gizmos for 25k colliders took about 33 ms; close the Scene view when profiling.
