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
/// De quien es el turno (por slot: 0=host, 1=invitado1, 2=invitado2) y en
/// que parte de su turno esta (robar/descartar), sincronizado a todos.
///
/// El SERVIDOR es quien decide y avanza el turno (via IniciarPrimerTurno,
/// NotificarRoboRealizado, NotificarDescarteRealizado, todos llamados desde
/// DeckManager despues de validar cada accion). Los clientes solo leen el
/// estado sincronizado para saber si pueden actuar.
/// </summary>
public class TurnManager : NetworkBehaviour
{
    [Header("Referencias")]
    [SerializeField] private HandManager handManager;
    [SerializeField] private TMP_Text turnMessage;

    private readonly NetworkVariable<int> turnoActual = new NetworkVariable<int>(0);
    private readonly NetworkVariable<TurnState> estadoActual = new NetworkVariable<TurnState>(TurnState.Dealing);

    // Indice dentro de VictoryRules.ReglasDisponibles - sorteado por el
    // servidor al iniciar la partida, sincronizado para que todos sepan
    // que regla esta activa esta ronda.
    private readonly NetworkVariable<int> indiceReglaActual = new NetworkVariable<int>(0);

    // -1 = todavia nadie ha ganado.
    private readonly NetworkVariable<int> slotGanador = new NetworkVariable<int>(-1);

    // El boss siempre ocupa el slot inmediatamente despues del ultimo
    // jugador humano (si hay 2 jugadores conectados, slots 0 y 1, el boss
    // es el slot 2) - sincronizado para que todos los clientes puedan
    // mostrar "Turno del boss" correctamente, no solo el servidor.
    // -1 = todavia no se definio (partida no iniciada).
    private readonly NetworkVariable<int> slotBoss = new NetworkVariable<int>(-1);

    private int cantidadJugadores = 1;

    public VictoryRuleType ReglaActiva => VictoryRules.ReglasDisponibles[indiceReglaActual.Value];

    public bool PartidaTerminada => slotGanador.Value != -1;

    public int SlotDelBoss => slotBoss.Value;

    /// <summary>
    /// Se dispara SOLO en el servidor, cuando el turno le llega al boss.
    /// BossManager se suscribe a esto para jugar su turno automaticamente -
    /// TurnManager no conoce a BossManager, solo avisa que "le toca a alguien
    /// que resulta ser el boss".
    /// </summary>
    public event Action OnBossTurnStarted;

    public override void OnNetworkSpawn()
    {
        turnoActual.OnValueChanged += (anterior, nuevo) => ActualizarMensaje();
        estadoActual.OnValueChanged += (anterior, nuevo) => ActualizarMensaje();
        slotGanador.OnValueChanged += (anterior, nuevo) => ActualizarMensaje();
        slotBoss.OnValueChanged += (anterior, nuevo) => ActualizarMensaje();

        ActualizarMensaje();
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
        slotGanador.Value = -1;
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

    public bool CanDraw()
    {
        return EsMiTurno() && estadoActual.Value == TurnState.WaitingToDraw && handManager.GetCardCount() == 4;
    }

    public bool CanDiscard()
    {
        return EsMiTurno() && estadoActual.Value == TurnState.WaitingToDiscard && handManager.GetCardCount() == 5;
    }

    /// <summary>
    /// SOLO el servidor llama esto, cuando DeckManager detecta (despues de
    /// un descarte) que la mano de un jugador cumple la regla activa.
    /// </summary>
    public void DeclararGanador(int slot)
    {
        if (!IsServer || PartidaTerminada) return;

        slotGanador.Value = slot;

        Debug.Log($"[Servidor] ¡Slot {slot} ganó cumpliendo la regla '{VictoryRules.ObtenerNombre(ReglaActiva)}'!");
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
        if (PartidaTerminada)
        {
            string quienGano;

            if (slotGanador.Value == slotBoss.Value)
            {
                quienGano = "¡Ganó el Boss!";
            }
            else
            {
                quienGano = slotGanador.Value == PlayerCube.MiSlot ? "¡Ganaste tú!" : $"Ganó el jugador {slotGanador.Value}.";
            }

            string mensajeFinal = $"{quienGano} Regla: {VictoryRules.ObtenerNombre(ReglaActiva)}.";

            Debug.Log("Partida terminada: " + mensajeFinal);

            if (turnMessage != null)
            {
                turnMessage.text = mensajeFinal;
            }

            return;
        }

        string mensaje;

        switch (estadoActual.Value)
        {
            case TurnState.Dealing:
                mensaje = "Repartiendo cartas...";
                break;

            case TurnState.WaitingToDraw:
                if (EsTurnoDelBoss())
                {
                    mensaje = $"Turno del boss: robando... (Regla: {VictoryRules.ObtenerNombre(ReglaActiva)})";
                }
                else
                {
                    mensaje = EsMiTurno()
                        ? $"Tu turno: roba una carta. (Regla: {VictoryRules.ObtenerNombre(ReglaActiva)})"
                        : $"Turno del jugador {turnoActual.Value}: esperando a que robe.";
                }
                break;

            case TurnState.WaitingToDiscard:
                if (EsTurnoDelBoss())
                {
                    mensaje = $"Turno del boss: descartando... (Regla: {VictoryRules.ObtenerNombre(ReglaActiva)})";
                }
                else
                {
                    mensaje = EsMiTurno()
                        ? $"Ahora descarta una carta. (Regla: {VictoryRules.ObtenerNombre(ReglaActiva)})"
                        : $"Turno del jugador {turnoActual.Value}: esperando a que descarte.";
                }
                break;

            default:
                mensaje = "";
                break;
        }

        Debug.Log("Estado del turno: " + estadoActual.Value + " | Slot con el turno: " + turnoActual.Value);

        if (turnMessage != null)
        {
            turnMessage.text = mensaje;
        }
    }
}