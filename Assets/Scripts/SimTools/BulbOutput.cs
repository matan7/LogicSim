using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BulbOutput : MonoBehaviour
{
    public GameObject Input;
    public Sprite isOn;
    public Sprite isOff;

    void Update()
    {
        if (MainManager.IsSimMode)
        {
            if (!Input.transform.gameObject.GetComponent<HasCurrent>().hasCurrent)
            {
                transform.gameObject.GetComponent<SpriteRenderer>().sprite = isOff;
            }
            else
            {
                transform.gameObject.GetComponent<SpriteRenderer>().sprite = isOn;
            }
        }
    }
}
