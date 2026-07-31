using System.Collections;
using UnityEngine;

/// <summary>
/// Animacion de "rebote" - va de la posicion base (minimo) a base + distancia
/// (maximo) y vuelta, con una curva suave (no lineal), en el eje que elijas
/// (X u Y) - el otro eje queda siempre fijo en su valor base. Pensado para
/// un sprite hijo del prefab del highlighter de turno (ademas del marco
/// brillante ya hecho con el shader UI/TurnGlow).
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
    private enum Eje
    {
        X,
        Y
    }

    [Header("Rebote")]
    [Tooltip("En que eje rebota - X (horizontal) o Y (vertical). El otro eje queda siempre fijo en su valor base.")]
    [SerializeField] private Eje ejeDeRebote = Eje.X;

    [Tooltip("Cuanto se aleja del centro (posicion base) hacia el lado positivo del eje elegido - el centro es el minimo, centro + distancia es el maximo.")]
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
        rebotandoCoroutine = StartCoroutine(Rebotar());
    }

    private void OnDisable()
    {
        if (rebotandoCoroutine != null)
        {
            StopCoroutine(rebotandoCoroutine);
            rebotandoCoroutine = null;
        }

        // Vuelve a la base para que la proxima vez que se active arranque limpio.
        if (rectTransform != null)
        {
            rectTransform.anchoredPosition = posicionBase;
        }
    }

    private IEnumerator Rebotar()
    {
        // Bucle infinito ida y vuelta mientras el objeto este activo - se
        // corta solo via OnDisable (por ejemplo, cuando TurnManager oculta
        // el highlighter completo al terminar la partida o durante el
        // reparto).
        float valorBase = ObtenerValorEnEje(posicionBase);

        while (true)
        {
            yield return MoverHacia(valorBase + distancia);
            yield return MoverHacia(valorBase);
        }
    }

    private IEnumerator MoverHacia(float destino)
    {
        float inicio = ObtenerValorEnEje(rectTransform.anchoredPosition);
        float tiempoTranscurrido = 0f;

        while (tiempoTranscurrido < duracionIda)
        {
            tiempoTranscurrido += Time.deltaTime;
            float progreso = Mathf.Clamp01(tiempoTranscurrido / duracionIda);
            float progresoSuavizado = curvaSuavizado.Evaluate(progreso);

            float valor = Mathf.Lerp(inicio, destino, progresoSuavizado);
            AplicarValorEnEje(valor);

            yield return null;
        }

        // Aseguramos llegar exacto al destino, sin arrastre por floats.
        AplicarValorEnEje(destino);
    }

    /// <summary>Lee la coordenada del eje elegido (X o Y) de una posicion dada.</summary>
    private float ObtenerValorEnEje(Vector2 posicion)
    {
        return ejeDeRebote == Eje.X ? posicion.x : posicion.y;
    }

    /// <summary>Aplica el valor al eje elegido, dejando el otro eje fijo en su valor base.</summary>
    private void AplicarValorEnEje(float valor)
    {
        rectTransform.anchoredPosition = ejeDeRebote == Eje.X
            ? new Vector2(valor, posicionBase.y)
            : new Vector2(posicionBase.x, valor);
    }
}