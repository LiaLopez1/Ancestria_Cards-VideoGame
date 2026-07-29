using UnityEngine;

/// <summary>
/// Mesa de descarte compartida y publica: todos los jugadores deben ver
/// exactamente las mismas cartas aqui.
///
/// IMPORTANTE: ya no se le pasa la carta que el jugador arrastro (esa era
/// una idea que solo tenia sentido para un cliente local unico). Ahora
/// AgregarCartaDescartada crea una copia visual NUEVA a partir del CardData -
/// es DeckManager quien llama a este metodo, en TODOS los clientes por
/// igual, cuando el servidor confirma un descarte valido.
/// </summary>
public class TableManager : MonoBehaviour
{
    [Header("Contenedor de cartas jugadas")]
    [SerializeField] private RectTransform tableCards;

    [Header("Prefab de la carta visible en la mesa")]
    [Tooltip("Debe tener un CardDisplay, igual que el prefab de la mano (la carta se ve boca arriba en la mesa).")]
    [SerializeField] private GameObject tableCardPrefab;

    [Header("Variación visual")]
    [SerializeField] private float positionRangeX = 25f;
    [SerializeField] private float positionRangeY = 5f;
    [SerializeField] private float rotationRangeZ = 8f;

    [Header("Audio")]
    [SerializeField] private SoundData discardCardSound;

    public void AgregarCartaDescartada(CardData carta)
    {
        if (tableCardPrefab == null)
        {
            Debug.LogError("[TableManager] Falta asignar el Table Card Prefab.");
            return;
        }

        GameObject cartaVisual = Instantiate(tableCardPrefab, tableCards);
        cartaVisual.name = "TableCard - " + carta.cardName;

        CardDisplay display = cartaVisual.GetComponent<CardDisplay>();

        if (display == null)
        {
            Debug.LogError("El prefab de la mesa necesita un CardDisplay.");
            Destroy(cartaVisual);
            return;
        }

        display.card = carta;

        RectTransform cardRect = cartaVisual.GetComponent<RectTransform>();

        if (cardRect == null)
        {
            Debug.LogError("El prefab de la mesa necesita RectTransform.");
            Destroy(cartaVisual);
            return;
        }

        cardRect.SetParent(tableCards, true);
        cardRect.SetAsLastSibling();

        Vector2 randomPosition = new Vector2(
            Random.Range(-positionRangeX, positionRangeX),
            Random.Range(-positionRangeY, positionRangeY)
        );

        float randomRotation = Random.Range(-rotationRangeZ, rotationRangeZ);

        cardRect.anchoredPosition = randomPosition;
        cardRect.localRotation = Quaternion.Euler(0f, 0f, randomRotation);
        cardRect.localScale = Vector3.one;

        DesactivarInteraccion(cartaVisual);

        discardCardSound.Play();
    }

    /// <summary>
    /// Vacía la mesa de descarte visualmente. Lo llama DeckManager en TODOS
    /// los clientes cuando el mazo se queda sin cartas y la pila de
    /// descarte se recicla de vuelta al mazo - esas cartas ya no están en
    /// la mesa, así que hay que sacarlas de pantalla.
    /// </summary>
    public void LimpiarMesa()
    {
        if (tableCards == null)
        {
            return;
        }

        for (int i = tableCards.childCount - 1; i >= 0; i--)
        {
            Destroy(tableCards.GetChild(i).gameObject);
        }
    }

    /// <summary>
    /// El prefab de la mesa es el mismo que el de la mano, asi que trae
    /// componentes de arrastre/interaccion que ya no tienen sentido aqui -
    /// se desactivan para que la carta quede solo visual, sin poder
    /// arrastrarse ni generar errores al intentarlo.
    /// </summary>
    private void DesactivarInteraccion(GameObject cartaVisual)
    {
        CardDragHandler dragHandler = cartaVisual.GetComponent<CardDragHandler>();
        if (dragHandler != null)
        {
            dragHandler.enabled = false;
        }

        CardInteraction interaccion = cartaVisual.GetComponent<CardInteraction>();
        if (interaccion != null)
        {
            interaccion.enabled = false;
        }

        CanvasGroup canvasGroup = cartaVisual.GetComponent<CanvasGroup>();
        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = false;
        }
    }
}