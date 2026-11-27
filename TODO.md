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
- [ ] View performance for large circuits (the rest of step 5). Play mode with `BenchmarkCircuits.Random(1, 10000, 16)`
  (10,016 components, 15,197 wires) at 48k ticks/s, editor in Debug code mode, i7-5820K: median frame 66 ms, of which
  the player loop is 21 ms (126 and 77 ms with the sprite and line renderer views, measured in the same session).
  Done: no per-frame transform scan (was 9.5 ms), labels fitted once (TMP layout passes 5,300 -> 460 per frame),
  component bodies drawn by the instanced symbol renderer (9,057 of the 10,016 components; the clock labels no longer
  change color, canvas/TMP update 33 -> 4 ms), wires drawn by the instanced wire renderer and colored from the net
  values on the GPU (`CircuitView.Update` 37 -> 15 ms, camera render 1.3 -> 0.5 ms).
  Left, in the order of their cost:
  - Editor only: the Scene view's gizmos for 25k colliders take about 33 ms; close the Scene view when profiling.
  - `CircuitView.Update` (`ApplyValues`): about 15 ms, nearly all of it the symbol states (value lookups per pin).
    Give the symbol instances net indices and read the same net buffer as the wires (`docs/SYMBOL_FORMAT.md`).
  - Picking: every wire and component still has a GameObject with a collider. A spatial index for pointer hit tests
    and selection by id (see the selection item above) would remove them.
  - Symbols not drawn by the renderer yet: gates with 3 to 8 inputs (vertical input bar), boxes for flip-flops, ICs,
    pins, displays and neurons, with the icons and markers from the atlas (`docs/SYMBOL_FORMAT.md`).
  - Loading such a circuit creates about 25k GameObjects and takes 10 to 15 s.
