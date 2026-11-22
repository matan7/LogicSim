# Component symbol format

Format of the component graphics for GPU-instanced rendering: one draw call for all component bodies, with their
on/off state read from a buffer of net values on the GPU instead of updated per GameObject on the CPU. See "View
performance" in `TODO.md` for why.

Status: agreed on 2026-11-22. The source art, the atlas builder and the instanced renderer for the gates and the
inputs/outputs with a fixed footprint exist (see "Renderer" at the end). Boxes, gates with more inputs, the net-bit
buffer and chunk culling do not yet. Wires (one mesh, colors from the same net buffer) are separate work and need no
art.

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

One RGBA8 atlas, linear (not sRGB), trilinear filtering (mipmaps). Every channel is a **signed distance field**
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
- Layers in each artboard: `outline`, `fill` and optionally `glow`. Shapes on transparency in any color: only alpha is
  used. In the template the outlines are white and the fills gray, so that both stay visible. Prefer vector shapes.
- Do not draw pin stubs or text.
- Save the PSD. Nothing is exported: the atlas builder reads the PSD itself.

The template exists: `Art/Symbols/Symbols.psd` (3100 × 1600) has one artboard per symbol with unit guides. The gate,
switch and bulb bodies were taken over from `Assets/Textures/GraphicAssets.psd` as vector shapes, scaled 4 times
(16 px inside strokes), without pin stubs and pin dots. First drafts of the new symbols, same stroke:

| Symbol | Outline | Fill |
|---|---|---|
| `io.button` | rounded body and a round cap | the cap |
| `io.clock` | rounded body and a square wave | the body |
| `io.constant` | rounded body with a second, inset border (the 0/1 glyph is drawn separately) | the inner area |
| `icon.neuron` | soma, three dendrites, axon with a fork | the soma |
| `icon.lif` | circle and a spike trace (round joins) | the circle, filled bottom-up by the potential |
| `icon.network` | 3-2-1 network: node rings and edges | the nodes |
| `mark.bubble` | circle of the gates' bubble size (0.96 units) | none |
| `mark.clock` | edge triangle open to the left, its tip 0.58 units into the box | none |

## Atlas builder (Unity editor)

**Tools > LogicSim > Build Symbol Atlas** (`Assets/Scripts/Editor/Symbols`) reads `Art/Symbols/Symbols.psd` directly:

- Every visible top-level artboard is a symbol named after the artboard. Its size must be whole units (multiples of
  100 px), otherwise it is skipped with a warning.
- In each artboard the visible layers named `outline`, `fill` and `glow` (case-insensitive; several layers with the
  same name are merged) become the R, G and B channels. A pixel is inside where the layer's alpha is at least 128.
  Shape layers work as they are, because the PSD stores their rasterized alpha.
- Only 8-bit PSD files (not PSB) are read.

Each layer becomes an exact Euclidean signed distance field (positive inside), sampled at 32 texels per unit with an
8-texel spread (0.25 units) and 9 texels of padding around each symbol. The symbols are shelf-packed into the smallest
power-of-two square, and the builder writes:

- `Assets/Art/Symbols/SymbolAtlas.png`: linear (not sRGB), mipmaps, trilinear, clamp, uncompressed RGBA32. With the
  18 symbols it is 1024 × 1024 and takes about 3 seconds to build.
- `Assets/Resources/SymbolSet.asset` (`LogicSim.Views.SymbolSet`): per symbol id the UV rect (padding included), the
  size in units, and per unit row where the outline starts and ends (the pin stubs run to there); also the texels per
  unit, the spread, the padding in units, the atlas and the shader `Assets/Shaders/Symbols.shader`. It is in
  Resources so that the app loads it and builds include the atlas and the shader.

Run it again after every change to the PSD and commit both outputs. `SymbolAtlasTests` checks the reader, the distance
field and the packed atlas.

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

## Renderer

What exists (`Views/SymbolRenderer.cs`, `Views/SymbolComponentView.cs`, `Shaders/Symbols.shader`):

- Components whose kind and pin count match a symbol get a `SymbolComponentView`: the 2-input gates, NOT, Buffer,
  Switch, Bulb, Button, Clock and Constant. It has no renderer, only a box collider of the footprint, the pin objects
  and, for the constant, its 0/1 text. Everything else keeps the sprite prefabs or `GenericComponentView`; so does
  everything when the GPU has no compute buffers in vertex shaders or the symbol set is missing (a warning is logged).
- One `Graphics.RenderPrimitives` call draws all instances: 6 vertices per instance, the quad built in the vertex
  shader from three structured buffers: symbols (UV rect, size, row edges), instances (16 bytes: position, symbol, quarter
  turns, selected, pin rows) and states (one word per instance: lit, values shown, the value of each pin row).
- Instances change only when components are added, removed, moved, dragged, rotated or (de)selected. States are
  computed on the CPU from the signal reader when the values change (about 1.2 microseconds per component) and
  uploaded as one buffer. The net-bit buffer from the contract above is the next step when this shows up in the
  profiler.
- The shader draws, back to front: the glow when lit, the pin stubs (colored like the wires, from the pin to the
  outline edge of that row), the fill (lit or off color), the outline (selection color and a little wider when
  selected), and below about 5 to 3 pixels per unit a solid rectangle that fades in.
- Draw order with the sprites works through the transparent queue: the draw call has sorting order 0, so it lands
  above the wires (-1) and below labels and pin dots.
- No chunks yet: the draw call's bounds cover all instances, and the vertex shader cost is negligible at 10k
  instances. Chunks pay off from hundreds of thousands.
