# TODO

Open work on branch `claude/optimistic-noether-1l3qr9`. Items are removed from this list when they are committed.
Details for the Unity editor work: `docs/UNITY_SESSION.md`.

## Without the Unity editor
- [ ] Selection model keyed by component ids with a `Changed` event, instead of selection keyed by views (the inspector polls every frame).
- [ ] Per-kind component behaviors so a new component kind does not touch ~8 files (largest item).

## Needs the Unity editor (see docs/UNITY_SESSION.md)
- [ ] Step 1: fix compile errors against the real packages (code was only compiled against 2021.3 reference DLLs + stubs); check the Burst version.
- [ ] Step 2: EditMode tests in the Unity Test Runner; Jobs parity tests with the real job system and Burst.
- [ ] Step 3: Play mode check of every feature (palette, editing, ICs, neurons, inspector, GPU LUT, MCP).
- [ ] Step 4: Jobs + Burst benchmark numbers in the README; Profiler capture of a large circuit.
- [ ] Step 5: asmdefs for the app code, no string lookups (generated input class, serialized UI references), PlayMode tests, wire mesh batching if views are the bottleneck.

## Outside this branch
- [ ] `Assets/logicsim-complete.bundle` on `refactoring`: delete only if the user agrees.
