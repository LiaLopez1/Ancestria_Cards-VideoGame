using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public enum TurnState
{
    Dealing,
    WaitingToDraw,
    WaitingToDiscard,
}

/// <summary>
/// De quien es el turno (por slot: 0=host, 1=invitado1, 2=invitado2, mas el
/// slot del boss) y en que parte de su turno esta (robar/descartar),
/// sincronizado a todos.
///
/// El SERVIDOR es quien decide y avanza el turno (via IniciarPrimerTurno,
/// NotificarRoboRealizado, NotificarDescarteRealizado, todos llamados desde
/// DeckManager despues de validar cada accion). Los clientes solo leen el
/// estado sincronizado para saber si pueden actuar.
///
/// De cara al jugador, el turno YA NO se muestra como texto: se indica con
/// un highlighter (turnHighlighter) que se reposiciona detras del panel de
/// nombre del jugador en turno, usando coordenadas fijas por slot
/// (posicionesHighlighterPorSlot) - el boss tiene su propia posicion fija
/// (posicionHighlighterBoss), ya que su slot numerico cambia segun cuantos
/// humanos esten conectados pero su panel en pantalla es siempre el mismo.
/// El texto (turnMessage) ahora solo muestra la regla de victoria de la
/// ronda (y "Repartiendo..."/el nombre del ganador en esos momentos puntuales).
/// </summary>
public class TurnManager : NetworkBehaviour
{
    [Header("Referencias")]
    [SerializeField] private HandManager handManager;
    [SerializeField] private TMP_Text turnMessage;
    [Tooltip("Texto aparte para la explicación larga de la regla (VictoryRules.ObtenerDescripcion) - turnMessage se queda con el título corto.")]
    [SerializeField] private TMP_Text turnRuleDescriptionText;
    [Tooltip("Ahora es quien decide victoria/derrota - TurnManager solo le pregunta si la partida ya terminó.")]
    [SerializeField] private GameManager gameManager;

    [Header("Highlighter de turno")]
    [Tooltip("Objeto que se reposiciona detras del panel del jugador en turno.")]
    [SerializeField] private RectTransform turnHighlighter;
    [Tooltip("Posiciones fijas (ancoradas) por slot: [0]=host, [1]=invitado1, [2]=invitado2 - deben coincidir con los paneles de PlayerNamePanelsUI.")]
    [SerializeField] private Vector2[] posicionesHighlighterPorSlot = new Vector2[3];
    [Tooltip("Posición fija del panel del boss - a diferencia de los jugadores, el slot del boss cambia según cuántos humanos se conecten (1, 2 o 3), pero su panel en pantalla siempre está en el mismo lugar.")]
    [SerializeField] private Vector2 posicionHighlighterBoss;

    [Header("Audio")]
    [SerializeField] private SoundData NotifyTurn;

    [Header("Carta infiltrada")]
    [Tooltip("Solo se usa si la regla sorteada esta ronda es CartaInfiltrada o CartaInfiltrada2.")]
    [SerializeField] private InfiltratedCardManager infiltratedCardManager;
    [Tooltip("Panel con el icono de la categoria infiltrada - queda apagado en las demas reglas.")]
    [SerializeField] private GameObject panelCartaInfiltrada;
    [SerializeField] private Image iconoCartaInfiltrada;

    [System.Serializable]
    public struct IconoPorCategoria
    {
        public CardCategory categoria;
        public Sprite icono;
    }

    [Tooltip("Un icono por cada valor de CardCategory - se muestra el que corresponda a la categoria infiltrada sorteada.")]
    [SerializeField] private IconoPorCategoria[] iconosPorCategoriaInfiltrada;

    [Header("Debug / Pruebas")]
    [Tooltip("Si está activo, la regla de la ronda NO se sortea al azar - siempre se usa 'reglaParaPruebas'. Apágalo para volver al comportamiento normal (aleatorio).")]
    [SerializeField] private bool usarReglaFijaParaPruebas = false;
    [Tooltip("Solo se usa si 'usarReglaFijaParaPruebas' está activo. Debe existir en VictoryRules.ReglasDisponibles (descoméntala ahí si está comentada).")]
    [SerializeField] private VictoryRuleType reglaParaPruebas = VictoryRuleType.CartaInfiltrada;

    private readonly NetworkVariable<int> turnoActual = new NetworkVariable<int>(0);
    private readonly NetworkVariable<TurnState> estadoActual = new NetworkVariable<TurnState>(TurnState.Dealing);

    // Indice dentro de VictoryRules.ReglasDisponibles - sorteado por el
    // servidor al iniciar la partida, sincronizado para que todos sepan
    // que regla esta activa esta ronda.
    private readonly NetworkVariable<int> indiceReglaActual = new NetworkVariable<int>(0);

    // El boss siempre ocupa el slot inmediatamente despues del ultimo
    // jugador humano (si hay 2 jugadores conectados, slots 0 y 1, el boss
    // es el slot 2) - sincronizado para que todos los clientes puedan
    // mostrar "Turno del boss" correctamente, no solo el servidor.
    // -1 = todavia no se definio (partida no iniciada).
    private readonly NetworkVariable<int> slotBoss = new NetworkVariable<int>(-1);

    private int cantidadJugadores = 1;

    public VictoryRuleType ReglaActiva => VictoryRules.ReglasDisponibles[indiceReglaActual.Value];

    public int SlotDelBoss => slotBoss.Value;

    /// <summary>Atajo para no repetir el null-check de gameManager en todos lados.</summary>
    private bool PartidaTerminada => gameManager != null && gameManager.PartidaTerminada;

    /// <summary>
    /// Se dispara SOLO en el servidor, cuando el turno le llega al boss.
    /// BossManager se suscribe a esto para jugar su turno automaticamente -
    /// TurnManager no conoce a BossManager, solo avisa que "le toca a alguien
    /// que resulta ser el boss".
    /// </summary>
    public event Action OnBossTurnStarted;

        /// <summary>
    /// A diferencia de OnBossTurnStarted (que SOLO se dispara en el
    /// servidor), este se dispara en TODOS los clientes cada vez que cambia
    /// el turno o el estado - lo usa BossManager para saber, del lado de
    /// cualquier cliente, cuándo recalcular qué sprite mostrar (turno propio
    /// vs. estado de atención).
    /// </summary>
    public event Action OnEstadoTurnoCambio;

    public override void OnNetworkSpawn()
{
    turnoActual.OnValueChanged += (anterior, nuevo) =>
    {
        ActualizarHighlighter();
        OnEstadoTurnoCambio?.Invoke();
    };

    estadoActual.OnValueChanged += (anterior, nuevo) =>
    {
        ActualizarMensaje();
        ActualizarHighlighter();
        OnEstadoTurnoCambio?.Invoke();

        if (nuevo == TurnState.WaitingToDraw && EsMiTurno())
        {
            NotifyTurn?.Play();
        }
    };

    slotBoss.OnValueChanged += (anterior, nuevo) =>
    {
        ActualizarMensaje();
        ActualizarHighlighter();
        OnEstadoTurnoCambio?.Invoke();
    };

    if (gameManager != null)
    {
        gameManager.OnResultadoCambio += (nuevo) =>
        {
            ActualizarMensaje();
            ActualizarHighlighter();
            OnEstadoTurnoCambio?.Invoke();
        };
    }

    if (infiltratedCardManager != null)
    {
        infiltratedCardManager.OnCategoriaInfiltradaElegida += ActualizarCategoriaInfiltrada;

        // Por si este cliente se conecta/reactiva DESPUES de que ya se
        // eligio la categoria esta ronda - no depender solo del evento.
        ActualizarCategoriaInfiltrada(infiltratedCardManager.HayCategoriaInfiltrada
            ? (int)infiltratedCardManager.CategoriaInfiltrada
            : -1);
    }

    ActualizarMensaje();
    ActualizarHighlighter();
}

    /// <summary>
    /// SOLO debe llamarse desde el servidor (DeckManager, al presionar
    /// "Iniciar partida"). Arranca el turno en el slot 0 (el host) y sortea
    /// la regla de victoria de esta ronda.
    /// </summary>
    public void IniciarPrimerTurno(int totalJugadoresConectados)
    {
        if (!IsServer) return;

        cantidadJugadores = Mathf.Max(1, totalJugadoresConectados);
        turnoActual.Value = 0;
        gameManager?.ReiniciarResultado();
        slotBoss.Value = cantidadJugadores; // el boss va justo despues del ultimo humano
        indiceReglaActual.Value = SortearIndiceDeRegla();
        estadoActual.Value = TurnState.WaitingToDraw;

        ActualizarCartaInfiltradaSegunRegla();

        Debug.Log($"[Servidor] Regla de esta ronda: {VictoryRules.ObtenerNombre(ReglaActiva)}");
    }

    /// <summary>
    /// Normalmente sortea al azar entre VictoryRules.ReglasDisponibles.
    /// Si 'usarReglaFijaParaPruebas' está activo, en cambio devuelve siempre
    /// el indice de 'reglaParaPruebas' - util para probar la logica del
    /// boss (o la propia regla) sin depender de que salga por sorteo.
    /// </summary>
    private int SortearIndiceDeRegla()
    {
        if (!usarReglaFijaParaPruebas)
        {
            return UnityEngine.Random.Range(0, VictoryRules.ReglasDisponibles.Length);
        }

        int indice = System.Array.IndexOf(VictoryRules.ReglasDisponibles, reglaParaPruebas);

        if (indice < 0)
        {
            Debug.LogError($"[TurnManager] La regla '{reglaParaPruebas}' no está en VictoryRules.ReglasDisponibles " +
                "- agregala ahí (descomentala si está comentada) para poder forzarla en pruebas. " +
                "Usando sorteo aleatorio en su lugar por esta vez.");
            return UnityEngine.Random.Range(0, VictoryRules.ReglasDisponibles.Length);
        }

        return indice;
    }

    /// <summary>
    /// SOLO servidor. Si la regla sorteada esta ronda es CartaInfiltrada o
    /// CartaInfiltrada2, elige la carta ahora (se sincroniza sola a todos
    /// via InfiltratedCardManager). Si no, se asegura de que quede en -1,
    /// para que el panel se mantenga apagado el resto de la ronda.
    /// </summary>
    private void ActualizarCartaInfiltradaSegunRegla()
    {
        if (infiltratedCardManager == null) return;

        bool necesitaCartaInfiltrada = ReglaActiva == VictoryRuleType.CartaInfiltrada
                                     || ReglaActiva == VictoryRuleType.CartaInfiltrada2;

        if (necesitaCartaInfiltrada)
        {
            infiltratedCardManager.ElegirCategoriaInfiltrada();
        }
        else
        {
            infiltratedCardManager.ReiniciarCategoriaInfiltrada();
        }
    }

    /// <summary>¿El turno actual le pertenece a este slot?</summary>
    public bool EsTurnoDelSlot(int slot)
    {
        return !PartidaTerminada && estadoActual.Value != TurnState.Dealing && turnoActual.Value == slot;
    }

    /// <summary>¿Es mi propio turno, en este cliente?</summary>
    public bool EsMiTurno()
    {
        return EsTurnoDelSlot(PlayerCube.MiSlot);
    }

    /// <summary>¿Le toca al boss? Válido para todos (slotBoss está sincronizado).</summary>
    public bool EsTurnoDelBoss()
    {
        return slotBoss.Value >= 0 && EsTurnoDelSlot(slotBoss.Value);
    }

    /// <summary>
    /// ¿Ese slot puede pedir un intercambio ahora mismo? A diferencia de
    /// CanDiscard()/EsMiTurno(), esto NO depende de PlayerCube.MiSlot (que
    /// solo es valido para "este" cliente) - por eso el SERVIDOR puede
    /// llamarlo pasando el slot del cliente que hizo el pedido, para
    /// validar a cualquiera, no solo a si mismo. La ventana es la misma que
    /// CanDiscard(): ya robaste, todavia no descartaste.
    /// </summary>
    public bool PuedeSolicitarIntercambio(int slot)
    {
        return EsTurnoDelSlot(slot) && estadoActual.Value == TurnState.WaitingToDiscard;
    }

    public bool CanDraw()
    {
        return EsMiTurno() && estadoActual.Value == TurnState.WaitingToDraw && handManager.GetCardCount() == 4;
    }

    public bool CanDiscard()
    {
        return EsMiTurno() && estadoActual.Value == TurnState.WaitingToDiscard && handManager.GetCardCount() == 5;
    }

    /// <summary>SOLO el servidor llama esto, despues de validar un robo.</summary>
    public void NotificarRoboRealizado()
    {
        if (!IsServer) return;
        estadoActual.Value = TurnState.WaitingToDiscard;
    }

    /// <summary>SOLO el servidor llama esto, despues de validar un descarte - avanza el turno.</summary>
    public void NotificarDescarteRealizado()
    {
        if (!IsServer) return;

        // +1 para incluir al boss en la rotacion: si hay N jugadores humanos
        // (slots 0..N-1), el boss ocupa el slot N, y el ciclo completo es
        // sobre N+1 posiciones en total.
        turnoActual.Value = (turnoActual.Value + 1) % (cantidadJugadores + 1);
        estadoActual.Value = TurnState.WaitingToDraw;

        if (EsTurnoDelBoss())
        {
            OnBossTurnStarted?.Invoke();
        }
    }

    /// <summary>
    /// Puramente local (no toca ninguna NetworkVariable) - lo llama
    /// GameRestartManager cuando ESTE cliente vuelve a jugar individualmente,
    /// mientras espera a que el host arranque la ronda nueva. Se sobreescribe
    /// solo apenas vuelva a cambiar algo real (turno, regla, resultado, etc.).
    /// </summary>
    public void MostrarMensajeEsperando()
    {
        if (turnMessage != null)
        {
            turnMessage.text = "Esperando jugadores...";
        }
    }

    private void ActualizarMensaje()
    {
        string mensaje;
        string descripcion = string.Empty;

        if (PartidaTerminada)
        {
            // El panel correspondiente (GameManager) ya muestra el
            // resultado - este texto de "regla activa" deja de tener sentido.
            mensaje = string.Empty;
        }
        else if (estadoActual.Value == TurnState.Dealing)
        {
            mensaje = "Repartiendo cartas...";
        }
        else
        {
            mensaje = $"Regla: {VictoryRules.ObtenerNombre(ReglaActiva)}";
            descripcion = VictoryRules.ObtenerDescripcion(ReglaActiva);
        }

        if (turnMessage != null)
        {
            turnMessage.text = mensaje;
        }

        if (turnRuleDescriptionText != null)
        {
            turnRuleDescriptionText.text = descripcion;
        }
    }

    /// <summary>
    /// Mueve el highlighter a la posicion fija del slot en turno (o a la
    /// posicion fija del boss, si le toca a el) - se oculta solo mientras
    /// se reparte o cuando la partida ya termino.
    /// </summary>
    private void ActualizarHighlighter()
    {
        if (turnHighlighter == null)
        {
            Debug.LogWarning("[TurnManager] 'Turn Highlighter' no está asignado en el Inspector - el prefab del turno nunca se va a mostrar hasta que se arrastre la referencia.");
            return;
        }

        bool debeMostrarse = !PartidaTerminada && estadoActual.Value != TurnState.Dealing;

        turnHighlighter.gameObject.SetActive(debeMostrarse);

        if (!debeMostrarse) return;

        if (EsTurnoDelBoss())
        {
            turnHighlighter.anchoredPosition = posicionHighlighterBoss;
            return;
        }

        int slot = turnoActual.Value;

        if (slot < 0 || slot >= posicionesHighlighterPorSlot.Length)
        {
            Debug.LogWarning($"[TurnManager] No hay posicion configurada para el slot {slot} en posicionesHighlighterPorSlot.");
            return;
        }

        turnHighlighter.anchoredPosition = posicionesHighlighterPorSlot[slot];
    }

    /// <summary>
    /// Prende el panel con el icono de la categoria infiltrada cuando hay
    /// una elegida (index >= 0), lo apaga si no (-1 = regla distinta esta
    /// ronda, o todavia no se sorteo nada). Se llama tanto por el evento de
    /// InfiltratedCardManager como al conectarse a mitad de ronda.
    /// </summary>
    private void ActualizarCategoriaInfiltrada(int categoriaIndex)
    {
        if (panelCartaInfiltrada == null) return;

        if (categoriaIndex < 0)
        {
            panelCartaInfiltrada.SetActive(false);
            return;
        }

        CardCategory categoria = (CardCategory)categoriaIndex;
        Sprite icono = ObtenerIconoDeCategoriaInfiltrada(categoria);

        if (icono == null)
        {
            Debug.LogWarning($"[TurnManager] No hay icono configurado para la categoria infiltrada {categoria}.");
            return;
        }

        if (iconoCartaInfiltrada != null) iconoCartaInfiltrada.sprite = icono;
        panelCartaInfiltrada.SetActive(true);
    }

    private Sprite ObtenerIconoDeCategoriaInfiltrada(CardCategory categoria)
    {
        if (iconosPorCategoriaInfiltrada == null) return null;

        foreach (var entrada in iconosPorCategoriaInfiltrada)
        {
            if (entrada.categoria == categoria)
            {
                return entrada.icono;
            }
        }

        return null;
    }
}