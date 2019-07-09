using UnityEngine;

public class ConnectorLine : MonoBehaviour
{
    public GameObject point;

    public GameObject StartPoint { get; set; }
    public GameObject EndPoint { get; set; }
    private SnapToGrid snapStartPoint;
    private SnapToGrid snapEndPoint;



    void Start()
    {
        StartPoint = gameObject.transform.parent.gameObject;
        snapStartPoint =  StartPoint.GetComponentInParent<SnapToGrid>() ?? StartPoint.GetComponent<SnapToGrid>();

        Instantiate(point, this.gameObject.transform);

        EndPoint = this.transform.GetChild(0).gameObject;
        EndPoint.GetComponent<LinePoint>().thisLine = this.gameObject;
        EndPoint.transform.parent = null;

        snapEndPoint = EndPoint.GetComponent<SnapToGrid>();

        LineCreator(StartPoint.transform.position, EndPoint.transform.position);

    }

    void Update()
    {
        if (!MainManager.IsSimMode)
        {
            DragObject(StartPoint, EndPoint, snapStartPoint, snapEndPoint);
        }
    }

    void DragObject(GameObject startObject, GameObject endObject, SnapToGrid snapOutCheck, SnapToGrid snapInCheck)
    {
        if (startObject != null && endObject != null)
        {
            bool doOnce = false;
            if (snapInCheck.isMoving || snapOutCheck.isMoving)
            {
                LineCreator(startObject.transform.position, endObject.transform.position);
                doOnce = true;
            }
            if (doOnce && !snapInCheck.isMoving || !snapOutCheck.isMoving)
            {
                LineCreator(startObject.transform.position, endObject.transform.position);
                doOnce = false;
            }
        }
    }

    void LineCreator(Vector3 startLine, Vector3 endLine)
    { 
        float aSide;
        float bSide = startLine.y - endLine.y;
        if (endLine.x >= startLine.x)
        {
            aSide = endLine.x - startLine.x;
        }
        else
        {
            aSide = startLine.x - endLine.x;
        }
        if (endLine.y >= startLine.y)
        {
            bSide = endLine.y - startLine.y;
        }
        else
        {
            bSide = startLine.y - endLine.y;
        }
        float cSide = Mathf.Sqrt(Mathf.Pow(aSide,2) +Mathf.Pow(bSide,2));
        float cAngle = Mathf.Atan(aSide / bSide) * Mathf.Rad2Deg;
        if (endLine.x >= startLine.x && endLine.y >= startLine.y)
        {
            cAngle = Mathf.Atan(aSide / bSide) * Mathf.Rad2Deg * -1 - 90; 
        }
        else if (endLine.x < startLine.x && endLine.y >= startLine.y)
        {
            cAngle = Mathf.Atan(aSide / bSide) * Mathf.Rad2Deg - 90 ; //ok
        }
        else if (endLine.x >= startLine.x && endLine.y < startLine.y)
        {
            cAngle = Mathf.Atan(aSide / bSide) * Mathf.Rad2Deg + 90 ; //ok
        }
        else
        {
            cAngle = Mathf.Atan(aSide / bSide) * Mathf.Rad2Deg * -1 + 90 ;
        }
        if (float.IsNaN(cAngle))
        {
            cAngle = 0f;
        }
        gameObject.transform.GetComponent<SpriteRenderer>().size = new Vector2(cSide, 0.275f);
        gameObject.transform.eulerAngles = new Vector3(0, 0, cAngle);
    }
}
