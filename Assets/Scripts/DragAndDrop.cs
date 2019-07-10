using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DragAndDrop : MonoBehaviour, IDragHandler, IEndDragHandler 
{


    private RectTransform elementSize;
    private Vector3 startPosition;
    public GameObject createItem;
    public Camera mainCamera;

    void Start()
    {
        elementSize = gameObject.GetComponent<RectTransform>();
        startPosition = transform.localPosition;
    }
    void IDragHandler.OnDrag(PointerEventData eventData)
    {
        elementSize.sizeDelta = new Vector2(1 / mainCamera.orthographicSize * 2900, 1 / mainCamera.orthographicSize * 2170);
        transform.position = Input.mousePosition;
        gameObject.GetComponent<Image>().color = new Color(0f, 1f, 0.15f, 0.75f);
    }

    void IEndDragHandler.OnEndDrag(PointerEventData eventData)
    {
        GameObject item = Instantiate(createItem, new Vector3(Camera.main.ScreenToWorldPoint(transform.position).x - 2.5f, Camera.main.ScreenToWorldPoint(transform.position).y + 2f), Quaternion.identity);
        item.name = createItem.name;
        item.transform.position += new Vector3(0, 0, 1f);
        elementSize.sizeDelta = new Vector2(70, 50);
        transform.localPosition = startPosition;
        gameObject.GetComponent<Image>().color = Color.white;
    }
}
