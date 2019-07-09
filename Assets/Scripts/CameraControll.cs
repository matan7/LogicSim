using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraControll : MonoBehaviour
{
    public Camera MainCamera;
    public SpriteRenderer SpriteRend;
    public Transform TiledBgTransform;
    private float defaultZoom;

    private Vector3 currentPosition;
    private Vector3 deltaPosition;
    private Vector3 lastPosition;

    private Vector2 WorldUnitsInCamera;
    private Vector2 WorldToPixelAmount;

    void Start()
    {
        defaultZoom = MainCamera.orthographicSize;
        PixelToWorldPoint();
        TiledBgTransform.position = new Vector3(Mathf.Round(this.transform.position.x), Mathf.Round(this.transform.position.y), 5);
    }

    void Update()
    {
        Zoom();
        Move();
    }
    private void Zoom()
    {
        if(Input.mouseScrollDelta.y != 0)
        {
            PixelToWorldPoint();
            MainCamera.orthographicSize += (Input.mouseScrollDelta.y * -1);
            if (MainCamera.orthographicSize < 10)
            {
                MainCamera.orthographicSize = 10;
            }
            if (MainCamera.orthographicSize > 75)
            {
                MainCamera.orthographicSize = 75;
            }
            float sizeX = Mathf.Round(Screen.width / WorldToPixelAmount.x);
            float sizeY = Mathf.Round(Screen.height / WorldToPixelAmount.y);
            if (sizeX % 2 != 0)
            {
                sizeX++;
            }
            if (sizeY % 2 != 0)
            {
                sizeY++;
            }
            SpriteRend.size = new Vector3(sizeX + 10, sizeY + 10);
        }
    }
    private void Move()
    {
        currentPosition = new Vector3(Input.mousePosition.x / WorldToPixelAmount.x * -1, Input.mousePosition.y / WorldToPixelAmount.y * -1);
        deltaPosition = currentPosition - lastPosition;
        lastPosition = currentPosition;
        if (Input.GetMouseButton(0) && Input.GetKey(KeyCode.LeftAlt) || Input.GetMouseButton(2))
        {
            transform.position += deltaPosition;
            TiledBgTransform.position = new Vector3(Mathf.Round(this.transform.position.x), Mathf.Round(this.transform.position.y), 5);
        }
    }   
    private void PixelToWorldPoint()
    {
        WorldUnitsInCamera.y = MainCamera.GetComponent<Camera>().orthographicSize * 2;
        WorldUnitsInCamera.x = WorldUnitsInCamera.y * Screen.width / Screen.height;
        WorldToPixelAmount.x = Screen.width / WorldUnitsInCamera.x;
        WorldToPixelAmount.y = Screen.height / WorldUnitsInCamera.y;
    }
}
