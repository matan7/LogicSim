using System;

namespace LogicSim.Simulation
{
    /// <summary>
    /// A component placed in a circuit. Mutated only through <see cref="Circuit"/> so that change events stay consistent.
    /// </summary>
    public sealed class CircuitComponent
    {
        public const int DefaultClockHalfPeriod = 300;

        public int Id { get; internal set; }
        public ComponentKind Kind { get; }
        public float X { get; internal set; }
        public float Y { get; internal set; }

        /// <summary>Quarter turns clockwise, 0..3.</summary>
        public int Rotation { get; internal set; }

        public string Label { get; internal set; } = string.Empty;

        /// <summary>Input count for gates with a variable number of inputs.</summary>
        public int InputCount { get; internal set; }

        /// <summary>Clock half period in ticks.</summary>
        public int ClockHalfPeriod { get; internal set; } = DefaultClockHalfPeriod;

        /// <summary>Switch/Button/PinIn state or Constant value.</summary>
        public bool Value { get; internal set; }

        /// <summary>Definition id for <see cref="ComponentKind.Subcircuit"/>.</summary>
        public string SubcircuitId { get; internal set; } = string.Empty;

        /// <summary>Weights and thresholds of a <see cref="ComponentKind.Neuron"/> or <see cref="ComponentKind.SpikingNeuron"/>.</summary>
        public NeuronParameters Neuron { get; internal set; }

        /// <summary>Network of a <see cref="ComponentKind.NeuralBlock"/>; its layers define the pins.</summary>
        public NeuralNetwork Network { get; internal set; }

        public bool IsNeuron => Kind == ComponentKind.Neuron || Kind == ComponentKind.SpikingNeuron;

        public ComponentSpec Spec => ComponentSpec.Get(Kind);

        public CircuitComponent(ComponentKind kind)
        {
            Kind = kind;
            InputCount = ComponentSpec.Get(kind).DefaultInputs;
            if (IsNeuron)
                Neuron = NeuronParameters.Default(kind, InputCount);
            else if (kind == ComponentKind.NeuralBlock)
                Network = NeuralNetwork.Default;
        }

        public CircuitComponent(ComponentKind kind, float x, float y) : this(kind)
        {
            X = x;
            Y = y;
        }

        public ComponentProperties GetProperties()
        {
            return new ComponentProperties
            {
                Label = Label,
                Rotation = Rotation,
                InputCount = InputCount,
                ClockHalfPeriod = ClockHalfPeriod,
                Value = Value,
                Neuron = Neuron,
                Network = Network
            };
        }

        internal void ApplyProperties(ComponentProperties properties)
        {
            Label = properties.Label ?? string.Empty;
            Rotation = ((properties.Rotation % 4) + 4) % 4;
            InputCount = properties.InputCount;
            ClockHalfPeriod = Math.Max(1, properties.ClockHalfPeriod);
            Value = properties.Value;
            if (IsNeuron)
                Neuron = (properties.Neuron ?? Neuron ?? NeuronParameters.Default(Kind, InputCount)).WithInputCount(InputCount);
            else if (Kind == ComponentKind.NeuralBlock)
                Network = properties.Network ?? Network ?? NeuralNetwork.Default;
        }

        /// <summary>Keeps neuron weights in step with the input count.</summary>
        internal void NormalizeNeuralState()
        {
            if (IsNeuron)
                Neuron = (Neuron ?? NeuronParameters.Default(Kind, InputCount)).WithInputCount(InputCount);
            else if (Kind == ComponentKind.NeuralBlock && Network == null)
                Network = NeuralNetwork.Default;
        }

        /// <summary>Deep copy with the same id. Used for snapshots.</summary>
        public CircuitComponent Clone()
        {
            return new CircuitComponent(Kind)
            {
                Id = Id,
                X = X,
                Y = Y,
                Rotation = Rotation,
                Label = Label,
                InputCount = InputCount,
                ClockHalfPeriod = ClockHalfPeriod,
                Value = Value,
                SubcircuitId = SubcircuitId,
                Neuron = Neuron,
                Network = Network
            };
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Label) ? $"{Kind}#{Id}" : $"{Kind}#{Id} '{Label}'";
        }
    }

    /// <summary>Editable, non-positional properties of a component.</summary>
    public struct ComponentProperties : IEquatable<ComponentProperties>
    {
        public string Label;
        public int Rotation;
        public int InputCount;
        public int ClockHalfPeriod;
        public bool Value;
        public NeuronParameters Neuron;
        public NeuralNetwork Network;

        public bool Equals(ComponentProperties other)
        {
            return string.Equals(Label ?? string.Empty, other.Label ?? string.Empty) && Rotation == other.Rotation
                && InputCount == other.InputCount && ClockHalfPeriod == other.ClockHalfPeriod && Value == other.Value
                && Equals(Neuron, other.Neuron) && Equals(Network, other.Network);
        }

        public override bool Equals(object obj) => obj is ComponentProperties other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Label, Rotation, InputCount, ClockHalfPeriod, Value);
    }
}
