using System.Collections.Generic;
using UnityEngine;

namespace Simulator
{
    public class Wire : MonoBehaviour, ISignalNode
    {
        private ISignalNode _start;
        private ISignalNode _end;

        public IEnumerable<OutputConnector> TraverseOutputs() => _start.TraverseOutputs();
        public IEnumerable<InputConnector> TraverseInputs() => _end.TraverseInputs();
    }
}