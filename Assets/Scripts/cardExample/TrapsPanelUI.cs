using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controla el panel de trampas: se abre/cierra con una animacion tipo
/// "pop-up" (escala de 0 a 1 con un poco de rebote). Por ahora solo maneja
/// mostrar/ocultar el panel - los botones de cada trampa individual (que
/// van dentro de este panel) se conectan a su propia logica mas adelante,
/// cuando definamos que hace cada trampa.
///
/// Es puramente local a la pantalla de cada jugador (no tiene nada de red
/// todavia) - cada jugador ve y controla su propio panel.
/// </summary>
public class TrapsPanelUI : MonoBehaviour
{
    [Header("Panel de trampas")]
    [SerializeField] private GameObject panelTrampas;

    [Header("Animación pop-up")]
    [SerializeField] private float duracionAnimacion = 0.25f;
    [Tooltip("Controla la curva de la animacion. Para un efecto 'pop' con rebote, agrega una tercera clave arriba de 1 antes de llegar a 1 (ej: 0,0 -> 0.7,1.15 -> 1,1) en el editor de curvas.")]
    [SerializeField] private AnimationCurve curvaEscala = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private RectTransform panelRect;
    private Coroutine animacionActual;
    private bool panelVisible;

    private void Awake()
    {
        panelRect = panelTrampas.GetComponent<RectTransform>();

        // Arranca oculto y en escala 0, listo para "aparecer" la primera vez.
        panelTrampas.SetActive(false);
        panelRect.localScale = Vector3.zero;
    }

    /// <summary>Conectar al OnClick() del botón principal de "Trampas".</summary>
    public void OnBotonTrampasPressed()
    {
        if (panelVisible)
        {
            CerrarPanel();
        }
        else
        {
            AbrirPanel();
        }
    }

    /// <summary>Conectar opcionalmente a un botón de "Cerrar" dentro del panel.</summary>
    public void CerrarPanel()
    {
        if (!panelVisible) return;

        panelVisible = false;

        if (animacionActual != null) StopCoroutine(animacionActual);
        animacionActual = StartCoroutine(AnimarEscala(1f, 0f, ocultarAlTerminar: true));
    }

    private void AbrirPanel()
    {
        panelVisible = true;
        panelTrampas.SetActive(true);

        if (animacionActual != null) StopCoroutine(animacionActual);
        animacionActual = StartCoroutine(AnimarEscala(0f, 1f));
    }

    private IEnumerator AnimarEscala(float desde, float hasta, bool ocultarAlTerminar = false)
    {
        float tiempo = 0f;

        while (tiempo < duracionAnimacion)
        {
            tiempo += Time.deltaTime;
            float progreso = Mathf.Clamp01(tiempo / duracionAnimacion);
            float curva = curvaEscala.Evaluate(progreso);
            float escala = Mathf.Lerp(desde, hasta, curva);

            panelRect.localScale = new Vector3(escala, escala, 1f);

            yield return null;
        }

        panelRect.localScale = new Vector3(hasta, hasta, 1f);

        if (ocultarAlTerminar)
        {
            panelTrampas.SetActive(false);
        }

        animacionActual = null;
    }
}