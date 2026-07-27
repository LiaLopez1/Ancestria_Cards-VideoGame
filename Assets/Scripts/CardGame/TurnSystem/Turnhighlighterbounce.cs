using System.Collections;
using UnityEngine;

/// <summary>
/// Animacion de "rebote" horizontal - va de la posicion base (minimo) a
/// base + distancia (maximo) y vuelta, con una curva suave (no lineal),
/// como un salto pero en el eje X en vez del Y. Pensado para un sprite
/// hijo del prefab del highlighter de turno (ademas del marco brillante
/// ya hecho con el shader UI/TurnGlow).
///
/// Sigue el mismo patron que ya usa el proyecto para animaciones locales
/// (corrutina + AnimationCurve, como en TrapsPanelUI) en vez de un plugin
/// de tweening externo.
///
/// Poner este componente en el mismo GameObject que el sprite/Image que
/// se quiere animar - ese objeto debe ser hijo del highlighter, para que
/// el rebote sea relativo a donde el highlighter este posicionado en ese
/// momento (TurnManager lo mueve entero segun el slot en turno).
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class TurnHighlighterBounce : MonoBehaviour
{
    [Header("Rebote horizontal")]
    [Tooltip("Cuanto se aleja del centro (posicion base) hacia el lado positivo - el centro es el minimo, centro + distancia es el maximo.")]
    [SerializeField] private float distancia = 15f;

    [Tooltip("Cuanto tarda en ir de un extremo al otro.")]
    [SerializeField] private float duracionIda = 0.4f;

    [Tooltip("Forma de la transicion - una curva tipo 'ease in/out' da la sensacion de salto/rebote en vez de un movimiento lineal robotico.")]
    [SerializeField] private AnimationCurve curvaSuavizado = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private RectTransform rectTransform;
    private Vector2 posicionBase;
    private Coroutine rebotandoCoroutine;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        posicionBase = rectTransform.anchoredPosition;
    }

    private void OnEnable()
    {
        rebotandoCoroutine = StartCoroutine(RebotarHorizontalmente());
    }

    private void OnDisable()
    {
        if (rebotandoCoroutine != null)
        {
            StopCoroutine(rebotandoCoroutine);
            rebotandoCoroutine = null;
        }

        // Vuelve al centro para que la proxima vez que se active arranque limpio.
        if (rectTransform != null)
        {
            rectTransform.anchoredPosition = posicionBase;
        }
    }

    private IEnumerator RebotarHorizontalmente()
    {
        // Bucle infinito entre la posicion base (minimo) y base + distancia
        // (maximo) - se corta solo via OnDisable (por ejemplo, cuando
        // TurnManager oculta el highlighter completo al terminar la
        // partida o durante el reparto).
        while (true)
        {
            yield return MoverHacia(posicionBase.x + distancia);
            yield return MoverHacia(posicionBase.x);
        }
    }

    private IEnumerator MoverHacia(float xDestino)
    {
        float xInicio = rectTransform.anchoredPosition.x;
        float tiempoTranscurrido = 0f;

        while (tiempoTranscurrido < duracionIda)
        {
            tiempoTranscurrido += Time.deltaTime;
            float progreso = Mathf.Clamp01(tiempoTranscurrido / duracionIda);
            float progresoSuavizado = curvaSuavizado.Evaluate(progreso);

            float nuevaX = Mathf.Lerp(xInicio, xDestino, progresoSuavizado);
            rectTransform.anchoredPosition = new Vector2(nuevaX, posicionBase.y);

            yield return null;
        }

        // Aseguramos llegar exacto al destino, sin arrastre por floats.
        rectTransform.anchoredPosition = new Vector2(xDestino, posicionBase.y);
    }
}