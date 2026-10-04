using System.Collections.Generic;

namespace Simulator
{
    public interface ISignalNode
    {
        IEnumerable<OutputConnector> TraverseOutputs();
        IEnumerable<InputConnector> TraverseInputs();
    }
}