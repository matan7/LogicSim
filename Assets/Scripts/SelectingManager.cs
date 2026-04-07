using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;
using Core.Input;
using Core;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using System.Linq;

public class SelectingManager : MonoBehaviour
{
    public static SelectingManager Instance { get; private set; }   

    public List<ISelectable> SelectionList { get; private set; } = new List<ISelectable>();

    [SerializeField] private SpriteRenderer _selectionSpriteRenderer; 
    [SerializeField] private Transform _selectionHolder;

    private RaycastHit2D _hit;

    private Vector2 _mouseStartPosition;
    private Vector2 _mousePosition;
    private Vector2 _pendingPosition;

    private bool _isSelectPending = false;
    private bool _isSelectMorePending = false;
    private bool _isDrawingSelection = false;
    private bool _isDrawingSelectionPending = false;
    private bool _isDrawingSelectionEndPending = false;
    private bool _isDrawingSelectionMoreEndPending = false;

    private InputService _inputService;
    private Camera _camera;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        else
        {
            Instance = this;
        }
    }

    void Start()
    {
        _inputService = SystemCore.InputService;
        _camera = Camera.main;
        _selectionSpriteRenderer.enabled = false;

        // Mouse left hold and drag
        _inputService.CurrentInputActions.FindAction("SelectionDraw").performed += OnSelecting;
        _inputService.CurrentInputActions.FindAction("SelectionDraw").canceled += OnSelectingEnd;
        // Shift + mouse left hold and drag
        _inputService.CurrentInputActions.FindAction("SelectionDrawMore").canceled += OnSelectingMoreEnd;
        // Mouse left click
        _inputService.CurrentInputActions.FindAction("SelectOne").performed += OnSelect;
        // Shift + mouse left click
        _inputService.CurrentInputActions.FindAction("SelectMany").performed += OnSelectMore;

    }


    private void Update()
    {
        if (_isSelectMorePending)
        {
            ISelectable selectable = RayCast();
            if (selectable != null)
            {
                if (!SelectionList.Exists(s => ReferenceEquals(s, selectable)))
                {
                    SelectionList.Add(selectable);
                    selectable.Select();
                }
                else
                {
                    SelectionList.Remove(selectable);
                    selectable.Deselect();
                }
            }
        }
        else if (_isSelectPending)
        {
            ISelectable selectable = RayCast();
            if (selectable != null) 
            {
                if (!SelectionList.Exists(s => ReferenceEquals(s, selectable)))
                {
                    DeselectAll();
                    SelectionList.Add(selectable);
                    selectable.Select();
                }
            }
            else 
            {
                DeselectAll();
            }
        }
        
        if (_isDrawingSelectionPending)
        {
            if (RayCast() == null && !EventSystem.current.IsPointerOverGameObject())
            {
                _mouseStartPosition = _pendingPosition;
                _isDrawingSelection = true;
                _selectionSpriteRenderer.enabled = true;
            }
        }

        if (_isDrawingSelection)
        {
            DrawSelection();
        }

        if (_isDrawingSelectionMoreEndPending)
        {
            var hits = Physics2D.OverlapAreaAll(_mouseStartPosition, _mousePosition);

            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].TryGetComponent<ISelectable>(out ISelectable selectable))
                {
                    SelectionList.Add(selectable);
                    selectable.Select();
                }
            }
            SelectionList = SelectionList.Distinct().ToList();

            _isDrawingSelection = false;
        }
        else if (_isDrawingSelectionEndPending)
        {
            DeselectAll();
            var hits = Physics2D.OverlapAreaAll(_mouseStartPosition, _mousePosition);

            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].TryGetComponent<ISelectable>(out ISelectable selectable))
                {
                    SelectionList.Add(selectable);
                    selectable.Select();
                }
            }
            SelectionList = SelectionList.Distinct().ToList();

            _isDrawingSelection = false;
        }

        _isSelectMorePending = false;
        _isDrawingSelectionPending = false;
        _isSelectPending = false;
        _isDrawingSelectionEndPending = false;
        _isDrawingSelectionMoreEndPending = false;
    }

    private void OnSelect(InputAction.CallbackContext context)
    {
        _isSelectPending = true;
        _pendingPosition = _camera.ScreenToWorldPoint(Pointer.current.position.value);
    }

    private void OnSelectMore(InputAction.CallbackContext context)
    {
        _isSelectMorePending = true;
        _pendingPosition = _camera.ScreenToWorldPoint(Pointer.current.position.value);
    }

    private void OnSelectingEnd(InputAction.CallbackContext context)
    {
        if (!_isDrawingSelection)
            return;

        _isDrawingSelectionEndPending = true;
        _selectionSpriteRenderer.enabled = false;
    }

    private void OnSelectingMoreEnd(InputAction.CallbackContext context)
    {
        if (!_isDrawingSelection)
            return;

        _isDrawingSelectionMoreEndPending = true;
        _selectionSpriteRenderer.enabled = false;
    }


    private void OnSelecting(InputAction.CallbackContext context)
    {
        _mousePosition = _camera.ScreenToWorldPoint(context.ReadValue<Vector2>());
        _pendingPosition = _mousePosition;
        StartDrawingSelection();
    }

    public void DeselectAll()
    {
        for (int i = 0; i < SelectionList.Count; i++)
        {
            SelectionList[i].Deselect();
        }
        SelectionList.Clear();
    }

    private void StartDrawingSelection()
    {
        if (_isDrawingSelection)
            return;

        _isDrawingSelectionPending = true;
    }

    private void DrawSelection()
    {
        _selectionSpriteRenderer.transform.position = (_mousePosition + _mouseStartPosition) / 2;
        Vector2 size = _mouseStartPosition - _mousePosition;
        size.x = Mathf.Abs(size.x);
        size.y = Mathf.Abs(size.y);
        _selectionSpriteRenderer.size = size;
    }

    private ISelectable RayCast()
    {
        if (EventSystem.current.IsPointerOverGameObject()!)
            return null;

        _hit = Physics2D.Raycast(_pendingPosition, Vector2.zero);
        if (_hit.collider != null && _hit.collider.TryGetComponent<ISelectable>(out ISelectable selectable))
        {
            return selectable;
        }

        return null;
    }
}