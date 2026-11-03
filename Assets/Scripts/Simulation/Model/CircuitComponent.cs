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

        public ComponentSpec Spec => ComponentSpec.Get(Kind);

        public CircuitComponent(ComponentKind kind)
        {
            Kind = kind;
            InputCount = ComponentSpec.Get(kind).DefaultInputs;
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
                Value = Value
            };
        }

        internal void ApplyProperties(ComponentProperties properties)
        {
            Label = properties.Label ?? string.Empty;
            Rotation = ((properties.Rotation % 4) + 4) % 4;
            InputCount = properties.InputCount;
            ClockHalfPeriod = Math.Max(1, properties.ClockHalfPeriod);
            Value = properties.Value;
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
                SubcircuitId = SubcircuitId
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

        public bool Equals(ComponentProperties other)
        {
            return string.Equals(Label ?? string.Empty, other.Label ?? string.Empty) && Rotation == other.Rotation
                && InputCount == other.InputCount && ClockHalfPeriod == other.ClockHalfPeriod && Value == other.Value;
        }

        public override bool Equals(object obj) => obj is ComponentProperties other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Label, Rotation, InputCount, ClockHalfPeriod, Value);
    }
}
