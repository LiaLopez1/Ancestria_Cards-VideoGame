using System.Collections.Generic;
using UnityEngine;

public class HandManager : MonoBehaviour
{
    [Header("Creación de cartas")]
    [SerializeField] private GameObject cardPrefab;
    [SerializeField] private List<CardSlot> cardSlots = new List<CardSlot>();
    private List<CardSlot> activeSlots = new List<CardSlot>();
    private CardSlot freeSlot;

    [Header("Forma de la mano")]
    [SerializeField] private float spacing = 130f;
    [SerializeField] private float curveHeight = 25f;
    [SerializeField] private float maxRotation = 10f;



    private RectTransform handRectTransform;
    private CardSlot draggedSlot;

    private void Awake()
    {
        handRectTransform = GetComponent<RectTransform>();

        activeSlots.Clear();

        foreach (CardSlot slot in cardSlots)
        {
            if (slot == null)
            {
                continue;
            }

            if (slot.transform.childCount > 0)
            {
                activeSlots.Add(slot);
            }
        }

        UpdateFreeSlot();
    }

    private void Start()
    {
        ArrangeHand();
    }

    public int GetCardCount()
    {
       return activeSlots.Count;
    }

//Funcion para saber si hay espacio
    public bool HasEmptySlot()
    {
        return freeSlot != null;
        //me dice, si hay una slot libre= hay espacio;
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
            Debug.LogWarning(
                "No se recibió información para crear la carta."
            );

            return false;
        }

        if (freeSlot == null)
        {
            UpdateFreeSlot();
        }

        if (freeSlot == null)
        {
            Debug.LogWarning(
                "No quedan espacios vacíos en la mano."
            );

            return false;
        }

        CardSlot destinationSlot = freeSlot;

        GameObject newCard = Instantiate(
            cardPrefab,
            destinationSlot.RectTransform
        );

        newCard.name = "Card - " + cardData.cardName;

        RectTransform cardRect =
            newCard.GetComponent<RectTransform>();

        if (cardRect != null)
        {
            cardRect.anchoredPosition = Vector2.zero;
            cardRect.localRotation = Quaternion.identity;
            cardRect.localScale = Vector3.one;
        }

        CardDisplay cardDisplay =
            newCard.GetComponent<CardDisplay>();

        if (cardDisplay == null)
        {
            Debug.LogError(
                "El prefab Card no contiene CardDisplay."
            );

            Destroy(newCard);
            return false;
        }

        cardDisplay.card = cardData;

        if (!activeSlots.Contains(destinationSlot))
        {
            activeSlots.Add(destinationSlot);
        }

        UpdateFreeSlot();

        Debug.Log(
            "Carta agregada a la mano: " +
            cardData.cardName
        );

        ArrangeHand();

        return true;
    }

   public void BeginCardDrag(CardSlot slot)
    {
        draggedSlot = slot;
        ArrangeHand();
    }
    public void CancelCardDrag()
    {
        draggedSlot = null;
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
        activeSlots.Remove(slot);

        newIndex = Mathf.Clamp( newIndex, 0, activeSlots.Count);
        activeSlots.Insert(newIndex, slot);
        draggedSlot = null;
        ArrangeHand();
    }
    

    private List<CardSlot> GetVisibleSlots()
    {
        List<CardSlot> visibleSlots = new List<CardSlot>();

        foreach (CardSlot slot in activeSlots)
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

    private void UpdateFreeSlot()
    {
        freeSlot = null;

        foreach (CardSlot slot in cardSlots)
        {
            if (
                slot != null && slot.transform.childCount == 0 && !activeSlots.Contains(slot))
            {
                freeSlot = slot;
                return;
            }
        }
    }

    public void RemoveSolt(CardSlot slot)
    {
        if (slot == null)
        {
            return;
        }

        activeSlots.Remove(slot);

        freeSlot = slot;

        if (draggedSlot == slot)
        {
            draggedSlot = null;
        }

        ArrangeHand();
    }





}
