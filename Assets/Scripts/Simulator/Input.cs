using Assets.Scripts;
using System.Collections.Generic;
using UnityEngine;

public class Input : MonoBehaviour, IConnector
{
    public List<LineConnector> ConnectedLines { get; private set; }

    public Transform Transform()
    {
        return transform;
    }
}
