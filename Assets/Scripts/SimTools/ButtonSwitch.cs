using UnityEngine;
using UnityEngine.UI;

public class ButtonSwitch : MonoBehaviour
{
    public GameObject Output;
    public Sprite isOn;
    public Sprite isOff;
    void OnMouseDown()
    {
        if (MainManager.IsSimMode)
        {
            if (Output.transform.gameObject.GetComponent<HasCurrent>().hasCurrent)
            {
                Output.transform.gameObject.GetComponent<HasCurrent>().hasCurrent = false;
                transform.gameObject.GetComponent<SpriteRenderer>().sprite = isOff;
            }
            else
            {
                Output.transform.gameObject.GetComponent<HasCurrent>().hasCurrent = true;
                transform.gameObject.GetComponent<SpriteRenderer>().sprite = isOn;
            }
        } 
    }
}
