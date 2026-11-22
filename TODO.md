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
- [ ] Step 3 leftovers (the rest of step 3 is done):
  - Mouse and keyboard by hand: drag from the palette, wire pin to pin, rewire/disconnect by dragging an input, box and
    Shift selection, double-click into an IC, Esc back, and the shortcuts (Ctrl+Z/Y/C/V/X, Del, R, F, F5, P, `.`).
    Simulated input could not be checked: see "Simulated input" in docs/UNITY_SESSION.md.
  - Editor test comparing `ComputeLutBuilder` with `BuildLutOnCpu`. It needs the app code in an asmdef (step 5);
    an eval run on 10 random networks found 0 differing bits.
- [ ] ReLU blocks often fail to learn small tables (XOR, 2-6-1: 5 of 10 seeds; sigmoid 2-4-1: 10 of 10). Try He
  initialisation and a small positive bias for ReLU, or retry training from a few seeds and keep the best.
- [ ] Make unique on the only instance of an IC still asks for a name and makes a copy; say it is already unique.
- [ ] Step 4: Jobs + Burst benchmark numbers in the README; Profiler capture of a large circuit.
- [ ] Step 5: asmdefs for the app code, no string lookups (generated input class, serialized UI references), PlayMode tests, wire mesh batching if views are the bottleneck.
