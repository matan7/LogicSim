using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DragAndDrop : MonoBehaviour, IDragHandler, IEndDragHandler 
{

    void IDragHandler.OnDrag(PointerEventData eventData)
    {
        elementSize.sizeDelta = new Vector2(1/mainCamera.orthographicSize * 2900, 1 / mainCamera.orthographicSize * 2170);
        transform.position = Input.mousePosition;
        gameObject.GetComponent<Image>().color = new Color(0f, 1f, 0.15f, 0.75f);
    }

    void IEndDragHandler.OnEndDrag(PointerEventData eventData)
    {
        Instantiate(createItem, new Vector3(Camera.main.ScreenToWorldPoint(transform.position).x - 2.5f , Camera.main.ScreenToWorldPoint(transform.position).y + 2f), Quaternion.identity);
        elementSize.sizeDelta = new Vector2(70, 50);
        transform.localPosition = startPosition;
        gameObject.GetComponent<Image>().color = new Color(1, 1, 1, 1);  
    }
    private RectTransform elementSize;
    private Vector3 startPosition;
    public GameObject createItem;
    public Camera mainCamera;

    void Start()
    {
        elementSize = gameObject.GetComponent<RectTransform>();
        startPosition = transform.localPosition;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
