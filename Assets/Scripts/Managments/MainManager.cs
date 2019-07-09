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
            if (hit.collider != null && (hit.transform.tag == "Item" || hit.transform.tag == "Output" || hit.transform.tag == "LineConnector" || hit.transform.tag == "Selection"))
            {
                return;
            }
            else
            {
                Instantiate(selectionPrefab, Camera.main.ScreenToWorldPoint(Input.mousePosition), Quaternion.identity);
                if(selectionHolder.transform.childCount > 0)
                {
                    selectionHolder.transform.DetachChildren();
                }
            }
            
        }

        if (Input.GetMouseButtonUp(0) && selectionList.Count > 0)
        {
            foreach(GameObject item in selectionList)
            {
                item.transform.SetParent(selectionHolder.transform);
            }
        }
    }
    private void MouseLocation()
    {
        if (Input.GetMouseButtonUp(0))
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
                    copyPositionSum += child.transform.position;
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
                }
                catch
                {
                }
            }
        }
    }
}
