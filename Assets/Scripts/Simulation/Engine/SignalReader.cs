using System;
using System.Collections.Generic;
using System.Linq;

namespace LogicSim.Simulation
{
    /// <summary>Reads simulation values for the components of one circuit level.</summary>
    public interface ISignalReader
    {
        bool GetOutputValue(int componentId, int pin);
        bool GetInputPinValue(int componentId, int pin);
        bool GetWireValue(Wire wire);

        /// <summary>Value shown by a component: input pin 0 for sinks, otherwise output pin 0.</summary>
        bool GetDisplayValue(CircuitComponent component);

        /// <summary>Unsigned value of all input pins, pin 0 being the least significant bit.</summary>
        int GetInputBits(CircuitComponent component);
    }

    /// <summary>Shared implementation of the derived queries.</summary>
    public abstract class SignalReaderBase : ISignalReader
    {
        protected abstract Circuit Circuit { get; }

        public abstract bool GetOutputValue(int componentId, int pin);
        public abstract bool GetInputPinValue(int componentId, int pin);

        public bool GetWireValue(Wire wire) => GetOutputValue(wire.From.ComponentId, wire.From.Pin);

        public bool GetDisplayValue(CircuitComponent component)
        {
            if (component.Spec.IsSink)
                return GetInputPinValue(component.Id, 0);
            var circuit = Circuit;
            if (circuit != null && circuit.GetOutputCount(component) > 0)
                return GetOutputValue(component.Id, 0);
            return false;
        }

        public int GetInputBits(CircuitComponent component)
        {
            var circuit = Circuit;
            if (circuit == null)
                return 0;
            int value = 0;
            int count = Math.Min(30, circuit.GetInputCount(component));
            for (int pin = 0; pin < count; pin++)
                if (GetInputPinValue(component.Id, pin))
                    value |= 1 << pin;
            return value;
        }
    }

    /// <summary>
    /// Values inside one IC instance of the running document, addressed by the instance path from the document.
    /// Component ids are those of the definition (and of working copies, which keep them).
    /// </summary>
    public sealed class InstanceSignalReader : SignalReaderBase
    {
        private readonly SimulationSession _session;
        private readonly int[] _path;
        private CompiledCircuit _resolvedFor;
        private CompiledScope _scope;

        public IReadOnlyList<int> Path => _path;

        public InstanceSignalReader(SimulationSession session, IEnumerable<int> instancePath)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _path = (instancePath ?? Enumerable.Empty<int>()).ToArray();
        }

        /// <summary>The instance exists in the current netlist.</summary>
        public bool IsValid => Scope != null;

        private CompiledScope Scope
        {
            get
            {
                var compiled = _session.Compiled;
                if (!ReferenceEquals(compiled, _resolvedFor))
                {
                    _resolvedFor = compiled;
                    _scope = compiled?.FindScope(_path);
                }
                return _scope;
            }
        }

        protected override Circuit Circuit => Scope?.Circuit;

        public override bool GetOutputValue(int componentId, int pin)
        {
            var scope = Scope;
            return scope != null && _session.Engine.GetNet(_session.Compiled.GetOutputNet(scope, componentId, pin));
        }

        public override bool GetInputPinValue(int componentId, int pin)
        {
            var scope = Scope;
            return scope != null && _session.Engine.GetNet(_session.Compiled.GetInputNet(scope, componentId, pin));
        }
    }
}
