using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UiManagerServerMenu : MonoBehaviour
{
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [Tooltip("Con Relay es el campo del código de sala. Sin Relay, la IP.")]
    [SerializeField] private TMP_InputField ipInput;
    [Tooltip("Opcional: texto donde se muestran los errores de conexión.")]
    [SerializeField] private TMP_Text errorText;

    private void Start()
    {
        hostButton.onClick.AddListener(OnHostClicked);
        joinButton.onClick.AddListener(OnJoinClicked);

        if (errorText != null) errorText.text = "";
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
        SessionManager.instance.StartHost();
    }

    private void OnJoinClicked()
    {
        ClearError();
        SessionManager.instance.StartClient(ipInput.text);
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