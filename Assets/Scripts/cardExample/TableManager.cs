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
    }
}