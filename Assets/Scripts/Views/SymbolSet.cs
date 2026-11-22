using System;
using System.Collections.Generic;
using UnityEngine;

namespace LogicSim.Views
{
    /// <summary>
    /// The component symbol atlas: signed distance fields of the outline (R), fill (G) and glow (B) of each symbol, built
    /// by Tools > LogicSim > Build Symbol Atlas from Art/Symbols/Symbols.psd. See docs/SYMBOL_FORMAT.md.
    /// </summary>
    [CreateAssetMenu(menuName = "LogicSim/Symbol Set", fileName = "SymbolSet")]
    public sealed class SymbolSet : ScriptableObject
    {
        [Serializable]
        public struct Symbol
        {
            public string Id;
            /// <summary>Atlas rectangle of the symbol including its padding, in normalized texture coordinates.</summary>
            public Rect Uv;
            /// <summary>Size of the symbol (its artboard) in world units.</summary>
            public Vector2 Size;
        }

        public Texture2D Atlas;
        /// <summary>Atlas texels per world unit.</summary>
        public float TexelsPerUnit = 32;
        /// <summary>Distance range of the fields on each side of an edge, in world units: a channel value v means
        /// a distance of (v - 0.5) * 2 * Spread inside the shape.</summary>
        public float Spread = 0.25f;
        /// <summary>Margin around every symbol inside its atlas rectangle, in world units.</summary>
        public float Padding;
        public List<Symbol> Symbols = new List<Symbol>();

        private Dictionary<string, Symbol> _byId;

        public bool TryGet(string id, out Symbol symbol)
        {
            if (_byId == null || _byId.Count != Symbols.Count)
            {
                _byId = new Dictionary<string, Symbol>(StringComparer.Ordinal);
                foreach (var s in Symbols)
                    _byId[s.Id] = s;
            }
            return _byId.TryGetValue(id, out symbol);
        }

        private void OnValidate() => _byId = null;
    }
}
