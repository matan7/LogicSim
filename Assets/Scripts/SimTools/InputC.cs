using System.Collections.Generic;
using UnityEngine;

public class InputC : MonoBehaviour
{   
    public List<GameObject> outputList;                  //List of all output connected to this input

    private void Update()
    {
        if (MainManager.IsSimMode)                       //Do things in simulator mode
        {
            foreach(GameObject output in outputList)     //Checks if output list has any otuput with current and breaks loop when first has current
            {
                if (output.gameObject.GetComponent<HasCurrent>().hasCurrent)
                {
                    this.gameObject.GetComponent<HasCurrent>().hasCurrent = true;
                    break;
                }
                else
                {
                    this.gameObject.GetComponent<HasCurrent>().hasCurrent = false;
                }
            }
            if(outputList.Count == 0)
            {
                this.gameObject.GetComponent<HasCurrent>().hasCurrent = false;
            }
        }
    }
}
