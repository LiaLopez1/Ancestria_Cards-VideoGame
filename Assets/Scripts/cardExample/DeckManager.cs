using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DeckManager : MonoBehaviour
{
    [Header("Reparto inicial")]
    [SerializeField] private int initialHandSize = 4;
    [SerializeField] private float delayBetweenCards = 0.25f;

private bool initialDealFinished;
    [Header("Configuración del mazo")]
    [SerializeField] private int copiesPerCard = 4;

    [Header("Representación visual")]
    [SerializeField] private RectTransform deckArea;
    [SerializeField] private GameObject deckCardPrefab;
    [SerializeField] private Vector2 cardOffset = new Vector2(0.25f, -0.25f);

    [Header("Turno")]
    [SerializeField] private TurnManager turnManager;

    [Header("Mano del jugador")]
    [SerializeField] private HandManager handManager;

    // Cartas disponibles para robar.
    private readonly List<CardData> drawPile = new List<CardData>();

    // Objetos que representan las cartas apiladas en pantalla.
    private readonly List<GameObject> visualDeck = new List<GameObject>();

    public int CardsRemaining
    {
        get { return drawPile.Count; }
    }

    private void Start()
    {
        BuildLogicalDeck();
        ShuffleDeck();
        BuildVisualDeck();
        StartCoroutine(DealInitialHand());
    }

    private void BuildLogicalDeck()
    {
        drawPile.Clear();

        foreach (CardData cardData in CardDatabase.Instance.ObtenerTodas())
        {
            if (cardData == null)
            {
                continue;
            }

            for (int copy = 0; copy < copiesPerCard; copy++)
            {
                drawPile.Add(cardData);
            }
        }

        Debug.Log("Mazo creado con " + drawPile.Count + " cartas.");
    }

    private void ShuffleDeck()
    {
        for (int i = drawPile.Count - 1; i > 0; i--)
        {
            int randomIndex = Random.Range(0, i + 1);

            CardData temporaryCard = drawPile[i];
            drawPile[i] = drawPile[randomIndex];
            drawPile[randomIndex] = temporaryCard;
        }

        Debug.Log("El mazo fue mezclado.");
    }

    private void BuildVisualDeck()
    {
        ClearVisualDeck();

        for (int i = 0; i < drawPile.Count; i++)
        {
            GameObject visualCard = Instantiate(deckCardPrefab,deckArea);
            visualCard.name = "DeckCard " + i + " - " + drawPile[i].cardName; // para saber herarquia cuantas se crean de un tipo
            DeckCardDrag deckCardDrag = visualCard.GetComponent<DeckCardDrag>();

            if (deckCardDrag != null)
            {
                deckCardDrag.Configure(this);
                deckCardDrag.enabled = false;
            }

            RectTransform cardRect =
                visualCard.GetComponent<RectTransform>();

            if (cardRect == null)
            {
                Debug.LogError(
                    "El prefab del mazo necesita RectTransform."
                );

                Destroy(visualCard);
                continue;
            }

            cardRect.anchoredPosition = cardOffset * i;
            cardRect.localRotation = Quaternion.identity;
            cardRect.localScale = Vector3.one;

            visualCard.transform.SetSiblingIndex(i);

            visualDeck.Add(visualCard);
        }

        RefreshTopCardDrag();
    }




    private void ClearVisualDeck()
    {
        foreach (GameObject visualCard in visualDeck)
        {
            if (visualCard != null)
            {
                Destroy(visualCard);
            }
        }
        visualDeck.Clear();
    }

    public CardData DrawCard()
    {
        if (drawPile.Count == 0)
        {
            Debug.LogWarning("No quedan cartas en el mazo.");
            return null;
        }

        int topCardIndex = drawPile.Count - 1;
        CardData drawnCard = drawPile[topCardIndex];
        drawPile.RemoveAt(topCardIndex);

        if (visualDeck.Count > 0)
        {
            int topVisualIndex = visualDeck.Count - 1;
            GameObject topVisualCard = visualDeck[topVisualIndex];
            visualDeck.RemoveAt(topVisualIndex);
            Destroy(topVisualCard);
            RefreshTopCardDrag();
        }

        

        Debug.Log(
            "Carta robada: " + drawnCard.name + " | Cartas restantes: " + drawPile.Count
        );

        return drawnCard;
    }

    /*[ContextMenu("Probar robo de una carta")]
    private void TestDrawCard()
    {
        if (handManager == null)
        {
            Debug.LogError("No se asignó el HandManager.");
            return;
        }
        if (!handManager.HasEmptySlot())
        {
            Debug.LogWarning("La mano ya tiene cinco cartas.");
            return;
        }
        CardData drawnCard = DrawCard();

        if (drawnCard != null)
        {
            handManager.AddCardToHand(drawnCard);
        }
    }*/

    private IEnumerator DealInitialHand()
    {
        initialDealFinished = false;

        for (int i = 0; i < initialHandSize; i++)
        {
            yield return new WaitForSeconds(delayBetweenCards);

            CardData drawnCard = DrawCard();

            if (drawnCard == null)
            {
                yield break;
            }

            bool cardAdded = handManager.AddCardToHand(drawnCard);

            if (!cardAdded)
            {
                Debug.LogError(
                    "No fue posible agregar una carta durante el reparto."
                );

                yield break;
            }
        }

        initialDealFinished = true;

        Debug.Log( "Reparto inicial terminado. El jugador tiene " +handManager.GetCardCount() + " cartas.");

        if (turnManager != null)
        {
            turnManager.InitialDealFinished();
        }
    }

    public bool CanStartManualDraw()
    {
        if (!initialDealFinished)
        {
            return false;
        }

        if (handManager == null)
        {
            return false;
        }

        if (drawPile.Count == 0)
        {
            return false;
        }

        return handManager.GetCardCount() == initialHandSize;
    }

    public bool TryManualDraw( Vector2 screenPosition,Camera eventCamera)
    {
        if (handManager == null)
        {
            Debug.LogError("No se asignó el HandManager.");
            return false;
        }

        bool pointerInsideHand = handManager.IsPointerInsideHand(screenPosition, eventCamera
        );

        if (!pointerInsideHand)
        {
            return false;
        }

        if (!CanStartManualDraw())
        {
            Debug.LogWarning("No puedes robar ahora. Debes tener exactamente 4 cartas.");
            return false;
        }
        CardData drawnCard = DrawCard();

        if (drawnCard == null)
        {
            return false;
        }

        bool cardAdded = handManager.AddCardToHand(drawnCard);

        if (!cardAdded)
        {
            Debug.LogError("La carta fue robada, pero no pudo agregarse a la mano.");
            return false;
        }

        Debug.Log("Robo manual completado: " + drawnCard.cardName);
        return true;
    }

    private void RefreshTopCardDrag()
    {
        for (int i = 0; i < visualDeck.Count; i++)
        {
            GameObject visualCard = visualDeck[i];

            if (visualCard == null)
            {
                continue;
            }

            DeckCardDrag drag =
                visualCard.GetComponent<DeckCardDrag>();

            if (drag == null)
            {
                continue;
            }

            drag.Configure(this);

            bool isTopCard = i == visualDeck.Count - 1;

            drag.enabled = isTopCard;
        }
    }

    public bool CanDrawCard()
    //Comprueba si el jugador puede robar una carta
    {
        if (handManager == null)
        {
            return false;
        }

        if (drawPile.Count == 0)
        {
            return false;
        }

        return handManager.GetCardCount() == 4;
    }

    public bool TryDrawCardToHand( Vector2 screenPosition, Camera eventCamera)
    {

        if (turnManager == null)
        {
            Debug.LogError("No se asignó el TurnManager.");
            return false;
        }

        if (!turnManager.CanDraw())
        {
            Debug.Log( "No puedes robar una carta en este momento.");

            return false;
        }

        if (handManager == null)
        {
            Debug.LogError("No se asignó el HandManager.");
            return false;
        }

        bool isInsideHand = handManager.IsPointerInsideHand( screenPosition,eventCamera);

        if (!isInsideHand)
        {
            Debug.Log("La carta se soltó fuera de la mano.");
            return false;
        }

        if (handManager.GetCardCount() != 4)
        {
            Debug.LogWarning("No puedes robar: debes tener exactamente 4 cartas.");
            return false;
        }

        if (!handManager.HasEmptySlot()) //confirmamos que haya un slot libre
        {
            Debug.LogWarning("No existe un slot vacío para recibir la carta.");
            return false;
        }

        CardData drawnCard = DrawCard();

        if (drawnCard == null)
        {
            Debug.LogWarning("No fue posible robar una carta.");
            return false;
        }
        //Creamos la carta jugable dentro de la mano
        bool wasAdded = handManager.AddCardToHand(drawnCard);

        if (!wasAdded)
        {
            Debug.LogError("Se robó la carta, pero no se pudo agregar a la mano.");
            return false;
        }

        turnManager.CardWasDrawn();

        Debug.Log(
            "Carta robada correctamente: " + drawnCard.cardName);

        return true;
    }

    
}