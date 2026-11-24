using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UiManagerServerMenu : MonoBehaviour
{
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [Tooltip("Solo se usa con Relay. En modo de prueba local se conecta automáticamente a localhost.")]
    [SerializeField] private TMP_InputField ipInput;
    [Tooltip("Nombre que se mostrará sobre el personaje durante la partida.")]
    [SerializeField] private TMP_InputField playerNameInput;
    [Tooltip("Opcional: texto donde se muestran los errores de conexión.")]
    [SerializeField] private TMP_Text errorText;

    private void Start()
    {
        hostButton.onClick.AddListener(OnHostClicked);
        joinButton.onClick.AddListener(OnJoinClicked);

        if (errorText != null) errorText.text = "";
        bool useRelay = SessionManager.instance == null || SessionManager.instance.UsesRelay;
        if (ipInput != null && ipInput.placeholder is TMP_Text placeholder)
            placeholder.text = "Código de sala";
        if (playerNameInput == null && ipInput != null)
            CreatePlayerNameInput();
        if (SessionManager.instance != null && playerNameInput != null)
        {
            playerNameInput.characterLimit = 24;
            playerNameInput.text = SessionManager.instance.LocalPlayerName;
        }
        if (ipInput != null) ipInput.gameObject.SetActive(useRelay);
        SessionManager.instance.OnConnectionError += ShowError;
    }

    private void OnDestroy()
    {
        hostButton.onClick.RemoveListener(OnHostClicked);
        joinButton.onClick.RemoveListener(OnJoinClicked);

        if (SessionManager.instance != null)
            SessionManager.instance.OnConnectionError -= ShowError;
    }

    private void OnHostClicked()
    {
        ClearError();
        SavePlayerName();
        SessionManager.instance.StartHost();
    }

    private void OnJoinClicked()
    {
        ClearError();
        SavePlayerName();
        SessionManager.instance.StartClient(SessionManager.instance.UsesRelay ? ipInput.text : null);
    }

    private void CreatePlayerNameInput()
    {
        GameObject inputObject = Instantiate(ipInput.gameObject, ipInput.transform.parent);
        inputObject.name = "Player Name Input";
        playerNameInput = inputObject.GetComponent<TMP_InputField>();
        playerNameInput.text = "";
        playerNameInput.characterLimit = 24;
        RectTransform inputRect = inputObject.GetComponent<RectTransform>();
        inputRect.anchoredPosition += new Vector2(0f, -38f);
        TMP_Text placeholder = playerNameInput.placeholder as TMP_Text;
        if (placeholder != null) placeholder.text = "Nombre del jugador";
    }

    private void SavePlayerName()
    {
        if (SessionManager.instance == null) return;
        SessionManager.instance.SetLocalPlayerName(playerNameInput != null ? playerNameInput.text : "");
    }

    private void ShowError(string message)
    {
        if (errorText != null) errorText.text = message;
    }

    private void ClearError()
    {
        if (errorText != null) errorText.text = "";
    }
}
