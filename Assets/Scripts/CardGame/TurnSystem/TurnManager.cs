using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;

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
        indiceReglaActual.Value = UnityEngine.Random.Range(0, VictoryRules.ReglasDisponibles.Length);
        estadoActual.Value = TurnState.WaitingToDraw;

        Debug.Log($"[Servidor] Regla de esta ronda: {VictoryRules.ObtenerNombre(ReglaActiva)}");
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

    private void ActualizarMensaje()
    {
        string mensaje;

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
        }

        if (turnMessage != null)
        {
            turnMessage.text = mensaje;
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
}