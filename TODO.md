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
- [ ] Step 1: fix compile errors against the real packages (code was only compiled against 2021.3 reference DLLs + stubs); check the Burst version.
- [ ] Step 2: EditMode tests in the Unity Test Runner; Jobs parity tests with the real job system and Burst.
- [ ] Step 3: Play mode check of every feature (palette, editing, ICs, neurons, inspector, GPU LUT, MCP).
- [ ] Step 4: Jobs + Burst benchmark numbers in the README; Profiler capture of a large circuit.
- [ ] Step 5: asmdefs for the app code, no string lookups (generated input class, serialized UI references), PlayMode tests, wire mesh batching if views are the bottleneck.

## Outside this branch
- [ ] `Assets/logicsim-complete.bundle` on `refactoring`: delete only if the user agrees.
