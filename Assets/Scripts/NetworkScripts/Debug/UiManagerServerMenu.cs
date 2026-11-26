
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UiManagerServerMenu : MonoBehaviour
{
    [Header("Botones")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;

    [Header("Conexión")]
    [Tooltip("En modo Steam se utiliza para ingresar el SteamID del Host. En modo Local permanece oculto.")]
    [SerializeField] private TMP_InputField ipInput;

    [Header("Jugador")]
    [Tooltip("Nombre que se mostrará sobre el personaje durante la partida.")]
    [SerializeField] private TMP_InputField playerNameInput;

    [Header("Mensajes")]
    [Tooltip("Opcional: texto donde se muestran los errores de conexión.")]
    [SerializeField] private TMP_Text errorText;

    private SessionManager _session;

    private void Start()
    {
        _session = SessionManager.instance;

        if (_session == null)
        {
            Debug.LogError(
                "[UiManagerServerMenu] No se encontró SessionManager."
            );

            return;
        }

        if (hostButton != null)
            hostButton.onClick.AddListener(OnHostClicked);

        if (joinButton != null)
            joinButton.onClick.AddListener(OnJoinClicked);

        if (errorText != null)
            errorText.text = "";

        ConfigureConnectionInput();

        if (playerNameInput == null && ipInput != null)
            CreatePlayerNameInput();

        if (playerNameInput != null)
        {
            playerNameInput.characterLimit = 24;
            playerNameInput.text = _session.LocalPlayerName;
        }

        _session.OnConnectionError += ShowError;
    }

    private void OnDestroy()
    {
        if (hostButton != null)
            hostButton.onClick.RemoveListener(OnHostClicked);

        if (joinButton != null)
            joinButton.onClick.RemoveListener(OnJoinClicked);

        if (_session != null)
            _session.OnConnectionError -= ShowError;
    }

    // =========================================================
    // CONFIGURACIÓN DE LA UI
    // =========================================================

    private void ConfigureConnectionInput()
    {
        if (ipInput == null)
            return;

        bool isSteam =
            _session.CurrentNetworkMode == SessionManager.NetworkMode.Steam;

        ipInput.gameObject.SetActive(isSteam);

        if (ipInput.placeholder is TMP_Text placeholder)
        {
            placeholder.text = isSteam
                ? "SteamID del Host"
                : "Conexión local";
        }

        ipInput.text = "";
    }

    private void CreatePlayerNameInput()
    {
        if (ipInput == null)
            return;

        GameObject inputObject = Instantiate(
            ipInput.gameObject,
            ipInput.transform.parent
        );

        inputObject.name = "Player Name Input";

        playerNameInput = inputObject.GetComponent<TMP_InputField>();

        if (playerNameInput == null)
        {
            Debug.LogError(
                "[UiManagerServerMenu] No se pudo crear el campo de nombre."
            );

            Destroy(inputObject);
            return;
        }

        playerNameInput.text = "";
        playerNameInput.characterLimit = 24;

        RectTransform inputRect =
            inputObject.GetComponent<RectTransform>();

        inputRect.anchoredPosition += new Vector2(0f, -38f);

        if (playerNameInput.placeholder is TMP_Text placeholder)
            placeholder.text = "Nombre del jugador";
    }

    // =========================================================
    // BOTONES
    // =========================================================

    private void OnHostClicked()
    {
        ClearError();

        if (_session == null)
            return;

        SavePlayerName();

        _session.StartHost();
    }

    private void OnJoinClicked()
    {
        ClearError();

        if (_session == null)
            return;

        SavePlayerName();

        if (_session.CurrentNetworkMode == SessionManager.NetworkMode.Steam)
        {
            if (ipInput == null || string.IsNullOrWhiteSpace(ipInput.text))
            {
                ShowError("Ingresá el SteamID del Host.");
                return;
            }

            _session.StartClient(ipInput.text);
        }
        else
        {
            _session.StartClient();
        }
    }

    // =========================================================
    // NOMBRE DEL JUGADOR
    // =========================================================

    private void SavePlayerName()
    {
        if (_session == null)
            return;

        _session.SetLocalPlayerName(
            playerNameInput != null
                ? playerNameInput.text
                : ""
        );
    }

    // =========================================================
    // MENSAJES
    // =========================================================

    private void ShowError(string message)
    {
        if (errorText != null)
            errorText.text = message;
    }

    private void ClearError()
    {
        if (errorText != null)
            errorText.text = "";
    }
}
