using System.Collections.Generic;
using UnityEngine;

public class HandManager : MonoBehaviour
{
    [Header("Slots de la mano")]
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

            slot.SetTarget(
                new Vector2(x, y),
                rotationZ
                );


            /*visibleSlots[i].SetTarget(     
                new Vector2(x, y),
                rotationZ
            );
            este es para actualizar el slot solo en juego, no en la jerarquia
            esto hace que debido a la posición en la jerarquia se vean unas enciam de otras y no en orden*/
        }
    }

    public void BeginCardDrag(CardSlot slot)
    {
        draggedSlot = slot;
        ArrangeHand();
    }

    public int GetInsertionIndex(
        Vector2 screenPosition,
        Camera eventCamera
    )
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

    public bool IsPointerInsideHand(
    Vector2 screenPosition,
    Camera eventCamera
    )
    {
        return RectTransformUtility.RectangleContainsScreenPoint(
            handRectTransform,
            screenPosition,
            eventCamera
        );
    }

    public void RemoveSolt(CardSlot slot)
    {
        cardSlots.Remove(slot);

        if (draggedSlot == slot)
        {
            draggedSlot = null;
        }

        ArrangeHand();

        Destroy(slot.gameObject);


    }





}
