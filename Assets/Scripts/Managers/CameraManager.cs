using Unity.Cinemachine;
using UnityEngine;

public class CameraManager : MonoBehaviour
{
    private CameraManager _instance;

    public CameraManager Instance => _instance;

    [SerializeField] private CinemachineCamera _camera;

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
