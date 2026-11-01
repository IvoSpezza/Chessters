using UnityEngine;
using UnityEngine.UI;

public class UiHealth : MonoBehaviour
{
    [SerializeField] private Image _health;
      
    private Health _healthValue;    

    public void SetUpHealth(Health playerHealth)
    {
        ClearHealth();
        _healthValue = playerHealth;
        _healthValue.OnHealthChanged += ActualizeHealth;
      
    }
    private void ActualizeHealth(float actualHealth, float maxHealth)
    {
        if (_healthValue == null) return;
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
