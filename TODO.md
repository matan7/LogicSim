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
- [ ] Letter shortcuts other than Undo/Redo bind physical keys (`<Keyboard>/c`, `a`, `s`, ...). Fine on QWERTY/QWERTZ;
  on AZERTY Ctrl+A is on the Q key. Character bindings (`#(A)`) would follow the layout but do not resolve on
  non-Latin layouts, so decide per shortcut.
- [ ] ReLU blocks often fail to learn small tables (XOR, 2-6-1: 5 of 10 seeds; sigmoid 2-4-1: 10 of 10). Try He
  initialisation and a small positive bias for ReLU, or retry training from a few seeds and keep the best.
- [ ] Make unique on the only instance of an IC still asks for a name and makes a copy; say it is already unique.
- [ ] View performance for large circuits (the rest of step 5). Play mode with `BenchmarkCircuits.Random(1, 10000, 16)`
  (10,016 components, 15,197 wires) at 48k ticks/s, editor in Debug code mode, i7-5820K: median frame 89 ms (126 ms
  with the sprite views in the same session).
  Done: no per-frame transform scan (was 9.5 ms), labels fitted once (TMP layout passes 5,300 -> 460 per frame),
  component bodies drawn by the instanced symbol renderer (9,057 of the 10,016 components; the clock labels no longer
  change color, canvas/TMP update 33 -> 4 ms).
  Left, in the order of their cost:
  - `CircuitView.Update` (`ApplyValues`): about 37 ms. Measured in Play mode: 15,197 wire views 17 ms, 9,057 symbol
    states 11 ms, the other 959 views 2 ms, nearly all of it value lookups. Update only what changed (compare the net
    values with the last frame and map nets to views), or move the symbol states to the GPU: a net-bit buffer and
    per-instance net indices as in `docs/SYMBOL_FORMAT.md`.
  - Editor only: the Scene view's gizmos for 25k colliders take about 32 ms; close the Scene view when profiling.
  - Wires: one GameObject with a LineRenderer and an `EdgeCollider2D` each. Draw them as one mesh (colors from the net
    values) and use a spatial index for pointer hit tests; this also needs wire selection without per-wire objects.
  - Symbols not drawn by the renderer yet: gates with 3 to 8 inputs (vertical input bar), boxes for flip-flops, ICs,
    pins, displays and neurons, with the icons and markers from the atlas (`docs/SYMBOL_FORMAT.md`).
  - Loading such a circuit creates about 25k GameObjects and takes 10 to 15 s.
