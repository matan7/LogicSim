using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NANDgate : MonoBehaviour
{
    public GameObject Input1;
    public GameObject Input2;
    public GameObject Output;
    void Update()
    {
        if (MainManager.IsSimMode)
        {
            if (Input1.transform.gameObject.GetComponent<HasCurrent>().hasCurrent && Input2.transform.gameObject.GetComponent<HasCurrent>().hasCurrent)
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