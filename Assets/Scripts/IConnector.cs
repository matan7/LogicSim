using System;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts
{
    public interface IConnector 
    {
        public List<LineConnector> ConnectedLines { get; }

        public Transform Transform();

        public void ConnectLine(LineConnector line)
        {
            ConnectedLines.Add(line);
        }

        public void DisconnectLine(LineConnector line)
        {
            ConnectedLines.Remove(line);
        }
    }
}
