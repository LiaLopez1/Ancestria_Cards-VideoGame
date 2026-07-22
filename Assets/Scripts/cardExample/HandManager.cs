using System.Collections.Generic;
using UnityEngine;

public class HandManager : MonoBehaviour
{
    [Header("Creación de cartas")]
    [SerializeField] private GameObject cardPrefab;
    [SerializeField] private List<CardSlot> cardSlots = new List<CardSlot>();

    [Header("Forma de la mano")]
    [SerializeField] private float spacing = 130f;
    [SerializeField] private float curveHeight = 25f;
    [SerializeField] private float maxRotation = 10f;



    private RectTransform handRectTransform;
    private CardSlot draggedSlot;

    private void Awake()
    {
        handRectTransform = GetComponent<RectTransform>();
    }

    private void Start()
    {
        ArrangeHand();
    }

    public int GetCardCount()
    {
        int cardCount = 0;

        foreach (CardSlot slot in cardSlots)
        {
            if (slot != null && slot.transform.childCount > 0)
            {
                cardCount++;
            }
        }

        return cardCount;
    }

//Funcion para saber si hay espacio
    public bool HasEmptySlot()
    {
        foreach (CardSlot slot in cardSlots)
        {
            if (slot != null && slot.RectTransform.childCount == 0)
            {
                return true;
            }
        }

        return false;
    }

    public void ArrangeHand()
    {
        List<CardSlot> visibleSlots = GetVisibleSlots();

        int count = visibleSlots.Count;

        if (count == 0)
            return;

        float center = (count - 1) / 2f;

        for (int i = 0; i < count; i++)
        {
            float offset = i - center;

            float x = offset * spacing;
            float y = -Mathf.Abs(offset) * curveHeight;
            float rotationZ = -offset * maxRotation;


            CardSlot slot = visibleSlots[i];

        // Actaulizamos el orden visual en la jerarquia
            slot.transform.SetSiblingIndex(i);

            slot.SetTarget( new Vector2(x, y), rotationZ);


            /*visibleSlots[i].SetTarget(     
                new Vector2(x, y),
                rotationZ
            );
            este es para actualizar el slot solo en juego, no en la jerarquia
            esto hace que debido a la posición en la jerarquia se vean unas enciam de otras y no en orden*/
        }
    }

    public bool AddCardToHand(CardData cardData)
    {
        if (cardData == null)
        {
            Debug.LogWarning("No se recibió información para crear la carta.");
            return false;
        }

        foreach (CardSlot slot in cardSlots)
        {
            if (slot == null || slot.RectTransform.childCount > 0)
            {
                continue;
            }

            GameObject newCard = Instantiate(cardPrefab, slot.RectTransform);

            newCard.name = "Card - " + cardData.cardName;

            RectTransform cardRect = newCard.GetComponent<RectTransform>();

            if (cardRect != null)
            {
                cardRect.anchoredPosition = Vector2.zero;
                cardRect.localRotation = Quaternion.identity;
                cardRect.localScale = Vector3.one;
            }

            CardDisplay cardDisplay = newCard.GetComponent<CardDisplay>();

            if (cardDisplay == null)
            {
                Debug.LogError( "El prefab Card no contiene el componente CardDisplay.");

                Destroy(newCard);
                return false;
            }

            cardDisplay.card= cardData;

            Debug.Log("Carta agregada a la mano: " + cardData.cardName);
            ArrangeHand();

            return true;
        }

        Debug.LogWarning("No quedan espacios vacíos en la mano.");
        return false;
    }

   public void BeginCardDrag(CardSlot slot)
    {
        

        draggedSlot = slot;
        ArrangeHand();
    }

    public int GetInsertionIndex(Vector2 screenPosition,Camera eventCamera)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            handRectTransform,
            screenPosition,
            eventCamera,
            out Vector2 localPointerPosition
        );

        List<CardSlot> visibleSlots = GetVisibleSlots();

        for (int i = 0; i < visibleSlots.Count; i++)
        {
            float slotX = visibleSlots[i].RectTransform.anchoredPosition.x;

            if (localPointerPosition.x < slotX)
            {
                return i;
            }
        }

        return visibleSlots.Count;
    }
  
    public void CompleteCardDrag(CardSlot slot, int newIndex)
    {
        cardSlots.Remove(slot);

        newIndex = Mathf.Clamp(
            newIndex,
            0,
            cardSlots.Count
        );

        cardSlots.Insert(newIndex, slot);

        draggedSlot = null;

        ArrangeHand();
    }
    public void CancelCardDrag()
    {
        draggedSlot = null;
        ArrangeHand();
    }

    private List<CardSlot> GetVisibleSlots()
    {
        List<CardSlot> visibleSlots = new List<CardSlot>();

        foreach (CardSlot slot in cardSlots)
        {
            if (slot != draggedSlot)
            {
                visibleSlots.Add(slot);
            }
        }

        return visibleSlots;
    }

    public bool IsPointerInsideHand( Vector2 screenPosition,Camera eventCamera)
    {
        return RectTransformUtility.RectangleContainsScreenPoint(
            handRectTransform,
            screenPosition,
            eventCamera
        );
    }

    public void RemoveSolt(CardSlot slot)
    {
        if(slot == null)
        {
            return;
        }

        if (draggedSlot == slot)
        {
            draggedSlot = null;
        }

        if (slot.transform.childCount>0)
        {
            Transform card = slot.transform.GetChild(0);
            card.SetParent(null);
            Destroy(card.gameObject);
        }
        ArrangeHand();


    }





}
