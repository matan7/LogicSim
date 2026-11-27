# TODO

Open work on branch `claude/optimistic-noether-1l3qr9`. Items are removed from this list when they are committed.
Details for the Unity editor work: `docs/UNITY_SESSION.md`.

## Needs the Unity editor (see docs/UNITY_SESSION.md)
- [ ] `Art/UI/Icons.psd` has no `comp.screen` artboard yet: the screen's palette icon
  (`Assets/Art/UI/Icons/comp.screen.png`, a monitor outline with six pixel squares, 14 px strokes on 256 px) was drawn
  without the icon builder. Rebuilding the icons keeps it, but add the artboard (row 4, after `comp.pinout`) so that
  the PSD stays the source.
- [ ] View performance for large circuits (the rest of step 5). Play mode with `BenchmarkCircuits.Random(1, 10000, 16)`
  (10,016 components, 15,197 wires) at 48k ticks/s, editor in Debug code mode, i7-5820K: median frame 66 ms, of which
  the player loop is 21 ms (126 and 77 ms with the sprite and line renderer views, measured in the same session).
  Done: no per-frame transform scan (was 9.5 ms), labels fitted once (TMP layout passes 5,300 -> 460 per frame),
  component bodies drawn by the instanced symbol renderer (9,057 of the 10,016 components; the clock labels no longer
  change color, canvas/TMP update 33 -> 4 ms), wires drawn by the instanced wire renderer and colored from the net
  values on the GPU (`CircuitView.Update` 37 -> 15 ms, camera render 1.3 -> 0.5 ms), symbols lit from the same net
  values (`CircuitView.Update` 15 -> 4.4 ms; what is left is the views without a symbol, the flip-flops here).
  Picking uses a spatial grid instead of colliders (`Views/SpatialGrid.cs`) and the selection holds ids, so the views
  have no colliders any more: median frame 54.6 -> 38.3 ms (the Scene view's gizmos 32 -> 6 ms), loading 13.8 -> 8.7 s.
  Filing the benchmark's 15,197 wires takes 67 ms, re-filing them 84 ms.
  Left, in the order of their cost:
  - Symbols not drawn by the renderer yet: gates with 3 to 8 inputs (vertical input bar), boxes for flip-flops, ICs,
    pins, displays and neurons, with the icons and markers from the atlas (`docs/SYMBOL_FORMAT.md`).
  - Loading such a circuit takes about 6.5 s (13.8 s before the grid, the font size cache and lazy captions): 5.2 s
    for the component views and 1.2 s for the wire views. A symbol view costs about 0.34 ms (its GameObject, the pin
    GameObjects, components added), a flip-flop drawn as a generic box 2.6 ms, of which 1.5 ms are its five
    TextMeshPro labels (0.3 ms each to create). Next steps: boxes in the symbol renderer with the labels as glyph
    instances (see the item below), pins as plain objects instead of GameObjects. The views no
    longer need GameObjects for picking; pins as plain objects and views pooled or drawn without GameObjects would
    remove most of them.
