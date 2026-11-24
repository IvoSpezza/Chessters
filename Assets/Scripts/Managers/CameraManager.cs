using Unity.Cinemachine;
using UnityEngine;

public class CameraManager : MonoBehaviour
{
    private static CameraManager _instance;

    public static CameraManager Instance => _instance;

    [SerializeField] private CinemachineCamera _camera;

    [SerializeField] private Camera _mainCam;
    public Camera MainCam => _mainCam;

    private Transform _defaultTarget;
    private Transform _focusTarget;
    private Transform _movingFocusTarget;
    private Vector3 _fixedFocusPoint;
    private bool _hasFocusOverride;

    private void Awake()
    {
        if(_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }        
        _instance = this;
        GameObject focusObject = new GameObject("Camera Focus Target");
        _focusTarget = focusObject.transform;
    }

    private void LateUpdate()
    {
        if (_movingFocusTarget == null) return;
        _focusTarget.position = (_fixedFocusPoint + _movingFocusTarget.position) * 0.5f;
    }

    public void SetTarget(Transform target)
    {
        _defaultTarget = target;
        if (!_hasFocusOverride) _camera.Follow = target;
    }

    public void FocusAt(Vector3 position)
    {
        _movingFocusTarget = null;
        _focusTarget.position = position;
        _hasFocusOverride = true;
        _camera.Follow = _focusTarget;
    }

    /// <summary>Enmarca un punto fijo (por ejemplo el monstruo) y un objetivo móvil.</summary>
    public void FocusBetween(Vector3 fixedPosition, Transform movingTarget)
    {
        if (movingTarget == null)
        {
            FocusAt(fixedPosition);
            return;
        }

        _fixedFocusPoint = fixedPosition;
        _movingFocusTarget = movingTarget;
        _focusTarget.position = (_fixedFocusPoint + _movingFocusTarget.position) * 0.5f;
        _hasFocusOverride = true;
        _camera.Follow = _focusTarget;
    }

    public void RestoreDefaultTarget()
    {
        _movingFocusTarget = null;
        _hasFocusOverride = false;
        _camera.Follow = _defaultTarget;
    }
}
