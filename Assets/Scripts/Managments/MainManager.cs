using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.EventSystems;

public class MainManager : MonoBehaviour
{
   
    public static bool IsSimMode { get; set; }  //false = editor mode (paused), true = simulation mode
    public Toggle startSimulation;
    public GameObject originPoint;
    public GameObject selectionObject;
    public GameObject selectionPrefab;
    public GameObject selectionHolder;
    public List<GameObject> selectionList = new List<GameObject>();
    public List<GameObject> copyMemory = new List<GameObject>();
    public Vector3 mousePoint;

    private RaycastHit2D hit;
    private Vector3 copyPositionSum = new Vector3();
    private Vector3 selectionPositionSum = new Vector3();
    private void Start()
    {
        
        IsSimMode = false;
    }
    private void Update()
    {
        SelectItems();
        MouseLocation();
        SelectionManagment();
    }
    public void SetSimMode (bool isOn)
    {
        IsSimMode = isOn;
        CancelSelection();
    }
    void CancelSelection()
    {
        if (selectionHolder.transform.childCount > 0)
        {
            foreach (Transform child in selectionHolder.transform)
            {
                child.GetComponent<SpriteRenderer>().color = Color.white;
                if (child.transform.Find("Output/Line") != null)
                {
                    child.transform.Find("Output/Line").GetComponent<SpriteRenderer>().color = Color.white;
                }
                if (child.transform.Find("Input/LineConnector") != null)
                {
                    child.transform.Find("Input/LineConnector").GetComponent<SpriteRenderer>().color = Color.white;
                }
            }
            foreach (BoxCollider2D box in selectionHolder.transform.GetComponents<BoxCollider2D>())
            {
                Destroy(box);
            }
            selectionHolder.transform.DetachChildren();
        }
    }
    private void SelectItems()
    {
        
        if (Input.GetMouseButtonDown(0) && !IsSimMode)
        {
            if (EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }
            hit = Physics2D.Raycast(Camera.main.ScreenToWorldPoint(Input.mousePosition), Vector2.zero);
            if (hit.collider != null && (hit.transform.tag == "Output" || hit.transform.tag == "LineConnector" || hit.transform.tag == "Selection"))
            {
                return;
            }
            else if(hit.collider != null && hit.transform.tag == "Item")
            {
                CancelSelection();
                selectionList.Add(hit.transform.gameObject);
            }
            else
            {
                Instantiate(selectionPrefab, Camera.main.ScreenToWorldPoint(Input.mousePosition), Quaternion.identity);
                CancelSelection();
            }
            
        }

        if (Input.GetMouseButtonUp(0) && selectionList.Count > 0)
        {
            selectionPositionSum = Vector3.zero;
            foreach (GameObject item in selectionList)
            {               
                selectionPositionSum += item.transform.position;
            }
            selectionPositionSum /= selectionList.Count;
            selectionHolder.transform.position = selectionPositionSum ; //Centrira selection holder u sredinu odabranih objekata
            selectionHolder.transform.position += new Vector3(0f, 0f, -1f);

            foreach (GameObject item in selectionList)
            {
                item.GetComponent<SpriteRenderer>().color = new Color(0.35f, 0.65f, 1f, 1f); //Plava
                if(item.transform.Find("Output/Line") != null)
                {
                    item.transform.Find("Output/Line").GetComponent<SpriteRenderer>().color = new Color(0.35f, 0.65f, 1f, 1f); //Plava
                }
                if(item.transform.Find("Input/LineConnector") != null)
                {
                    item.transform.Find("Input/LineConnector").GetComponent<SpriteRenderer>().color = new Color(0.35f, 0.65f, 1f, 1f); //Plava
                }
                item.transform.SetParent(selectionHolder.transform);
            }
            foreach (Transform child in selectionHolder.transform)
            {
                BoxCollider2D box = selectionHolder.gameObject.AddComponent<BoxCollider2D>();
                box.size = new Vector2(5f, 5f);
                box.offset = child.localPosition + new Vector3(2.5f, -2.5f);
            }
            selectionList.Clear();
        }
    }
    private void MouseLocation()
    {
        if (Input.GetMouseButtonUp(0))
        {
            if (EventSystem.current.IsPointerOverGameObject())
                return;
            mousePoint = Camera.main.ScreenToWorldPoint(Input.mousePosition);     
        }
        if (Input.GetMouseButtonUp(1))
        {
            if (EventSystem.current.IsPointerOverGameObject())
                return;
            mousePoint = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            originPoint.transform.position = new Vector2(Mathf.Round(mousePoint.x), Mathf.Round(mousePoint.y));
        }
    }
    // Functions when items are selected
    private void SelectionManagment()
    {
        if(selectionHolder.transform.childCount > 0)
        {
            
            // Deletes selected items
            if (Input.GetKeyDown(KeyCode.Delete))
            {
                foreach(Transform child in selectionHolder.transform)
                {
                    GameObject.Destroy(child.gameObject);
                }
            }
            // Create list of object ready to copy
            if((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.C))
            {
                copyMemory.Clear();
                copyPositionSum = Vector3.zero;
                foreach (Transform child in selectionHolder.transform)
                {
                    copyMemory.Add(child.gameObject);
                    copyPositionSum += child.transform.localPosition;
                }
                copyPositionSum /= copyMemory.Count;
            }         
        }
        // Paste copies of item
        if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.V))
        {
            foreach (GameObject item in copyMemory)
            {
                try
                {
                    GameObject clone = GameObject.Instantiate(item);
                    clone.name = item.name;
                    clone.transform.position +=  mousePoint - copyPositionSum;
                    clone.transform.position = new Vector3(clone.transform.position.x, clone.transform.position.y, 1);
                    clone.GetComponent<SpriteRenderer>().color = Color.white;
                    if (clone.transform.Find("Output/Line") != null)
                    {
                        clone.transform.Find("Output/Line").GetComponent<SpriteRenderer>().color = Color.white;
                    }
                    if (clone.transform.Find("Input/LineConnector") != null)
                    {
                        clone.transform.Find("Input/LineConnector").GetComponent<SpriteRenderer>().color = Color.white;
                    }
                }
                catch
                {
                }
            }
        }
    }
}
