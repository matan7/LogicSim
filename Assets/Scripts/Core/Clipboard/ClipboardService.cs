using LogicSim.Simulation;
using UnityEngine;

namespace Core.Clipboard
{
    /// <summary>Holds copied circuit fragments; also mirrors them as JSON in the system clipboard.</summary>
    public class ClipboardService
    {
        public CircuitFragment Fragment { get; private set; }

        public bool HasFragment => Fragment != null && !Fragment.IsEmpty;

        public void SetFragment(CircuitFragment fragment, SubcircuitLibrary library)
        {
            Fragment = fragment;
            if (fragment != null && !fragment.IsEmpty)
                SetStringClipboard(CircuitSerializer.SerializeFragment(fragment, library));
        }

        /// <summary>Fragment copied in another LogicSim window (or by an MCP client), if the system clipboard holds one.</summary>
        public CircuitFragment ReadSystemFragment(SubcircuitLibrary library)
        {
            var text = GUIUtility.systemCopyBuffer;
            if (!CircuitSerializer.LooksLikeFragment(text))
                return null;
            try
            {
                return CircuitSerializer.DeserializeFragment(text, library);
            }
            catch (CircuitException)
            {
                return null;
            }
        }

        public void SetStringClipboard(string data)
        {
            GUIUtility.systemCopyBuffer = data;
        }
    }
}
