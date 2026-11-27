using System;
using System.Collections.Generic;
using System.Linq;

namespace LogicSim.Simulation
{
    /// <summary>
    /// Integer settings of a component that have no field of their own (for example the size of a screen). Immutable,
    /// compared by value, so that properties and undo snapshots stay plain values.
    /// </summary>
    public sealed class SettingValues : IEquatable<SettingValues>
    {
        public static readonly SettingValues Empty = new SettingValues(Array.Empty<KeyValuePair<string, int>>());

        private readonly KeyValuePair<string, int>[] _items;    // sorted by key

        private SettingValues(KeyValuePair<string, int>[] items)
        {
            _items = items;
        }

        public IEnumerable<KeyValuePair<string, int>> Items => _items;

        public int Get(string key, int fallback)
        {
            foreach (var item in _items)
                if (item.Key == key)
                    return item.Value;
            return fallback;
        }

        public SettingValues With(string key, int value)
        {
            if (Get(key, value + 1) == value)
                return this;
            var items = _items.Where(i => i.Key != key).Append(new KeyValuePair<string, int>(key, value))
                .OrderBy(i => i.Key, StringComparer.Ordinal).ToArray();
            return new SettingValues(items);
        }

        public SettingValues Without(string key)
        {
            if (_items.All(i => i.Key != key))
                return this;
            return new SettingValues(_items.Where(i => i.Key != key).ToArray());
        }

        public bool Equals(SettingValues other)
        {
            if (other is null || other._items.Length != _items.Length)
                return false;
            for (int i = 0; i < _items.Length; i++)
                if (_items[i].Key != other._items[i].Key || _items[i].Value != other._items[i].Value)
                    return false;
            return true;
        }

        public override bool Equals(object obj) => obj is SettingValues other && Equals(other);

        public override int GetHashCode()
        {
            int hash = 17;
            foreach (var item in _items)
                hash = hash * 31 + item.Key.GetHashCode() * 7 + item.Value;
            return hash;
        }

        public override string ToString() => string.Join(", ", _items.Select(i => $"{i.Key}={i.Value}"));
    }

    /// <summary>
    /// A scalar setting of a component kind: an integer in a range, or a 0/1 flag. The inspector, the MCP tools
    /// (describe, add, set, list_component_types, tool schemas) and the file format all handle it from this description,
    /// so a new setting needs no code in those places.
    /// </summary>
    public sealed class ComponentSetting
    {
        public delegate void Setter(ref ComponentProperties properties, int value);

        /// <summary>MCP argument and description key (snake_case).</summary>
        public string Name { get; }

        /// <summary>Key in the file format (camelCase).</summary>
        public string JsonKey { get; }

        /// <summary>Label in the inspector.</summary>
        public string Label { get; }

        /// <summary>Meaning and unit, for agents and the inspector's placeholder.</summary>
        public string Help { get; }

        public bool IsFlag { get; }
        public int Min { get; }
        public int Max { get; }
        public int Default { get; }

        /// <summary>Changing it adds or removes pins (wires on removed pins must be disconnected first).</summary>
        public bool ChangesPins { get; private set; }

        /// <summary>Shown in the inspector (a switch's state is toggled on the canvas instead).</summary>
        public bool InInspector { get; private set; } = true;

        /// <summary>Written in component descriptions (an input count shows as the pin list instead).</summary>
        public bool InDescription { get; private set; } = true;

        private readonly Func<ComponentProperties, int> _get;
        private readonly Setter _set;

        private ComponentSetting(string name, string jsonKey, string label, string help, bool isFlag, int min, int max, int @default,
            Func<ComponentProperties, int> get, Setter set)
        {
            Name = name;
            JsonKey = jsonKey;
            Label = label;
            Help = help;
            IsFlag = isFlag;
            Min = min;
            Max = max;
            Default = @default;
            _get = get;
            _set = set;
        }

        public static ComponentSetting Integer(string name, string jsonKey, string label, string help, int min, int max, int @default,
            Func<ComponentProperties, int> get, Setter set)
            => new ComponentSetting(name, jsonKey, label, help, false, min, max, @default, get, set);

        public static ComponentSetting Flag(string name, string jsonKey, string label, string help, Func<ComponentProperties, int> get, Setter set)
            => new ComponentSetting(name, jsonKey, label, help, true, 0, 1, 0, get, set);

        /// <summary>An integer kept in <see cref="ComponentProperties.Settings"/> under <paramref name="name"/>.</summary>
        public static ComponentSetting Stored(string name, string label, string help, int min, int max, int @default)
            => new ComponentSetting(name, CamelCase(name), label, help, false, min, max, @default,
                p => (p.Settings ?? SettingValues.Empty).Get(name, @default),
                // A default value is not stored, so that "set to the default" and "never set" compare equal.
                (ref ComponentProperties p, int value) => p.Settings = value == @default
                    ? (p.Settings ?? SettingValues.Empty).Without(name)
                    : (p.Settings ?? SettingValues.Empty).With(name, value));

        public ComponentSetting AddsPins()
        {
            ChangesPins = true;
            return this;
        }

        public ComponentSetting NotInInspector()
        {
            InInspector = false;
            return this;
        }

        public ComponentSetting NotDescribed()
        {
            InDescription = false;
            return this;
        }

        public int Get(ComponentProperties properties) => _get(properties);

        public int Get(CircuitComponent component) => _get(component.GetProperties());

        /// <summary>Sets the value after checking its range.</summary>
        public void Set(ref ComponentProperties properties, int value)
        {
            if (value < Min || value > Max)
                throw new CircuitException(IsFlag ? $"{Name} is 0 or 1" : $"{Name} must be {Min}..{Max}");
            _set(ref properties, value);
        }

        /// <summary>For files: out-of-range values are clamped instead of rejected.</summary>
        public void SetClamped(ref ComponentProperties properties, int value) => _set(ref properties, Math.Min(Max, Math.Max(Min, value)));

        /// <summary>"half_period (1-1000000000, default 300): ticks per half period" for agents.</summary>
        public string Describe()
        {
            string range = IsFlag ? "0/1" : $"{Min}-{Max}, default {Default}";
            return string.IsNullOrEmpty(Help) ? $"{Name} ({range})" : $"{Name} ({range}): {Help}";
        }

        private static string CamelCase(string snake)
        {
            var parts = snake.Split('_');
            return parts[0] + string.Concat(parts.Skip(1).Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p.Substring(1)));
        }
    }
}
