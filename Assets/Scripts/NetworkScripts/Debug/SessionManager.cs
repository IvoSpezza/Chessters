using System;
using System.Collections.Generic;
using System.Net;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SessionManager : MonoBehaviour
{
    [SerializeField] private string iPadress = "127.0.0.1";
    [SerializeField] private ushort port = 7777;

    [SerializeField] private int playersAmount = 2;
    public static SessionManager instance {  get; private set;}

    [SerializeField] private String START_SCENE = "ivoScene";
    private NetworkManager NetManager => NetworkManager.Singleton;

    private void Awake()
    {
        if(instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        NetManager.OnClientConnectedCallback += NetworkManager_ClientConnectedCallBack;
        NetManager.OnClientDisconnectCallback += NetworkManager_ClientDesconnectedCallBack;
        NetManager.OnServerStarted += NetworkManager_ServerStarted;
        NetManager.OnServerStopped += NetworkManager_ServerStopped;
    }

    

    private void NetworkManager_ClientConnectedCallBack(ulong clientId)
    {
        if (clientId == NetworkManager.ServerClientId)
        {
            Debug.Log("HostConected");
        } else
        {
            Debug.Log($"Client {clientId} conected");
        }

        if (!NetManager.IsServer) return;

        if(NetManager.ConnectedClients.Count == playersAmount)
        {
            NetManager.SceneManager.LoadScene(START_SCENE, LoadSceneMode.Single);
        }
    }

    private void NetworkManager_ClientDesconnectedCallBack(ulong clientId)
    {
        Debug.Log($"Client {clientId} Desconected");
    }
    private void NetworkManager_ServerStopped(bool obj)
    {
        Debug.Log("Server Stopped");
    }

    private void NetworkManager_ServerStarted()
    {
        Debug.Log("Server Started");
        NetManager.SceneManager.OnLoadEventCompleted += SceneManager_OnServerSincronized;
    }

    private void SceneManager_OnServerSincronized(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        Debug.Log($"SE SINCRONIZO, se sincrionizaron {clientsCompleted.Count}.{clientsTimedOut.Count} usuarions a la escena{sceneName}");
    }

    public void StartHost()
    {
        UnityTransport utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
        utp.SetConnectionData(iPadress, port);
        NetworkManager.Singleton.StartHost();
    }

    public void StartClient()
    {
        UnityTransport utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
        utp.SetConnectionData(iPadress, port);
        NetworkManager.Singleton.StartClient();
    }
}
