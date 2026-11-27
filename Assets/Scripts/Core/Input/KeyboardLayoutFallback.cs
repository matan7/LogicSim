using System.Text.RegularExpressions;
using UnityEngine.InputSystem;

namespace Core.Input
{
    /// <summary>
    /// Letter shortcuts bind the character on the key (<c>&lt;Keyboard&gt;/#(A)</c>), so they follow the layout: Ctrl+A
    /// selects all where the A is on AZERTY, Ctrl+Z undoes where the Z is on QWERTZ. Layouts without Latin letters
    /// (Cyrillic, Greek, ...) have no key for those characters; there the shortcuts fall back to the key positions of a
    /// US keyboard, as most applications do. Bindings the user has overridden are left alone.
    /// </summary>
    public static class KeyboardLayoutFallback
    {
        private static readonly Regex CharacterPath = new Regex(@"^<Keyboard>/#\((\w)\)$");

        /// <summary>Points character bindings without a key on the current layout to their US key, and back.</summary>
        /// <returns>The number of bindings that use their fallback.</returns>
        public static int Apply(InputActionAsset asset, Keyboard keyboard)
        {
            if (asset == null || keyboard == null)
                return 0;
            int fallbacks = 0;
            foreach (var action in asset)
            {
                var bindings = action.bindings;
                for (int i = 0; i < bindings.Count; i++)
                {
                    var match = CharacterPath.Match(bindings[i].path ?? string.Empty);
                    if (!match.Success)
                        continue;
                    string character = match.Groups[1].Value;
                    string fallback = "<Keyboard>/" + character.ToLowerInvariant();
                    string current = bindings[i].overridePath;
                    bool onLayout = keyboard.FindKeyOnCurrentKeyboardLayout(character) != null;
                    if (!onLayout)
                    {
                        if (string.IsNullOrEmpty(current))
                            action.ApplyBindingOverride(i, fallback);
                        if (string.IsNullOrEmpty(current) || current == fallback)
                            fallbacks++;
                    }
                    else if (current == fallback)
                    {
                        action.RemoveBindingOverride(i);
                    }
                }
            }
            return fallbacks;
        }
    }
}
