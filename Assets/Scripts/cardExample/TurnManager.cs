using TMPro;
using UnityEngine;

public enum TurnState
{
    Dealing,
    WaitingToDraw,
    WaitingToDiscard,
    TurnFinished
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
        return currentState == TurnState.WaitingToDraw  && handManager.GetCardCount() == 4;
        // Para poder robar se deben cumplir 2 condiciones
    }

    public void CardWasDrawn()
    {
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
        return currentState == TurnState.WaitingToDiscard && handManager.GetCardCount() == 5;
    }

    public void CardWasDiscarded()
    {
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
    }

    private void ChangeState(TurnState newState)
    {
        currentState = newState;

        switch (currentState)
        {
            case TurnState.Dealing:
                ShowMessage("Repartiendo cartas...");
                break;

            case TurnState.WaitingToDraw:
                ShowMessage("Tu turno: roba una carta.");
                break;

            case TurnState.WaitingToDiscard:
                ShowMessage("Ahora descarta una carta.");
                break;

            case TurnState.TurnFinished:
                ShowMessage("Turno finalizado.");
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