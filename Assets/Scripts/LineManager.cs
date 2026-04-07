using UnityEngine;
using UnityEngine.EventSystems;

public class LineManager : MonoBehaviour
{
    public static LineManager Instance { get; private set; }

    [SerializeField] private string _outputTag;
    [SerializeField] private LineConnector _linePrefab;
    [SerializeField] private GameObject _linePoint;

    private RaycastHit2D _hit;
    private Camera _camera;
    private bool _isMoving;

    private LineConnector _currentDrawingLine;
    private Transform _currentMovingPoint;

    private void Awake()
    {
        if (Instance != null)
        {
            Debug.LogWarning($"Singleton LineManager already exist: {LineManager.Instance.gameObject.name}");
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }
    private void Start()
    {
        _camera = Camera.main;
    }
    //void Update()
    //{
    //    if (Input.GetMouseButtonDown(0))
    //    {
    //        if (EventSystem.current.IsPointerOverGameObject())
    //        {
    //            return;
    //        }
    //        _hit = Physics2D.Raycast(_camera.ScreenToWorldPoint(Input.mousePosition), Vector2.zero);
    //        if (_hit.collider != null)
    //        {
    //            if (_hit.collider.CompareTag(_outputTag))
    //            {
    //                _isMoving = true;
    //                _currentDrawingLine = GameObject.Instantiate(_linePrefab, Vector3.zero, Quaternion.identity);
    //                _currentMovingPoint = GameObject.Instantiate(_linePoint, _camera.ScreenToWorldPoint(Input.mousePosition), Quaternion.identity).transform;
    //                _currentDrawingLine.Setup(_hit.collider.transform, _currentMovingPoint);
    //            }
    //            else
    //            {
    //                _isMoving = false;
    //            }
    //        }
    //        else
    //        {
    //            _isMoving = false;
    //        }
    //    }
    //    if (_isMoving && Input.GetMouseButton(0))
    //    {
    //        if (EventSystem.current.IsPointerOverGameObject())
    //        {
    //            return;
    //        }
    //        Vector3 mousePos = _camera.ScreenToWorldPoint(Input.mousePosition);
    //        _currentMovingPoint.position = new Vector3(mousePos.x, mousePos.y);
    //        _currentDrawingLine.DrawLine();

    //    }
    //    if (_isMoving && Input.GetMouseButtonUp(0))
    //    {
    //        _currentMovingPoint.position = new Vector3(Mathf.Round(_currentMovingPoint.position.x), Mathf.Round(_currentMovingPoint.position.y), Mathf.Round(_currentMovingPoint.position.z));
    //        _currentDrawingLine.DrawLine();
    //        _isMoving = false;
    //    }
    //}
}
