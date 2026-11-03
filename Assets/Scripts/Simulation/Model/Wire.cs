using System;

namespace LogicSim.Simulation
{
    /// <summary>Reference to a pin of a component. Direction is implied by context.</summary>
    public readonly struct PinRef : IEquatable<PinRef>
    {
        public readonly int ComponentId;
        public readonly int Pin;

        public PinRef(int componentId, int pin)
        {
            ComponentId = componentId;
            Pin = pin;
        }

        internal long Key => ((long)ComponentId << 32) | (uint)Pin;

        public bool Equals(PinRef other) => ComponentId == other.ComponentId && Pin == other.Pin;
        public override bool Equals(object obj) => obj is PinRef other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ComponentId, Pin);
        public override string ToString() => $"{ComponentId}:{Pin}";
    }

    /// <summary>Connection from an output pin to an input pin.</summary>
    public sealed class Wire
    {
        public int Id { get; internal set; }

        /// <summary>Driving output pin.</summary>
        public PinRef From { get; }

        /// <summary>Driven input pin.</summary>
        public PinRef To { get; }

        public Wire(PinRef from, PinRef to)
        {
            From = from;
            To = to;
        }

        public Wire(int id, PinRef from, PinRef to) : this(from, to)
        {
            Id = id;
        }

        public Wire Clone() => new Wire(Id, From, To);

        public override string ToString() => $"Wire#{Id} {From} -> {To}";
    }
}
