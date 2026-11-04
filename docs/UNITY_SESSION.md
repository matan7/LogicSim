# Unity editor session: verification and follow-up work

Instructions for a Claude session that is connected to a running Unity editor (Unity MCP / `unity` CLI, able to read
the Console, enter Play mode, run the Test Runner, execute editor C# and take screenshots).

Work top to bottom. Each step ends with a commit. Stop and report to the user when a step fails in a way that needs a
decision.

## Context

- Branch: `claude/optimistic-noether-1l3qr9` (based on `refactoring`, not merged into `master`). Commit and push only
  to this branch. Do not open a pull request unless the user asks.
- Unity: 6000.4.1f1 (`ProjectSettings/ProjectVersion.txt`).
- What exists: see `README.md`. The pure C# parts (`Assets/Scripts/Simulation`, `Assets/Scripts/Mcp`) are covered by
  85 NUnit tests that pass with `dotnet test Headless/LogicSim.Headless.sln`. The Jobs backend has 9 parity tests
  against a stand-in of the Unity job API (`Headless/LogicSim.JobsTests`).
- **Never run in Unity yet.** Everything under `Assets/Scripts/{Core,App,UI,Views,Actions,Behaviours,Editor}` and
  `Simulation.Jobs` was only compiled outside Unity, against UnityEngine 2021.3 reference assemblies plus hand-written
  signature stubs for Input System, uGUI, TextMeshPro, Burst and `Awaitable`. Expect some compile errors where the
  real API differs from the stubs.

Language: answer the user in Croatian; code, comments, commits and docs in English.

## Step 1: open the project and fix compile errors

1. Open the project and wait for import and package resolution.
2. Check `Packages/manifest.json`:
   - `com.unity.burst` is pinned to `1.8.18`. That version was chosen without checking it against Unity 6000.4. If the
     Package Manager reports a problem, switch to the Burst version it offers for this editor.
   - The `com.unity.test-framework` version is `1.6.0`.
3. Read the Console and fix every compile error. Likely spots:
   - `Simulation.Jobs/SimulationJobs.cs`: `[BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict)]`.
     It is compiled only when Burst defines `LOGICSIM_BURST` (versionDefines in the asmdef), so it was never compiled.
   - TextMeshPro: `textWrappingMode`, `TextWrappingModes`, `TMP_InputField.onEndEdit`, `fontAsset`, `pointSize`
     (`UI/UiFactory.cs`, `UI/InspectorPanel.cs`, `UI/HierarchyBar.cs`, `Views/GenericComponentView.cs`).
   - `FindObjectsByType` / `FindFirstObjectByType` overloads (`UI/AppUi.cs`, `App/LogicSimBootstrap.cs`).
   - Input System callbacks and `InputActionAsset` APIs (`Core/Input/InputService.cs`, `Actions/*`).
4. Make sure there are no warnings about missing scripts on the scene objects (`Assets/Scenes/SampleScene.unity`).
5. Commit: `Fix compile errors found in the Unity editor`.

## Step 2: tests in the Unity Test Runner

1. Run all EditMode tests (`LogicSim.Tests.EditMode`). They are the same 85 tests as the dotnet run. All must pass.
2. Add Jobs parity tests that use the real Job System and Burst:
   - Create `Assets/Tests/EditMode/Jobs/LogicSim.Tests.Jobs.asmdef` (Editor only; references `LogicSim.Simulation`,
     `LogicSim.Simulation.Jobs`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner`; `nunit.framework.dll`).
   - Port `Headless/LogicSim.JobsTests/JobsEngineTests.cs`. Drop the `Unity.Collections.Tracker` assertion (that
     counter only exists in the stub). Keep the neuron parity test: managed and Burst potentials must match exactly.
3. If a parity test fails only with Burst, look at float handling in `Engine/SimulationKernel.cs`
   (`EvaluateSpiking`, perceptron sum) and at the `FloatMode` attribute.
4. Commit: `Run the Jobs parity tests with the real job system`.

## Step 3: check the app in Play mode

Use Play mode with screenshots. Fix what is broken, and give the user a short report with the screenshots. Check:

**Start-up**
- A `LogicSim` object is created by `LogicSimBootstrap` (views, driver, MCP host, UI). The Console shows no
  exceptions.
- The status bar shows the MCP endpoint `http://127.0.0.1:8765/mcp`.

**Palette and toolbar**
- Logic gates section: D, T, JK flip-flops, Neuron (N), Spiking neuron (LIF), Neural block (NN).
- IO section: Button, Clock, Constant, Hex display.
- Custom circuits section: Create IC, Collapse, Open IC, Explode, Make unique, Input pin, Output pin, and one entry per
  IC. Check that sections do not overlap and that the panel scrolls (`FitSection` and `MakeToolPanelScrollable` in
  `UI/AppUi.cs`).

**Editing basics**
- Place a component, wire two pins, rewire, delete, undo and redo, copy and paste, rotate.

**Simulation**
- F5 starts and stops. Clicking a switch toggles it. Wires and bulbs change color. P pauses, `.` steps.

**IC hierarchy** (Ctrl+O, open the `4-bit-adder` example)
- Double-click a full-adder instance in edit mode:
  - the breadcrumb `Main > Full adder` appears at the top;
  - the camera frames the IC;
  - the palette dims the ICs that would contain themselves.
- Inside the IC, move an input pin above another one, rename a pin in the inspector, then click Done. Every instance
  updates, wires stay on the moved/renamed pins, and the truth table (TT button) is unchanged. One Ctrl+Z undoes the
  whole IC edit.
- Open the IC again, delete something, then click Discard. Nothing changes outside.
- Esc with nothing selected goes back one level and restores the camera.
- In simulation mode, double-click an instance. Values inside show that instance's signals, and the breadcrumb says
  "Live values of this instance". Nested ICs work too.
- Select the gates of the `half-adder` example (not the switches/bulbs) and click Collapse:
  - a new IC replaces them;
  - pins are named after the switches/bulbs;
  - the truth table is unchanged.
- Explode and Make unique on an instance.

**Neurons** (Ctrl+O, examples `neurons` and `spiking-neuron`)
- Select a Neuron. The inspector on the right shows Label, Inputs, Weights, Threshold and Train. Change a weight, then
  undo.
- Train field: type `0001` (AND) on the neuron and `0110` (XOR) on the neural block. A toast reports the accuracy, and
  XOR on the single neuron reports that it is not linearly separable.
- Neural block: Layers `2 6 1`, the Activation button, New weights, and "Learn an IC..." with the half adder.
- Spiking neuron example in simulation: hold Weak and then Strong. The spike rate goes up (about 12 vs 18 spikes per
  100 ticks at low speed), and the label shows the potential `V 0.xx`.

**GPU lookup tables**
- Create a Neural block with Layers `12 16 2` and start the simulation. The Console must show
  `[LogicSim] Neural lookup table built on the GPU ...` (`App/ComputeLutBuilder.cs`,
  `Assets/Resources/NeuralLut.compute`).
- Add an editor test that compares `ComputeLutBuilder` with `NeuralNetwork.BuildLutOnCpu()` on about 10 random networks
  (sigmoid, tanh, relu; 10–16 inputs). Report the number of differing bits. They are allowed only for patterns whose
  output is within float rounding of 0.5. If differences are frequent, check the shader.

**MCP in the running app**
- `claude mcp add --transport http logicsim http://127.0.0.1:8765/mcp`
- Call `build_circuit`, `run_simulation`, `open_subcircuit` and `close_subcircuit`. The editor view must switch levels
  live, and every MCP edit must be undoable with Ctrl+Z.

Commit fixes per area, e.g. `Fix inspector layout`, `Fix IC breadcrumb in simulation mode`.

## Step 4: benchmark

- Run **Tools > LogicSim > Run Simulation Benchmark** (Burst enabled, Release-like settings).
- Put the Jobs + Burst numbers next to the managed numbers in the README table. Say which machine they come from.
- With a large random circuit (`BenchmarkCircuits.Random(1, 10000, 16)`, loaded through an editor script), capture
  one Profiler frame in Play mode. Find out whether rendering/views or the simulation is the bottleneck, and report it.
- Commit: `Add Jobs + Burst benchmark results`.

## Step 5: architecture improvements that need the editor

Do these only after steps 1–4 pass, in this order, each as its own commit with all tests green:

1. **Assembly definitions for the app code.**
   - Add `LogicSim.App` (App, UI, Views, Actions, Behaviours, Commands, `DrawingSelectionManager.cs`). It references
     Core, LogicSim.Simulation, LogicSim.Simulation.Jobs, LogicSim.Mcp, Unity.InputSystem, Unity.TextMeshPro and
     UnityEngine.UI.
   - Move `DrawingSelectionManager.cs` into a folder and namespace.
   - Fix the `Editor.asmdef` references.
2. **No string lookups.**
   - Generate the C# class for `Assets/Data/GeneralInputActions.inputactions` and use it instead of
     `InputService.Find("...")`.
   - Replace `AppUi.FindDeep("UndoButton")` and similar with serialized references on a component in the scene.
3. **PlayMode tests** (Input System `InputTestFixture`) for these flows:
   - place a gate from the palette;
   - wire two pins;
   - toggle a switch in simulation;
   - double-click into an IC and back.
4. **Wire rendering** (only if step 4 showed views as the bottleneck): draw wires as one mesh and use a spatial index
   for pointer hit tests instead of one `EdgeCollider2D` per wire.

## Can also be done without Unity

These items from the architecture review do not need the editor (cheaper in a headless session):

- Move `CircuitDocumentStore` from `Mcp/ILogicSimWorkspace.cs` to `Simulation/Serialization`, and drop Core's
  reference to `LogicSim.Mcp`.
- Replace `CircuitHierarchy.IsModified` (full circuit comparison, called every frame inside an IC while simulating)
  with a version counter of the working copy.
- Add a batch scope to `Circuit` events (like `SubcircuitLibrary.BatchUpdate`) for paste and collapse.
- Split `Mcp/LogicSimTools.cs` (about 1500 lines) by area: inspection, editing, simulation, subcircuits, neurons.
- Add an optional token for the MCP HTTP transport.
- Use a selection model keyed by component ids, with a `Changed` event, instead of selection keyed by views (the
  inspector currently rebuilds the id list every frame).
- Per-kind component behaviors, so a new component kind does not touch about 8 files. This is the largest item.

## Leftover outside this branch

`Assets/logicsim-complete.bundle` was committed by the user to the `refactoring` branch to hand over the work. Delete
it there only if the user agrees.
