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

    private int cantidadJugadores = 1;

    public override void OnNetworkSpawn()
    {
        turnoActual.OnValueChanged += (anterior, nuevo) => ActualizarMensaje();
        estadoActual.OnValueChanged += (anterior, nuevo) => ActualizarMensaje();

        ActualizarMensaje();
    }

    /// <summary>
    /// SOLO debe llamarse desde el servidor (DeckManager, al presionar
    /// "Iniciar partida"). Arranca el turno en el slot 0 (el host).
    /// </summary>
    public void IniciarPrimerTurno(int totalJugadoresConectados)
    {
        if (!IsServer) return;

        cantidadJugadores = Mathf.Max(1, totalJugadoresConectados);
        turnoActual.Value = 0;
        estadoActual.Value = TurnState.WaitingToDraw;
    }

    /// <summary>¿El turno actual le pertenece a este slot?</summary>
    public bool EsTurnoDelSlot(int slot)
    {
        return estadoActual.Value != TurnState.Dealing && turnoActual.Value == slot;
    }

    /// <summary>¿Es mi propio turno, en este cliente?</summary>
    public bool EsMiTurno()
    {
        return EsTurnoDelSlot(PlayerCube.MiSlot);
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

        turnoActual.Value = (turnoActual.Value + 1) % cantidadJugadores;
        estadoActual.Value = TurnState.WaitingToDraw;
    }

    private void ActualizarMensaje()
    {
        string mensaje;

        switch (estadoActual.Value)
        {
            case TurnState.Dealing:
                mensaje = "Repartiendo cartas...";
                break;

            case TurnState.WaitingToDraw:
                mensaje = EsMiTurno()
                    ? "Tu turno: roba una carta."
                    : $"Turno del jugador {turnoActual.Value}: esperando a que robe.";
                break;

            case TurnState.WaitingToDiscard:
                mensaje = EsMiTurno()
                    ? "Ahora descarta una carta."
                    : $"Turno del jugador {turnoActual.Value}: esperando a que descarte.";
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