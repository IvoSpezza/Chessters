using Unity.Cinemachine;
using UnityEngine;

public class CameraManager : MonoBehaviour
{
    private static CameraManager _instance;

    public static CameraManager Instance => _instance;

    [SerializeField] private CinemachineCamera _camera;

    [SerializeField] private Camera _mainCam;
    public Camera MainCam => _mainCam;

    private void Awake()
    {
        if(_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }        
        _instance = this;        
    }

    public void SetTarget(Transform target)
    {
        _camera.Follow = target;
    }
}
