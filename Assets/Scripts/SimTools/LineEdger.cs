using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LineEdger : MonoBehaviour
{
    public SpriteRenderer spriteDimensions;
    public GameObject pointStart;
    public GameObject pointEnd;
    

    void Update()
    {
        pointStart.transform.localPosition = new Vector3(0,0,0);
        pointEnd.transform.localPosition = new Vector3(spriteDimensions.size.x * -1, 0, 0);
    }
}
