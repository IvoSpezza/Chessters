
using Steamworks;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class SteamBootstrap : MonoBehaviour
{
    public static SteamBootstrap Instance { get; private set; }

    public bool IsInitialized { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        try
        {
            IsInitialized = SteamAPI.Init();

            if (!IsInitialized)
            {
                Debug.LogError("[Steam] No se pudo inicializar Steamworks.");
                return;
            }

            Debug.Log($"[Steam] Inicializado correctamente. SteamID: {SteamUser.GetSteamID()}");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            IsInitialized = false;
        }
    }

    private void Update()
    {
        if (IsInitialized)
            SteamAPI.RunCallbacks();
    }

    private void OnApplicationQuit()
    {
        ShutdownSteam();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            ShutdownSteam();
            Instance = null;
        }
    }

    private void ShutdownSteam()
    {
        if (!IsInitialized)
            return;

        SteamAPI.Shutdown();
        IsInitialized = false;

        Debug.Log("[Steam] Steamworks cerrado.");
    }
}
