using System;
using System.Collections.Generic;

namespace LogicSim.Simulation
{
    /// <summary>
    /// Keeps a simulation engine in sync with an editable <see cref="Circuit"/>:
    /// recompiles after structural edits and forwards input changes.
    /// </summary>
    public sealed class SimulationSession : IDisposable, ISignalReader
    {
        public Circuit Circuit { get; }
        public ISimulationEngine Engine { get; }
        public CompiledCircuit Compiled { get; private set; }

        /// <summary>Raised after the netlist was rebuilt (all values were reset).</summary>
        public event Action Recompiled;

        public bool IsDirty => Compiled == null || Compiled.SourceVersion != Circuit.StructureVersion
            || Compiled.LibraryVersion != Circuit.Library.Version;

        public long Tick => Engine.Circuit == null ? 0 : Engine.Tick;

        public SimulationSession(Circuit circuit, ISimulationEngine engine)
        {
            Circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            Engine = engine ?? throw new ArgumentNullException(nameof(engine));
            Circuit.InputValueChanged += OnInputValueChanged;
        }

        public void EnsureCompiled()
        {
            if (!IsDirty)
                return;
            Engine.Complete();
            Compiled = CircuitCompiler.Compile(Circuit);
            Engine.Load(Compiled);
            Recompiled?.Invoke();
        }

        /// <summary>Power-on state with the inputs as they are in the model now (not as they were when compiled).</summary>
        public void Reset()
        {
            EnsureCompiled();
            Engine.Complete();
            Engine.Reset();
            foreach (var pair in Compiled.InputSlots)
            {
                if (Circuit.TryGetComponent(pair.Key, out var component))
                    Engine.SetInput(pair.Value, component.Value);
            }
        }

        public void Schedule(long ticks)
        {
            EnsureCompiled();
            Engine.Schedule(ticks);
        }

        public void Complete() => Engine.Complete();

        public void Run(long ticks)
        {
            Schedule(ticks);
            Complete();
        }

        /// <returns>True when the circuit settled within <paramref name="maxTicks"/>.</returns>
        public bool Settle(long maxTicks)
        {
            EnsureCompiled();
            Engine.Complete();
            return Engine.Settle(maxTicks);
        }

        public bool GetOutputValue(int componentId, int pin)
        {
            return Compiled != null && Engine.GetNet(Compiled.GetOutputNet(componentId, pin));
        }

        public bool GetInputPinValue(int componentId, int pin)
        {
            return Compiled != null && Engine.GetNet(Compiled.GetInputNet(componentId, pin));
        }

        public bool GetWireValue(Wire wire) => GetOutputValue(wire.From.ComponentId, wire.From.Pin);

        public bool TryGetNeuronPotential(CircuitComponent component, out float potential)
        {
            potential = 0;
            if (component.Kind == ComponentKind.SpikingNeuron)
            {
                if (Compiled?.RootScope == null || !Compiled.RootScope.TryGetNeuron(component.Id, out int neuron))
                    return false;
                potential = Engine.GetNeuronPotential(neuron);
                return true;
            }
            if (component.Kind != ComponentKind.Neuron || component.Neuron == null)
                return false;
            var weights = component.Neuron.Weights;
            for (int pin = 0; pin < weights.Count; pin++)
                if (GetInputPinValue(component.Id, pin))
                    potential += weights[pin];
            return true;
        }

        /// <summary>Value shown by a component: input pin 0 for sinks, otherwise output pin 0.</summary>
        public bool GetDisplayValue(CircuitComponent component)
        {
            if (component.Spec.IsSink)
                return GetInputPinValue(component.Id, 0);
            if (Circuit.GetOutputCount(component) > 0)
                return GetOutputValue(component.Id, 0);
            return false;
        }

        /// <summary>Unsigned value of all input pins, pin 0 being the least significant bit.</summary>
        public int GetInputBits(CircuitComponent component)
        {
            int value = 0;
            int count = Math.Min(30, Circuit.GetInputCount(component));
            for (int pin = 0; pin < count; pin++)
                if (GetInputPinValue(component.Id, pin))
                    value |= 1 << pin;
            return value;
        }

        /// <summary>Reader for the values inside an IC instance (empty path = the document).</summary>
        public ISignalReader CreateReader(IReadOnlyList<int> instancePath)
        {
            if (instancePath == null || instancePath.Count == 0)
                return this;
            return new InstanceSignalReader(this, instancePath);
        }

        private void OnInputValueChanged(CircuitComponent component)
        {
            if (IsDirty)
                return;
            int slot = Compiled.GetInputSlot(component.Id);
            if (slot >= 0)
                Engine.SetInput(slot, component.Value);
        }

        public void Dispose()
        {
            Circuit.InputValueChanged -= OnInputValueChanged;
            Engine.Dispose();
        }
    }
}
