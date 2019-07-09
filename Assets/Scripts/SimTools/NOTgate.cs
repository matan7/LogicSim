using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NOTgate : MonoBehaviour
{
    public GameObject Input1;
    public GameObject Output;

    void Update()
    {
        if (MainManager.IsSimMode)
        {
            if (Input1.transform.gameObject.GetComponent<HasCurrent>().hasCurrent)
            {
                Output.transform.gameObject.GetComponent<HasCurrent>().hasCurrent = false;
            }
            else
            {
                Output.transform.gameObject.GetComponent<HasCurrent>().hasCurrent = true;
            }
        }
    }
}
