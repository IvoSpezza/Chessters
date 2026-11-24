using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>UI local para el chat persistente y la lista de jugadores del lobby.</summary>
public class LobbyChatUI : MonoBehaviour
{
    private static LobbyChatUI _instance;
    [SerializeField] private TMP_InputField chatInput;
    [SerializeField] private TMP_Text messageLog;
    [SerializeField] private TMP_Text playerList;
    [SerializeField] private GameObject connectedPlayersPanel;
    [SerializeField] private ScrollRect chatScrollRect;
    [SerializeField, Min(1)] private int maxVisibleMessages = 40;

    private readonly Queue<string> _messages = new Queue<string>();
    private bool _isTyping;
    private float _nextPlayerRefresh;
    private PlayerInput _localPlayerInput;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        if (transform.localScale == Vector3.zero) transform.localScale = Vector3.one;
        Canvas rootCanvas = GetComponent<Canvas>();
        if (rootCanvas != null) rootCanvas.sortingOrder = 100;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (chatInput == null) chatInput = GetComponentInChildren<TMP_InputField>(true);
        if (messageLog == null)
        {
            TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text text in texts)
                if (text.name == "MassageLog" || text.name == "MessageLog") { messageLog = text; break; }
        }
        if (playerList == null)
        {
            TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text text in texts)
                if (text.name == "PlayerList") { playerList = text; break; }
        }
        if (connectedPlayersPanel == null)
        {
            Transform panel = transform.Find("ConectedPlayers");
            if (panel != null) connectedPlayersPanel = panel.gameObject;
        }
        if (chatScrollRect == null) chatScrollRect = GetComponentInChildren<ScrollRect>(true);
        ConfigureHistoryLayout();

        if (chatInput != null)
        {
            chatInput.gameObject.SetActive(false);
            chatInput.onSubmit.AddListener(SubmitMessage);
        }
        RefreshSceneVisibility(SceneManager.GetActiveScene().name);
    }

    private void OnEnable() => PlayerSoul.OnChatMessageReceived += AddMessage;

    private void OnDisable() => PlayerSoul.OnChatMessageReceived -= AddMessage;

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (chatInput != null) chatInput.onSubmit.RemoveListener(SubmitMessage);
        SetTyping(false);
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        if (Keyboard.current != null)
        {
            if (!_isTyping && Keyboard.current.tKey.wasPressedThisFrame)
                SetTyping(true);
            else if (_isTyping && Keyboard.current.escapeKey.wasPressedThisFrame)
                SetTyping(false);
            else if (_isTyping && Keyboard.current.enterKey.wasPressedThisFrame)
                SubmitMessage(chatInput != null ? chatInput.text : string.Empty);
        }

        if (Time.unscaledTime >= _nextPlayerRefresh)
        {
            _nextPlayerRefresh = Time.unscaledTime + 0.25f;
            RefreshPlayerList();
        }
    }

    private void SetTyping(bool typing)
    {
        if (chatInput == null || _isTyping == typing) return;
        _isTyping = typing;
        chatInput.gameObject.SetActive(typing);
        if (typing)
        {
            CacheLocalPlayerInput();
            if (_localPlayerInput != null) _localPlayerInput.enabled = false;
            chatInput.text = string.Empty;
            chatInput.ActivateInputField();
            chatInput.Select();
        }
        else
        {
            if (chatInput.gameObject.activeInHierarchy) chatInput.DeactivateInputField();
            chatInput.gameObject.SetActive(false);
            if (_localPlayerInput != null) _localPlayerInput.enabled = true;
            _localPlayerInput = null;
        }
    }

    private void CacheLocalPlayerInput()
    {
        _localPlayerInput = null;
        PlayerSoul localSoul = PlayerSoul.Local;
        if (localSoul != null && localSoul.TryGetBody(out NetworkObject body) && body != null)
            _localPlayerInput = body.GetComponent<PlayerInput>();
    }

    private void SubmitMessage(string rawMessage)
    {
        if (!_isTyping) return;
        string message = (rawMessage ?? string.Empty).Trim();
        if (message.Length > 0)
        {
            if (PlayerSoul.Local != null) PlayerSoul.Local.SendChatMessage(message);
            else Debug.LogWarning("[Chat] No se pudo enviar: el jugador local todavía no tiene PlayerSoul.");
        }
        if (chatInput != null) chatInput.text = string.Empty;
        SetTyping(false);
    }

    private void AddMessage(string message)
    {
        if (messageLog == null) return;
        _messages.Enqueue(message);
        while (_messages.Count > maxVisibleMessages) _messages.Dequeue();
        messageLog.text = string.Join("\n", _messages);
        messageLog.ForceMeshUpdate();
        if (chatScrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            if (chatScrollRect.content != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(chatScrollRect.content);
            chatScrollRect.verticalNormalizedPosition = 0f;
        }
    }

    private void ConfigureHistoryLayout()
    {
        if (messageLog == null || chatScrollRect == null || chatScrollRect.content == null) return;

        chatScrollRect.horizontal = false;
        chatScrollRect.vertical = true;

        if (chatScrollRect.viewport != null)
        {
            chatScrollRect.viewport.anchorMin = Vector2.zero;
            chatScrollRect.viewport.anchorMax = Vector2.one;
            chatScrollRect.viewport.offsetMin = Vector2.zero;
            chatScrollRect.viewport.offsetMax = Vector2.zero;
            Image viewportImage = chatScrollRect.viewport.GetComponent<Image>();
            if (viewportImage != null)
            {
                Color maskColor = viewportImage.color;
                maskColor.a = 1f;
                viewportImage.color = maskColor;
            }
        }

        RectTransform content = chatScrollRect.content;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, content.sizeDelta.y);

        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        if (layout != null)
        {
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        RectTransform textRect = messageLog.rectTransform;
        textRect.anchorMin = new Vector2(0f, 1f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.pivot = new Vector2(0.5f, 1f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(0f, textRect.sizeDelta.y);
        messageLog.textWrappingMode = TextWrappingModes.Normal;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => RefreshSceneVisibility(scene.name);

    private void RefreshSceneVisibility(string sceneName)
    {
        bool inLobby = sceneName == "Lobby";
        if (connectedPlayersPanel != null) connectedPlayersPanel.SetActive(inLobby);
        if (!inLobby && _isTyping) SetTyping(false);
        if (sceneName == "MainMenu") Destroy(gameObject);
        RefreshPlayerList();
    }

    private void RefreshPlayerList()
    {
        if (playerList == null || connectedPlayersPanel == null || !connectedPlayersPanel.activeInHierarchy) return;
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening)
        {
            playerList.text = string.Empty;
            return;
        }

        List<ulong> clientIds = new List<ulong>(manager.ConnectedClientsIds);
        clientIds.Sort();
        StringBuilder lines = new StringBuilder();
        foreach (ulong clientId in clientIds)
        {
            PlayerSoul soul = PlayerSoul.GetFor(clientId);
            string name = soul != null && !string.IsNullOrWhiteSpace(soul.PlayerName)
                ? soul.PlayerName : $"Jugador {clientId + 1}";
            if (lines.Length > 0) lines.AppendLine();
            lines.Append(name);
        }
        playerList.text = lines.ToString();
    }
}
