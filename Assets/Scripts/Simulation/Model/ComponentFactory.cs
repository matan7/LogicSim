namespace LogicSim.Simulation
{
    /// <summary>Creates detached components (not yet part of a circuit) with their initial properties.</summary>
    public static class ComponentFactory
    {
        public static CircuitComponent Create(ComponentKind kind, float x, float y, string label = null, int? inputs = null,
            int? clockHalfPeriod = null, bool? value = null, int rotation = 0)
        {
            var component = new CircuitComponent(kind, x, y);
            var spec = component.Spec;
            if (label != null)
                component.Label = label;
            if (inputs.HasValue && spec.HasVariableInputs)
            {
                if (inputs.Value < spec.MinInputs || inputs.Value > spec.MaxInputs)
                    throw new CircuitException($"{spec.DisplayName} supports {spec.MinInputs}..{spec.MaxInputs} inputs");
                component.InputCount = inputs.Value;
            }
            if (clockHalfPeriod.HasValue)
                component.ClockHalfPeriod = System.Math.Max(1, clockHalfPeriod.Value);
            if (value.HasValue)
                component.Value = value.Value;
            component.Rotation = ((rotation % 4) + 4) % 4;
            return component;
        }

        public static CircuitComponent CreateSubcircuit(string subcircuitId, float x, float y, string label = null)
        {
            if (string.IsNullOrEmpty(subcircuitId))
                throw new CircuitException("Subcircuit id must not be empty");
            var component = new CircuitComponent(ComponentKind.Subcircuit, x, y)
            {
                SubcircuitId = subcircuitId
            };
            if (label != null)
                component.Label = label;
            return component;
        }

        /// <summary>Moves a component that is not part of a circuit yet (e.g. before an add edit is applied).</summary>
        public static void SetDetachedPosition(CircuitComponent component, float x, float y)
        {
            component.X = x;
            component.Y = y;
        }
    }
}
