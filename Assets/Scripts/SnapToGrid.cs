using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class SnapToGrid : MonoBehaviour
{
    public bool isMoving { get; set; }

    private RaycastHit2D hit;
    private Vector2 pivotDistance;

   
    private void Start()
    {
        transform.position = new Vector3(Mathf.Round(transform.position.x), Mathf.Round(transform.position.y));
        
        pivotDistance = new Vector2(0, 0);
    }
    private void Update()
    {
        if (!MainManager.IsSimMode)
        {
            if (Input.GetMouseButtonDown(0))
            {
                if (EventSystem.current.IsPointerOverGameObject())
                {
                    return;
                }
                hit = Physics2D.Raycast(Camera.main.ScreenToWorldPoint(Input.mousePosition), Vector2.zero);
                if (hit.collider != null && hit.transform.gameObject == this.gameObject)
                {
                    isMoving = true;
                    pivotDistance.x = Camera.main.ScreenToWorldPoint(Input.mousePosition).x - transform.position.x;
                    pivotDistance.y = Camera.main.ScreenToWorldPoint(Input.mousePosition).y - transform.position.y;
                }

            }
            if (isMoving && Input.GetMouseButton(0))
            {
                if (EventSystem.current.IsPointerOverGameObject())
                {
                    return;
                }
                Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                transform.position = new Vector2(mousePos.x - pivotDistance.x, mousePos.y - pivotDistance.y);

            }
            if (Input.GetMouseButtonUp(0))
            {
                transform.position = new Vector2(Mathf.Round(transform.position.x), Mathf.Round(transform.position.y));
                isMoving = false;
            }
        }
    }
}
