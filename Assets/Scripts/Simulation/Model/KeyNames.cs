using System.Collections.Generic;

namespace LogicSim.Simulation
{
    /// <summary>
    /// Keyboard keys a push button can be bound to, by name. Files store the name; the index is the setting's value, so
    /// new keys are appended at the end. Letters mean the letter the keyboard layout prints (Z is where the layout has
    /// it), digits the digit row.
    /// </summary>
    public static class KeyNames
    {
        public const string None = "None";

        public static readonly IReadOnlyList<string> All = Build();

        /// <summary>The list in short, for descriptions and errors.</summary>
        public const string Help = "None, A-Z, 0-9, Space, Enter, Tab, Backspace, Left, Right, Up, Down";

        private static string[] Build()
        {
            var keys = new List<string> { None };
            for (char c = 'A'; c <= 'Z'; c++)
                keys.Add(c.ToString());
            for (char c = '0'; c <= '9'; c++)
                keys.Add(c.ToString());
            keys.AddRange(new[] { "Space", "Enter", "Tab", "Backspace", "Left", "Right", "Up", "Down" });
            return keys.ToArray();
        }
    }
}
