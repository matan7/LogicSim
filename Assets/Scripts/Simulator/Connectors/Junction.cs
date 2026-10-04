using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Simulator
{
    public class Junction : MonoBehaviour, ISignalNode
    {
        List<Wire> _wires;

        public IEnumerable<InputConnector> TraverseInputs()
            => _wires.SelectMany(w => w.TraverseInputs());

        public IEnumerable<OutputConnector> TraverseOutputs()
            => _wires.SelectMany(w => w.TraverseOutputs());
    }
}

