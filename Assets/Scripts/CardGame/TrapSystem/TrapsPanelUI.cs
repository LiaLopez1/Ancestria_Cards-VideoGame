using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controla el panel de trampas: se abre/cierra con una animacion tipo
/// "pop-up" (escala de 0 a 1 con un poco de rebote). Tambien se encarga de
/// cerrar cualquier sub-panel (como el de categorias, Pedir/Decir, o el de
/// elegir companero de intercambio) que haya quedado abierto - sin esto,
/// un sub-panel podia quedar huerfano en pantalla si el jugador volvia a
/// apretar el boton global de "Trampas". Si el panel de elegir companero
/// se cierra ASI (sin haber llegado a elegir a nadie), tambien cancela el
/// aumento de sospecha que habia arrancado al apretar "Intercambio".
///
/// Es puramente local a la pantalla de cada jugador (no tiene nada de red
/// todavia) - cada jugador ve y controla su propio panel.
/// </summary>
public class TrapsPanelUI : MonoBehaviour
{
    [Header("Panel de trampas")]
    [SerializeField] private GameObject panelTrampas;

    [Header("Boton global (para poder des/habilitarlo)")]
    [SerializeField] private Button botonTrampas;

    [Header("Sub-paneles que tambien hay que cerrar (para no dejar ninguno huerfano)")]
    [SerializeField] private CategoryRequestPanelUI categoryRequestPanelUI;
    [SerializeField] private TradeUIManager tradeUIManager;
    [SerializeField] private SuspicionManager suspicionManager;

    [Header("Animación pop-up")]
    [SerializeField] private float duracionAnimacion = 0.25f;
    [Tooltip("Controla la curva de la animacion. Para un efecto 'pop' con rebote, agrega una tercera clave arriba de 1 antes de llegar a 1 (ej: 0,0 -> 0.7,1.15 -> 1,1) en el editor de curvas.")]
    [SerializeField] private AnimationCurve curvaEscala = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Audio")]
    [SerializeField] private SoundData TrapSound;
    [SerializeField] private SoundData CloseButtonSound;
    


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
        // Si CUALQUIERA de los dos esta abierto (el menu principal, o algun
        // sub-panel como el de categorias o el de elegir con quien
        // intercambiar), hay que cerrar todo - nunca reabrir el menu
        // principal mientras algo mas siga en pantalla.
        bool haySubPanelAbierto =
            (categoryRequestPanelUI != null && categoryRequestPanelUI.EstaAbierto) ||
            (tradeUIManager != null && tradeUIManager.PanelSeleccionJugadorAbierto);

        if (panelVisible || haySubPanelAbierto)
        {
            CerrarPanel();
        }
        else
        {
            AbrirPanel();
        }

        TrapSound.Play();


    }

    /// <summary>Conectar opcionalmente a un botón de "Cerrar" dentro del panel.</summary>
    public void CerrarPanel()
    {
        // Hay que chequear esto ANTES de cerrar nada: si el panel de elegir
        // companero estaba abierto y todavia no hubo compromiso (no se
        // eligio a nadie), hay que cancelar tambien el aumento de sospecha
        // que arranco al apretar "Intercambio" - si no, seguiria subiendo
        // para siempre aunque el intercambio nunca haya arrancado de verdad.
        bool debeCancelarSospecha = tradeUIManager != null
            && tradeUIManager.PanelSeleccionJugadorAbierto
            && !tradeUIManager.IntercambioEnProgreso;

        // Esto corre SIEMPRE, sin importar el estado de panelVisible, para
        // que un sub-panel que haya quedado huerfano (main panel ya cerrado,
        // pero categorias/seleccion de jugador todavia abiertos) se cierre igual.
        categoryRequestPanelUI?.Cerrar();
        tradeUIManager?.CerrarPanelSeleccionJugador();

        if (debeCancelarSospecha)
        {
            suspicionManager?.CancelarIntercambioRpc();
        }

        if (!panelVisible) return;

        panelVisible = false;

        if (animacionActual != null) StopCoroutine(animacionActual);
        animacionActual = StartCoroutine(AnimarEscala(1f, 0f, ocultarAlTerminar: true));

        CloseButtonSound.Play();
    }


    private void Update()
    {
        // El boton global se vuelve no interactuable desde que se elige con
        // quien intercambiar (el "punto de compromiso") hasta que el
        // intercambio termina - evita abrir cualquier otra trampa a mitad
        // de un intercambio ya en curso.
        if (botonTrampas != null)
        {
            botonTrampas.interactable = !(tradeUIManager != null && tradeUIManager.IntercambioEnProgreso);
        }
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