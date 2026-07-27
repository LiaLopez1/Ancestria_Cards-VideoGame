using System;
using TMPro;
using UnityEngine;

public enum TurnState
{
    Dealing,
    WaitingToDraw,
    WaitingToDiscard,
    TurnFinished
}

// Prototipo: solo distingue si el turno actual es del jugador o del boss.
// Todavía no hay slots (0/1/2) porque el juego real de esta escena es de 1 jugador.
public enum TurnOwner
{
    Player,
    Boss
}

public class TurnManager : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private HandManager handManager;
    [SerializeField] private TMP_Text turnMessage;

    private TurnState currentState;

    public TurnState CurrentState
    {
        get { return currentState; }
    }

    public TurnOwner CurrentOwner
    {
        get { return currentOwner; }
    }

    /// <summary>
    /// Se dispara justo cuando el jugador termina su turno (después de
    /// descartar). BossManager se suscribe a esto para arrancar su propio
    /// turno automáticamente, sin que TurnManager necesite conocer a
    /// BossManager por referencia directa.
    /// </summary>
    public event Action OnPlayerTurnFinished;

    private void Start()
    {
        ChangeState(TurnState.Dealing);
    }

    public void InitialDealFinished()
    {
        if (handManager.GetCardCount() != 4)
        {
            Debug.LogWarning(
                "El reparto terminó, pero la mano no tiene 4 cartas."
            );

            return;
        }

        ChangeState(TurnState.WaitingToDraw);
    }

    public bool CanDraw()
    {
        return currentOwner == TurnOwner.Player && currentState == TurnState.WaitingToDraw && handManager.GetCardCount() == 4;
        // Para poder robar se deben cumplir 3 condiciones: que sea turno del jugador,
        // que el estado sea el correcto, y que la mano tenga 4 cartas.
    }

    public void CardWasDrawn()
    {
        if (currentOwner != TurnOwner.Player)
        {
            Debug.LogWarning("CardWasDrawn() es solo para el jugador. Usa BossCardWasDrawn() para el boss.");
            return;
        }

        if (currentState != TurnState.WaitingToDraw)
        {
            Debug.LogWarning("El robo no corresponde al estado actual.");
            return;
        }

        if (handManager.GetCardCount() != 5)
        {
            Debug.LogWarning(
                "Después de robar, la mano debería tener 5 cartas."
            );
            return;
        }

        ChangeState(TurnState.WaitingToDiscard);
    }

    public bool CanDiscard()
    {
        return currentOwner == TurnOwner.Player && currentState == TurnState.WaitingToDiscard && handManager.GetCardCount() == 5;
    }

    public void CardWasDiscarded()
    {
        if (currentOwner != TurnOwner.Player)
        {
            Debug.LogWarning("CardWasDiscarded() es solo para el jugador. Usa BossCardWasDiscarded() para el boss.");
            return;
        }

        if (currentState != TurnState.WaitingToDiscard)
        {
            Debug.LogWarning( "El descarte no corresponde al estado actual.");
            return;
        }

        if (handManager.GetCardCount() != 4)
        {
            Debug.LogWarning( "Después de descartar, la mano debería tener 4 cartas.");
            return;
        }
        ChangeState(TurnState.TurnFinished);
        OnPlayerTurnFinished?.Invoke();
    }
    // Son métodos hermanos de los de arriba, pero SIN depender de handManager
    // (el boss no tiene una mano visual/UI, solo una lista interna en BossManager).

    /// <summary>
    /// Le pasa el control del turno al boss. Lo llama BossManager cuando
    /// detecta que el turno del jugador terminó (TurnFinished).
    /// </summary>
    public void StartBossTurn()
    {
        currentOwner = TurnOwner.Boss;
        ChangeState(TurnState.WaitingToDraw);
    }

    public bool BossCanDraw()
    {
        return currentOwner == TurnOwner.Boss && currentState == TurnState.WaitingToDraw;
    }

    public void BossCardWasDrawn()
    {
        if (!BossCanDraw())
        {
            Debug.LogWarning("El robo del boss no corresponde al estado actual.");
            return;
        }

        ChangeState(TurnState.WaitingToDiscard);
    }

    public bool BossCanDiscard()
    {
        return currentOwner == TurnOwner.Boss && currentState == TurnState.WaitingToDiscard;
    }

    public void BossCardWasDiscarded()
    {
        if (!BossCanDiscard())
        {
            Debug.LogWarning("El descarte del boss no corresponde al estado actual.");
            return;
        }

        ChangeState(TurnState.TurnFinished);
    }

    /// <summary>
    /// Devuelve el control al jugador. Lo llama BossManager una vez terminó
    /// su turno (después de BossCardWasDiscarded).
    /// </summary>
    public void EndBossTurn()
    {
        currentOwner = TurnOwner.Player;
        ChangeState(TurnState.WaitingToDraw);
    }

    private void ChangeState(TurnState newState)
    {
        currentState = newState;

        bool esTurnoDelBoss = currentOwner == TurnOwner.Boss;

        switch (currentState)
        {
            case TurnState.Dealing:
                ShowMessage("Repartiendo cartas...");
                break;

            case TurnState.WaitingToDraw:
                ShowMessage(esTurnoDelBoss ? "Turno del boss: robando..." : "Tu turno: roba una carta.");
                break;

            case TurnState.WaitingToDiscard:
                ShowMessage(esTurnoDelBoss ? "Turno del boss: descartando..." : "Ahora descarta una carta.");
                break;

            case TurnState.TurnFinished:
                ShowMessage(esTurnoDelBoss ? "El boss terminó su turno." : "Turno finalizado.");
                break;
        }

        Debug.Log("Estado del turno: " + currentState);
    }

    private void ShowMessage(string message)
    {
        Debug.Log(message);

        if (turnMessage != null)
        {
            turnMessage.text = message;
        }
    }
}