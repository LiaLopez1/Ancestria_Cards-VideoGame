//Este va en el PlayDropeZone
using UnityEngine;
using UnityEngine.EventSystems;

public class DropZone : MonoBehaviour, IDropHandler
{
    [SerializeField] private DeckManager deckManager;

    public void OnDrop(PointerEventData eventData)
    
    {

        Debug.Log("Dropzone Detecto la carta");

        
        CardDragHandler card = eventData.pointerDrag?.GetComponent<CardDragHandler>();

        if (card == null)
        {
            Debug.LogWarning("Carta encontrada" + card.name);
            return;
        }

        card.PlaceOnTable(deckManager);
    }
}