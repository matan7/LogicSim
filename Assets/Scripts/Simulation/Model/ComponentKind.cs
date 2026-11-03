namespace LogicSim.Simulation
{
    /// <summary>
    /// Every element that can be placed on the canvas. Names are part of the file format, do not rename.
    /// </summary>
    public enum ComponentKind
    {
        // Logic gates
        Buffer,
        Not,
        And,
        Or,
        Nand,
        Nor,
        Xor,
        Xnor,

        // Inputs
        Switch,
        Button,
        Clock,
        Constant,

        // Outputs
        Bulb,
        HexDisplay,

        // Sequential
        DFlipFlop,
        TFlipFlop,
        JKFlipFlop,

        // Subcircuit interface and instances
        PinIn,
        PinOut,
        Subcircuit
    }

    public enum ComponentCategory
    {
        Gate,
        Input,
        Output,
        Sequential,
        Subcircuit
    }
}
