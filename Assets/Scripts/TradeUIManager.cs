using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controla los 4 paneles del flujo de intercambio (seleccionar jugador,
/// Panel A del iniciador, Panel B de propuesta, Panel C del elegido) desde
/// UN SOLO script en un GameObject SIEMPRE ACTIVO - no en los paneles
/// mismos. Mismo patron "vista/controlador" que ya usa CategoryRequestPanelUI
/// en el resto del proyecto: si este script viviera en un panel que arranca
/// desactivado, su Awake() (y con el, cualquier suscripcion o inicializacion)
/// nunca correria hasta que alguien active ese GameObject por primera vez.
///
/// TradeManager llama directo a los metodos publicos de esta clase - una
/// sola referencia serializada alcanza, no hace falta un singleton Instance
/// por panel.
/// </summary>
public class TradeUIManager : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private TradeManager tradeManager;
    [SerializeField] private HandManager handManager;

    // ---------------- Panel: seleccionar jugador ----------------
    [Header("Panel: seleccionar jugador")]
    [SerializeField] private GameObject panelSeleccionJugador;
    [SerializeField] private RectTransform contenedorBotonesJugador;
    [Tooltip("Debe tener un TMP_Text (para el nombre) y un Button.")]
    [SerializeField] private GameObject botonJugadorPrefab;

    private readonly List<GameObject> botonesJugadorInstanciados = new List<GameObject>();

    // ---------------- Panel A: el iniciador selecciona su carta ----------------
    [Header("Panel A: iniciador selecciona carta")]
    [SerializeField] private GameObject panelIniciador;
    [SerializeField] private TMP_Text mensajeIniciadorText;
    [SerializeField] private float duracionAvisoRechazo = 2.5f;

    // ---------------- Panel B: propuesta (aceptar/rechazar) ----------------
    [Header("Panel B: propuesta de intercambio")]
    [SerializeField] private GameObject panelPropuesta;
    [SerializeField] private TMP_Text mensajePropuestaText;
    [SerializeField] private Button botonAceptar;
    [SerializeField] private Button botonRechazar;

    // ---------------- Panel C: el elegido selecciona su carta ----------------
    [Header("Panel C: elegido selecciona carta")]
    [SerializeField] private GameObject panelObjetivo;
    [SerializeField] private TMP_Text mensajeObjetivoText;
    [SerializeField] private Button botonConfirmar;

    // Se reutiliza para el Panel A y el Panel C (nunca los dos a la vez,
    // porque en este cliente solo uno de los dos roles puede estar activo).
    private System.Action<int> manejadorSeleccionActual;

    private void Awake()
    {
        if (panelSeleccionJugador != null) panelSeleccionJugador.SetActive(false);
        if (panelIniciador != null) panelIniciador.SetActive(false);
        if (panelPropuesta != null) panelPropuesta.SetActive(false);
        if (panelObjetivo != null) panelObjetivo.SetActive(false);

        if (botonAceptar != null) botonAceptar.onClick.AddListener(() => ResponderPropuesta(true));
        if (botonRechazar != null) botonRechazar.onClick.AddListener(() => ResponderPropuesta(false));

        if (botonConfirmar != null)
        {
            botonConfirmar.onClick.AddListener(ConfirmarSeleccionObjetivo);
            botonConfirmar.interactable = false;
        }
    }

    // ---------------------------------------------------------------
    // Seleccionar jugador
    // ---------------------------------------------------------------

    /// <summary>Conectar al boton "Intercambio" del panel de trampas.</summary>
    public void AbrirYPedirLista()
    {
        tradeManager.SolicitarListaDeJugadores();
    }

    /// <summary>Llamado por TradeManager cuando el servidor responde con la lista de companeros posibles.</summary>
    public void MostrarListaDeJugadores(int[] slots, string[] nombres)
    {
        LimpiarBotonesJugador();

        if (slots == null || slots.Length == 0)
        {
            Debug.LogWarning("[TradeUIManager] No hay otros jugadores conectados para intercambiar.");
            return;
        }

        for (int i = 0; i < slots.Length; i++)
        {
            int slotCapturado = slots[i]; // copia local - necesaria para el closure del boton
            string nombre = i < nombres.Length ? nombres[i] : $"Jugador {slotCapturado}";

            GameObject boton = Instantiate(botonJugadorPrefab, contenedorBotonesJugador);

            TMP_Text texto = boton.GetComponentInChildren<TMP_Text>();
            if (texto != null)
            {
                texto.text = nombre;
            }

            Button botonComponente = boton.GetComponent<Button>();
            if (botonComponente != null)
            {
                botonComponente.onClick.AddListener(() => SeleccionarJugador(slotCapturado));
            }

            botonesJugadorInstanciados.Add(boton);
        }

        if (panelSeleccionJugador != null)
        {
            panelSeleccionJugador.SetActive(true);
        }
    }

    private void SeleccionarJugador(int slot)
    {
        tradeManager.SolicitarIntercambioConSlot(slot);
        CerrarPanelSeleccionJugador();
    }

    private void CerrarPanelSeleccionJugador()
    {
        if (panelSeleccionJugador != null)
        {
            panelSeleccionJugador.SetActive(false);
        }

        LimpiarBotonesJugador();
    }

    private void LimpiarBotonesJugador()
    {
        foreach (GameObject boton in botonesJugadorInstanciados)
        {
            if (boton != null)
            {
                Destroy(boton);
            }
        }

        botonesJugadorInstanciados.Clear();
    }

    // ---------------------------------------------------------------
    // Panel A: el iniciador selecciona su carta (sin boton, click = confirma)
    // ---------------------------------------------------------------

    /// <summary>Llamado por TradeManager justo despues de elegir con quien intercambiar.</summary>
    public void MostrarSeleccionIniciador()
    {
        if (mensajeIniciadorText != null)
        {
            mensajeIniciadorText.text = "Selecciona la carta que quieres intercambiar.";
        }

        if (panelIniciador != null)
        {
            panelIniciador.SetActive(true);
        }

        handManager.HabilitarSeleccionParaIntercambio();

        manejadorSeleccionActual = ManejarCartaElegidaIniciador;
        handManager.OnCartaSeleccionadaParaIntercambio += manejadorSeleccionActual;
    }

    private void ManejarCartaElegidaIniciador(int cardId)
    {
        if (cardId < 0)
        {
            return;
        }

        DesuscribirSeleccion();

        tradeManager.ConfirmarCartaIniciador(cardId);

        if (mensajeIniciadorText != null)
        {
            mensajeIniciadorText.text = "Esperando al otro jugador...";
        }
    }

    /// <summary>Llamado por TradeManager si el invitado rechazo - reutiliza el Panel A.</summary>
    public void MostrarRechazoAlIniciador()
    {
        DesuscribirSeleccion();
        handManager.DeshabilitarSeleccionParaIntercambio();

        if (mensajeIniciadorText != null)
        {
            mensajeIniciadorText.text = "El otro jugador no aceptó el intercambio.";
        }

        if (panelIniciador != null)
        {
            panelIniciador.SetActive(true);
        }

        StartCoroutine(CerrarPanelIniciadorDespuesDeUnTiempo());
    }

    private IEnumerator CerrarPanelIniciadorDespuesDeUnTiempo()
    {
        yield return new WaitForSeconds(duracionAvisoRechazo);
        CerrarPanelIniciador();
    }

    private void CerrarPanelIniciador()
    {
        DesuscribirSeleccion();
        handManager.DeshabilitarSeleccionParaIntercambio();

        if (panelIniciador != null)
        {
            panelIniciador.SetActive(false);
        }
    }

    // ---------------------------------------------------------------
    // Panel B: propuesta (aceptar/rechazar)
    // ---------------------------------------------------------------

    /// <summary>Llamado por TradeManager cuando el iniciador ya eligio su carta.</summary>
    public void MostrarPropuesta(string nombreIniciador)
    {
        if (mensajePropuestaText != null)
        {
            mensajePropuestaText.text = $"{nombreIniciador} quiere intercambiar una carta contigo. ¿Aceptas?";
        }

        if (panelPropuesta != null)
        {
            panelPropuesta.SetActive(true);
        }
    }

    private void ResponderPropuesta(bool acepta)
    {
        tradeManager.ResponderPropuesta(acepta);

        if (panelPropuesta != null)
        {
            panelPropuesta.SetActive(false);
        }
    }

    // ---------------------------------------------------------------
    // Panel C: el elegido selecciona su carta (con boton Confirmar)
    // ---------------------------------------------------------------

    /// <summary>Llamado por TradeManager justo despues de que el elegido acepta la propuesta.</summary>
    public void MostrarSeleccionObjetivo()
    {
        if (mensajeObjetivoText != null)
        {
            mensajeObjetivoText.text = "Selecciona la carta que quieres intercambiar.";
        }

        if (botonConfirmar != null)
        {
            botonConfirmar.interactable = false;
        }

        if (panelObjetivo != null)
        {
            panelObjetivo.SetActive(true);
        }

        handManager.HabilitarSeleccionParaIntercambio();

        manejadorSeleccionActual = ManejarCartaElegidaObjetivo;
        handManager.OnCartaSeleccionadaParaIntercambio += manejadorSeleccionActual;
    }

    /// <summary>A diferencia del iniciador, esto solo habilita el boton Confirmar - no manda nada todavia.</summary>
    private void ManejarCartaElegidaObjetivo(int cardId)
    {
        if (botonConfirmar != null)
        {
            botonConfirmar.interactable = cardId >= 0;
        }
    }

    private void ConfirmarSeleccionObjetivo()
    {
        int cardId = handManager.ObtenerCardIdSeleccionado();

        if (cardId < 0)
        {
            Debug.LogWarning("[TradeUIManager] Se apreto Confirmar sin ninguna carta seleccionada.");
            return;
        }

        if (botonConfirmar != null)
        {
            botonConfirmar.interactable = false;
        }

        DesuscribirSeleccion();

        tradeManager.ConfirmarCartaObjetivo(cardId);

        if (mensajeObjetivoText != null)
        {
            mensajeObjetivoText.text = "Confirmando el intercambio...";
        }
    }

    private void CerrarPanelObjetivo()
    {
        DesuscribirSeleccion();
        handManager.DeshabilitarSeleccionParaIntercambio();

        if (panelObjetivo != null)
        {
            panelObjetivo.SetActive(false);
        }
    }

    // ---------------------------------------------------------------
    // Cierre general
    // ---------------------------------------------------------------

    /// <summary>
    /// Llamado por TradeManager cuando el intercambio termina con exito.
    /// Cierra el Panel A y el Panel C (solo uno de los dos esta realmente
    /// abierto en este cliente segun su rol - cerrar ambos es inofensivo)
    /// y por las dudas tambien el Panel B.
    /// </summary>
    public void CerrarTodo()
    {
        CerrarPanelIniciador();
        CerrarPanelObjetivo();

        if (panelPropuesta != null)
        {
            panelPropuesta.SetActive(false);
        }
    }

    private void DesuscribirSeleccion()
    {
        if (manejadorSeleccionActual != null)
        {
            handManager.OnCartaSeleccionadaParaIntercambio -= manejadorSeleccionActual;
            manejadorSeleccionActual = null;
        }
    }
}