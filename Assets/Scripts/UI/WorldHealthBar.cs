using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Barra de vida local sobre una entidad. Se construye en runtime para evitar
/// que cada barra necesite un NetworkObject propio.
/// </summary>
public sealed class WorldHealthBar : MonoBehaviour
{
    private const float CanvasScale = 0.01f;

    private Health _health;
    private UiHealth _healthUi;
    private PlayerSoul _soul;
    private Transform _canvasTransform;
    private Canvas _canvas;
    private TextMeshProUGUI _nameLabel;
    private bool _showPlayerName;

    public void SetUp(Health health, float width, float height, float verticalOffset, bool showPlayerName)
    {
        Clear();
        _health = health;
        _showPlayerName = showPlayerName;
        CreateVisualIfNeeded();
        RectTransform canvasRect = (RectTransform)_canvasTransform;
        float nameHeight = _showPlayerName ? 28f : 0f;
        canvasRect.sizeDelta = new Vector2(width, height + nameHeight);
        canvasRect.localPosition = Vector3.up * verticalOffset;
        ConfigureBarLayout(width, height, nameHeight);
        _healthUi.SetUpHealth(_health, _healthUi.GetComponent<Image>());
        if (_showPlayerName) TryAttachSoul();
        AlignToCamera();
    }

    public void Clear()
    {
        if (_healthUi != null) _healthUi.ClearHealth();
        if (_soul != null) _soul.OnNameChanged -= HandleNameChanged;
        _soul = null;
        _health = null;
        _showPlayerName = false;
        if (_nameLabel != null) _nameLabel.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (_showPlayerName && _soul == null) TryAttachSoul();
        AlignToCamera();
    }

    private void CreateVisualIfNeeded()
    {
        if (_canvas != null) return;

        var canvasObject = new GameObject("World Health Bar", typeof(RectTransform), typeof(Canvas));
        canvasObject.transform.SetParent(transform, false);
        _canvasTransform = canvasObject.transform;
        _canvas = canvasObject.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.sortingOrder = 10;

        var canvasRect = (RectTransform)_canvasTransform;

        canvasRect.localScale = Vector3.one * CanvasScale;

        Image background = CreateImage("Background", canvasRect, new Color(0.06f, 0.06f, 0.06f, 0.9f));
        RectTransform backgroundRect = background.rectTransform;
        backgroundRect.anchorMin = new Vector2(0f, 0f);
        backgroundRect.anchorMax = new Vector2(1f, 0f);
        backgroundRect.pivot = new Vector2(0.5f, 0f);

        Image fill = CreateImage("Fill", backgroundRect, new Color(0.85f, 0.12f, 0.08f, 1f));
        RectTransform fillRect = fill.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(1f, 1f);
        fillRect.offsetMax = new Vector2(-1f, -1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        _healthUi = fill.gameObject.AddComponent<UiHealth>();

        var labelObject = new GameObject("Player Name", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(canvasRect, false);
        _nameLabel = labelObject.GetComponent<TextMeshProUGUI>();
        _nameLabel.alignment = TextAlignmentOptions.Center;
        _nameLabel.fontSize = 24f;
        _nameLabel.fontStyle = FontStyles.Bold;
        _nameLabel.color = Color.white;
        _nameLabel.outlineWidth = 0.18f;
        _nameLabel.outlineColor = Color.black;
        _nameLabel.raycastTarget = false;
        RectTransform labelRect = _nameLabel.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.pivot = new Vector2(0.5f, 0f);
        labelRect.localRotation = Quaternion.Euler(0f, 180f, 0f);
    }

    private void ConfigureBarLayout(float width, float height, float nameHeight)
    {
        RectTransform backgroundRect = _healthUi.transform.parent as RectTransform;
        backgroundRect.sizeDelta = new Vector2(width, height);
        backgroundRect.anchoredPosition = Vector2.zero;

        if (_nameLabel == null) return;
        RectTransform labelRect = _nameLabel.rectTransform;
        labelRect.sizeDelta = new Vector2(width, nameHeight);
        labelRect.anchoredPosition = Vector2.zero;
        _nameLabel.gameObject.SetActive(_showPlayerName);
    }

    private void TryAttachSoul()
    {
        if (_health == null || _nameLabel == null) return;
        _soul = PlayerSoul.GetFor(_health.OwnerClientId);
        if (_soul == null) return;

        _soul.OnNameChanged += HandleNameChanged;
        HandleNameChanged(_soul.PlayerName);
    }

    private void HandleNameChanged(string playerName)
    {
        if (_nameLabel != null)
            _nameLabel.text = string.IsNullOrWhiteSpace(playerName) ? "Jugador" : playerName;
    }

    private static Image CreateImage(string objectName, Transform parent, Color color)
    {
        var imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private void AlignToCamera()
    {
        if (_canvasTransform == null) return;

        Camera camera = CameraManager.Instance != null ? CameraManager.Instance.MainCam : Camera.main;
        if (camera == null) return;

        // La cara visible de la barra mira hacia la cámara y conserva su
        // inclinación isométrica, independientemente del giro de la entidad.
        _canvasTransform.rotation = Quaternion.LookRotation(-camera.transform.forward, camera.transform.up);
        if (_canvas.worldCamera == null) _canvas.worldCamera = camera;
    }
}
