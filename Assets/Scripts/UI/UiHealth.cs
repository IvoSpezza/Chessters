using UnityEngine;
using UnityEngine.UI;

public class UiHealth : MonoBehaviour
{
    [SerializeField] private Image _health;
      
    private Health _healthValue;    

    public void SetUpHealth(Health playerHealth)
    {
        SetUpHealth(playerHealth, _health);
    }

    /// <summary>Conecta esta barra con la vida indicada y permite asignar la imagen en runtime.</summary>
    public void SetUpHealth(Health playerHealth, Image healthImage)
    {
        ClearHealth();
        _health = healthImage;
        _healthValue = playerHealth;
        if (_healthValue == null || _health == null) return;
        _healthValue.OnHealthChanged += ActualizeHealth;
        ActualizeHealth(_healthValue.CurrentHealth, _healthValue.MaxHealth);
      
    }
    private void ActualizeHealth(float actualHealth, float maxHealth)
    {
        if (_healthValue == null || _health == null) return;
        Vector3 actualScale = _health.rectTransform.localScale;
        actualScale.x = actualHealth > 0 ? Mathf.Clamp01(actualHealth / maxHealth) : 0f;
        _health.rectTransform.localScale = actualScale;
    }
    public void ClearHealth()
    {
        if (_healthValue != null) _healthValue.OnHealthChanged -= ActualizeHealth;
        _healthValue = null;
    }

    private void OnDestroy()
    {
        ClearHealth();
    }
}
