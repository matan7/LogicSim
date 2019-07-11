using UnityEngine;

public class LinePoint : MonoBehaviour
{
    public GameObject thisLine;
    void Start()
    {
        gameObject.GetComponent<SnapToGrid>().isMoving = true;
        
    }
   
    void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Input") && gameObject.GetComponent<SnapToGrid>().isMoving)
        {
            thisLine.GetComponent<CurrentTransmitter>().Input1 = collision.gameObject;
            this.transform.parent = collision.transform;
        }

    }
    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Input") && gameObject.GetComponent<SnapToGrid>().isMoving)
        {

            thisLine.GetComponent<CurrentTransmitter>().Input1.GetComponent<InputC>().outputList.Remove(thisLine.GetComponent<CurrentTransmitter>().Output1);
            thisLine.GetComponent<CurrentTransmitter>().Input1 = null;
            thisLine.GetComponent<CurrentTransmitter>().setOnListOnce = true;
            this.transform.parent = null;
            this.GetComponent<SpriteRenderer>().color = Color.white;
        }
    }
    private void OnMouseUp()
    {
        
        if (Mathf.Round(thisLine.GetComponent<SpriteRenderer>().size.x) == 0)
        {
            Destroy(thisLine.gameObject);
            Destroy(this.gameObject);
        }
    }
}
