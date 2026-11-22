# Component symbol format

Format of the component graphics for GPU-instanced rendering: one draw call for all component bodies, with their
on/off state read from a buffer of net values on the GPU instead of updated per GameObject on the CPU. See "View
performance" in `TODO.md` for why.

Status: agreed on 2026-11-22. Not implemented yet. Wires (one mesh, colors from the same net buffer) are separate
work and need no art.

## Geometry

The model and the wires depend on pin positions, so **footprints and pins stay exactly as they are today**. Saved
circuits, the auto layout and the MCP tools need no change.

- 1 world unit is one grid step. Component positions snap to whole units.
- A component's origin is its top-left corner: x = 0 is the input pin column, y = 0 the top edge. The body extends to
  negative y.
- Inputs are 2 units apart. Pins sit on whole-unit coordinates.

| Kind | Footprint (w × h) | Inputs | Outputs |
|---|---|---|---|
| AND, OR, XOR, NAND, NOR, XNOR (2 inputs) | 5 × 4 | (0, −1), (0, −3) | (5, −2) |
| NOT, Buffer | 5 × 4 | (0, −2) | (5, −2) |
| Switch | 5 × 4 | | (5, −2) |
| Bulb | 5 × 4 | (0, −2) | |
| Everything else (see below) | `GenericComponentView` | | |

The other kinds keep the layout of `GenericComponentView`: width 6 (4 for Clock, Button, Constant, PinIn, PinOut),
height 2 × rows + 2 with rows = max(inputs, outputs). Pin i of a column sits at (x, −2 − 2i − (rows − count)), with x = 0
for inputs and x = width for outputs. Gates with 3 to 8 inputs use this layout too.

## Texture channels

One RGBA8 atlas, linear (not sRGB), bilinear filtering with mipmaps. Every channel is a **signed distance field**
(SDF): 0.5 is the edge of the shape, larger values are inside. The distance range (spread) is 8 texels on each side of
the edge, which is 0.25 units at 32 texels per unit.

| Channel | Content | Drawn |
|---|---|---|
| R | Outline: the stroke as drawn, including its width | Always, in the outline color |
| G | Fill: the area that lights up | When the component's value is 1 |
| B | Glow (optional), e.g. around a lit bulb | When the value is 1; 0 where unused |
| A | Unused | Always 255 |

Colors are not baked into the texture. The shader takes them from `VisualStyle` (outline, fill on, fill off,
selection), so themes stay possible.

The shader does the antialiasing (`fwidth` of the distance), so MSAA is not needed. It keeps strokes at least one pixel
wide when zoomed out. A partial fill (for example a spiking neuron's potential) is computed in the shader by filling
the G area from the bottom up to the fraction; it needs no extra channel.

## What the atlas contains

Only shapes that cannot be drawn as rectangles or straight lines:

| Symbol id | Size (units) | Fill is lit when |
|---|---|---|
| `gate.and`, `gate.or`, `gate.xor`, `gate.nand`, `gate.nor`, `gate.xnor` | 5 × 4 | the output is 1 |
| `gate.not`, `gate.buf` | 5 × 4 | the output is 1 |
| `io.switch` | 5 × 4 | the switch is on |
| `io.bulb` | 5 × 4 | the input is 1 |
| `io.button` | 4 × 4 | the button is held |
| `io.clock` (wave icon) | 4 × 4 | the output is 1 |
| `io.constant` | 4 × 4 | the value is 1 |
| `icon.neuron`, `icon.lif`, `icon.network` | 2 × 2, centered in their box | the output is 1 (LIF: partial fill by potential) |
| `mark.bubble` (inversion circle) | 1 × 1 | never (outline only) |
| `mark.clock` (edge triangle at a CLK input) | 1 × 1 | never (outline only) |

Gate bodies are drawn without pin stubs. NAND, NOR, XNOR and NOT include their inversion bubble in the body art.

## What the shader draws without art

- Pin stubs, and for gates with 3 to 8 inputs a vertical input bar connecting all inputs (the extended input line of
  ANSI symbols). The body art stays at the 2-input size, centered on the output.
- Boxes (rounded rectangles of the footprint size) for flip-flops, ICs, hex displays, input and output pins, neurons,
  spiking neurons and neural blocks. Their icons and markers come from the atlas.
- The seven segments of the hex display.

## Text

The atlas contains no text. Letters and names (D, T, JK, CLK, Q, nQ, IC and pin names, user labels) are drawn
separately and always upright, also on rotated components, for example as glyph instances from the TextMeshPro SDF
font atlas. They are shown only above a zoom threshold.

## Authoring in PhotoCraft

- Source file: `Art/Symbols/Symbols.psd`, outside `Assets` so that Unity does not import it.
- Scale: **100 px per unit** (4 times the current 25). A 5 × 4 gate is 500 × 400 px.
- One artboard per symbol, named with its symbol id, sized to the symbol in whole units, with the origin at the top
  left. Guides every 100 px; pin positions from the geometry table.
- Layers in each artboard: `outline`, `fill` and optionally `glow`. White shapes on transparency (only alpha is used).
  Prefer vector shapes.
- Do not draw pin stubs or text.
- Export every layer as PNG to `Art/Symbols/export/<symbol id>_<layer>.png`.

The current gate shapes in `Assets/Textures/GraphicAssets.psd` are vector shapes; they only need to be scaled 4 times
and split into `outline` and `fill`. New art: `io.button`, `io.clock`, `io.constant`, the three icons and the two
markers.

## Atlas builder (Unity editor)

**Tools > LogicSim > Build Symbol Atlas** reads `Art/Symbols/export`, computes the SDF of each layer at 32 texels per unit
with an 8-texel spread, packs the symbols into a power-of-two RGBA8 atlas with padding, and writes:

- `Assets/Art/Symbols/SymbolAtlas.png`
- `Assets/Art/Symbols/SymbolSet.asset`: per symbol id the UV rect, the size in units and the origin.

## Rendering contract

- Per instance, updated only when the circuit changes: position, rotation (0 to 3 quarter turns), symbol index,
  footprint size, input count, the index of the net that drives the fill, and flags (selected, hovered, dimmed).
- Net values: a `StructuredBuffer<uint>` with one bit per net, uploaded once per frame (27 KB for 220k nets).
- Drawn with `Graphics.RenderMeshInstanced` or `RenderMeshIndirect` on a quad. The world is split into chunks, and only
  chunks that intersect the camera are drawn.
- Level of detail: below about 4 px per unit, a component is drawn as a solid rectangle in its state color.
- Shaders are plain unlit HLSL without a `LightMode` tag, so they run in the built-in pipeline and in URP.
- Interaction first keeps one GameObject with a collider per component for picking, dragging and selection, without
  renderers or text. A spatial grid can replace these later.
