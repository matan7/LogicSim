using System;

namespace LogicSim.Simulation
{
    /// <summary>
    /// Pins and size of a <see cref="ComponentKind.Screen"/>: a pixel display with a write port. Its inputs are the x
    /// address (bit 0 first), the y address, D, WR and CLR. On a rising edge of WR the pixel at (x, y) takes the value
    /// of D; a rising edge of CLR clears every pixel. Addresses outside the screen are ignored.
    /// </summary>
    public static class ScreenLayout
    {
        public const int MaxSize = 256;
        public const int DefaultWidth = 16;
        public const int DefaultHeight = 16;
        public const int MaxPixelSize = 8;
        public const int DefaultPixelSize = 1;

        public const string WidthSetting = "width";
        public const string HeightSetting = "height";
        public const string PixelSizeSetting = "pixel_size";

        /// <summary>D, WR and CLR follow the address bits.</summary>
        public const int ControlPins = 3;

        public static int Width(CircuitComponent component) => component.Settings.Get(WidthSetting, DefaultWidth);

        public static int Height(CircuitComponent component) => component.Settings.Get(HeightSetting, DefaultHeight);

        /// <summary>Grid units per pixel on the canvas (only the view uses it).</summary>
        public static int PixelSize(CircuitComponent component) => component.Settings.Get(PixelSizeSetting, DefaultPixelSize);

        /// <summary>Bits needed to address <paramref name="size"/> pixels: 0 for 1, 4 for 16, 5 for 20.</summary>
        public static int AddressBits(int size)
        {
            int bits = 0;
            while ((1 << bits) < size)
                bits++;
            return bits;
        }

        public static int XBits(CircuitComponent component) => AddressBits(Width(component));

        public static int YBits(CircuitComponent component) => AddressBits(Height(component));

        public static int InputCount(CircuitComponent component) => XBits(component) + YBits(component) + ControlPins;

        public static int DataPin(CircuitComponent component) => XBits(component) + YBits(component);

        public static int WritePin(CircuitComponent component) => DataPin(component) + 1;

        public static int ClearPin(CircuitComponent component) => DataPin(component) + 2;

        public static string InputName(CircuitComponent component, int pin)
        {
            int x = XBits(component), y = YBits(component);
            if (pin < 0)
                throw new ArgumentOutOfRangeException(nameof(pin));
            if (pin < x)
                return "x" + pin;
            if (pin < x + y)
                return "y" + (pin - x);
            switch (pin - x - y)
            {
                case 0: return "D";
                case 1: return "WR";
                case 2: return "CLR";
                default: return "in" + pin;
            }
        }
    }
}
