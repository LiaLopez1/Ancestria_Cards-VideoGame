// Este va en el PlayDropeZone
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Detecta que soltaste una carta aqui, y le pide a CardDragHandler que
/// inicie el descarte. Ya no apunta directo a TableManager: el descarte
/// tiene que pasar por DeckManager (que es quien valida y avisa al
/// servidor), asi que le pasamos DeckManager en vez de TableManager.
/// </summary>
public class DropZone : MonoBehaviour, IDropHandler
{
    [SerializeField] private DeckManager deckManager;

    public void OnDrop(PointerEventData eventData)
    {
        Debug.Log("Dropzone detectó la carta.");

        CardDragHandler card = eventData.pointerDrag?.GetComponent<CardDragHandler>();

        if (card == null)
        {
            Debug.LogWarning("No se encontró CardDragHandler en la carta soltada.");
            return;
        }

        card.PlaceOnTable(deckManager);
    }
}