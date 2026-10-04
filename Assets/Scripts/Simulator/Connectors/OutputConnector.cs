using System.Collections.Generic;
using UnityEngine;

namespace Simulator
{
    public class OutputConnector : MonoBehaviour, ISignalNode
    {
        public IEnumerable<InputConnector> TraverseInputs() => null;

        public IEnumerable<OutputConnector> TraverseOutputs() => new[] { this };
    }
}