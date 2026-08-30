using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

public class UiManagerServerMenu : MonoBehaviour
{
    [SerializeField] private Button startClient;
    [SerializeField] private Button startHost;

    public event Action OnStartHost;
    public event Action OnStartClient;

    private void Start()
    {
        startClient.onClick.AddListener(() => OnStartClient?.Invoke());
        startHost.onClick.AddListener(() => OnStartHost?.Invoke());

        OnStartClient += SessionManager.instance.StartClient;
        OnStartHost += SessionManager.instance.StartHost;
    }
}
