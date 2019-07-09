﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BUFFERgate : MonoBehaviour
{
    public GameObject Input1;
    public GameObject Output;

    void Update()
    {
        if (MainManager.IsSimMode)
        {
            if (Input1.transform.gameObject.GetComponent<HasCurrent>().hasCurrent)
            {
                Output.transform.gameObject.GetComponent<HasCurrent>().hasCurrent = true;
            }
            else
            {
                Output.transform.gameObject.GetComponent<HasCurrent>().hasCurrent = false;
            }
        }
    }
}
