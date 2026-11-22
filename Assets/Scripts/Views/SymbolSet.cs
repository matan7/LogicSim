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
            /// <summary>
            /// Per row r, r units below the top edge (0 to Size.y): distance from the left edge to where the outline
            /// starts, in units, or -1 where the row misses the outline. Input pin stubs end here.
            /// </summary>
            public float[] Left;
            /// <summary>Per row: distance from the left edge to where the outline ends, or -1. Output pin stubs start here.</summary>
            public float[] Right;
        }

        public Texture2D Atlas;
        /// <summary>The shader that draws the symbols (referenced here so that builds include it).</summary>
        public Shader Shader;
        /// <summary>Atlas texels per world unit.</summary>
        public float TexelsPerUnit = 32;
        /// <summary>Distance range of the fields on each side of an edge, in world units: a channel value v means
        /// a distance of (v - 0.5) * 2 * Spread inside the shape.</summary>
        public float Spread = 0.25f;
        /// <summary>Margin around every symbol inside its atlas rectangle, in world units.</summary>
        public float Padding;
        public List<Symbol> Symbols = new List<Symbol>();

        private Dictionary<string, int> _byId;

        /// <summary>Index of the symbol in <see cref="Symbols"/>, or -1.</summary>
        public int IndexOf(string id)
        {
            if (_byId == null || _byId.Count != Symbols.Count)
            {
                _byId = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < Symbols.Count; i++)
                    _byId[Symbols[i].Id] = i;
            }
            return id != null && _byId.TryGetValue(id, out int index) ? index : -1;
        }

        public bool TryGet(string id, out Symbol symbol)
        {
            int index = IndexOf(id);
            symbol = index >= 0 ? Symbols[index] : default;
            return index >= 0;
        }

        private void OnValidate() => _byId = null;
    }
}
