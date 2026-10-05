using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UiManagerServerMenu : MonoBehaviour
{
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private TMP_InputField ipInput;

    private void Start()
    {
        hostButton.onClick.AddListener(OnHostClicked);
        joinButton.onClick.AddListener(OnJoinClicked);
    }

    private void OnDestroy()
    {
        hostButton.onClick.RemoveListener(OnHostClicked);
        joinButton.onClick.RemoveListener(OnJoinClicked);
    }

    private void OnHostClicked()
    {
        SessionManager.instance.StartHost();
    }

    private void OnJoinClicked()
    {
        SessionManager.instance.StartClient(ipInput.text);
    }
}