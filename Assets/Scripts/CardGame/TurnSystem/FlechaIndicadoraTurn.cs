using UnityEngine;

/// <summary>
/// Animacion simple de "flecha flotante": sube y baja en loop mientras el
/// objeto esta activo. Se pone directamente en el GameObject de cada icono
/// (indicadorRobar / indicadorDescartar en TurnManager) - TurnManager solo
/// prende/apaga el GameObject via SetActive(), esta clase no sabe nada de
/// turnos, red, ni reglas, asi que se puede reusar en cualquier otro icono
/// que quieras animar igual.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class FlechaIndicadora : MonoBehaviour
{
    [Tooltip("Cuanto sube y baja, en unidades de UI (pixeles, si esta dentro de un Canvas).")]
    [SerializeField] private float amplitud = 15f;
    [Tooltip("Velocidad del movimiento - mas alto = mas rapido.")]
    [SerializeField] private float velocidad = 2f;

    private RectTransform rectTransform;
    private Vector2 posicionBase;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        posicionBase = rectTransform.anchoredPosition;
    }

    private void OnEnable()
    {
        // Vuelve a guardar la posicion base cada vez que se prende - por si
        // en algun momento se reposiciono el icono manualmente en el editor
        // mientras estaba desactivado.
        posicionBase = rectTransform.anchoredPosition;
    }

    private void OnDisable()
    {
        // Al apagarse, la deja quieta en su posicion base (no "clavada" a
        // mitad del recorrido de la ultima animacion).
        if (rectTransform != null)
        {
            rectTransform.anchoredPosition = posicionBase;
        }
    }

    private void Update()
    {
        float offsetY = Mathf.Sin(Time.time * velocidad) * amplitud;
        rectTransform.anchoredPosition = posicionBase + new Vector2(0f, offsetY);
    }
}