using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;

public class Selection : MonoBehaviour
{
    private float mouseX, mouseY;
    private SpriteRenderer spriteRenderer;
    public BoxCollider2D collider;

    // Start is called before the first frame update
    void Start()
    {
        mouseX = Camera.main.ScreenToWorldPoint(Input.mousePosition).x;
        mouseY = Camera.main.ScreenToWorldPoint(Input.mousePosition).y;
        transform.position = new Vector2(mouseX, mouseY);
        spriteRenderer = gameObject.GetComponent<SpriteRenderer>();

        GameObject.FindGameObjectWithTag("MainManager").GetComponent<MainManager>().selectionObject = this.gameObject;
        GameObject.FindGameObjectWithTag("MainManager").GetComponent<MainManager>().selectionList.Clear();
    }

    void Update()
    {
        if (!MainManager.IsSimMode)
        {
            if (Input.GetMouseButton(0))
            {
                spriteRenderer.size = new Vector2(Camera.main.ScreenToWorldPoint(Input.mousePosition).x - mouseX, Camera.main.ScreenToWorldPoint(Input.mousePosition).y - mouseY);
                collider.size = new Vector2(Math.Abs(spriteRenderer.size.x), Math.Abs(spriteRenderer.size.y));
                if (spriteRenderer.size.x < 0 && spriteRenderer.size.y < 0)
                {
                    collider.offset = collider.size / -2;
                }
                else if(spriteRenderer.size.x > 0 && spriteRenderer.size.y < 0)
                {
                    collider.offset = new Vector2 (collider.size.x / 2, collider.size.y /-2);
                }
                else if (spriteRenderer.size.x < 0 && spriteRenderer.size.y > 0)
                {
                    collider.offset = new Vector2(collider.size.x / -2, collider.size.y / 2);
                }
                else
                {
                    collider.offset = collider.size / 2;
                }
                
            }

            if (Input.GetMouseButtonUp(0))
            {
                GameObject.FindGameObjectWithTag("MainManager").GetComponent<MainManager>().selectionObject = null;
                Destroy(this.gameObject);
            }
        }

    }
    void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Item") || collision.gameObject.CompareTag("LineConnector"))
        {
            
            if(collision.transform.parent == null)
            {
                GameObject.FindGameObjectWithTag("MainManager").GetComponent<MainManager>().selectionList.Add(collision.gameObject);
            }
        }

    }
    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Item") || collision.gameObject.CompareTag("LineConnector"))
        {
            GameObject.FindGameObjectWithTag("MainManager").GetComponent<MainManager>().selectionList.Remove(collision.gameObject);

        }
    }
}
