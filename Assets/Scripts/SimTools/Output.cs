using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class Output : MonoBehaviour
{

    public GameObject connector;

    void OnMouseDown()
    {
        if (EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }
        if (!MainManager.IsSimMode)
        {
            Instantiate(connector, gameObject.transform);
        }
    }
}