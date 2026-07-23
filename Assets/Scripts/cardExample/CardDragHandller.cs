using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public class CardDragHandler : MonoBehaviour,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private Canvas parentCanvas;
    private RectTransform canvasRect;

    private CardSlot originalSlot;
    private HandManager handManager;
    private CardInteraction cardInteraction;

  
   

    private bool wasPlacedOnTable;

    [Header("Regreso al slot")]
    [SerializeField] private float returnDuration = 0.2f;

    private Coroutine returnCoroutine;
    private Vector3 dragOffset;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();

        parentCanvas = GetComponentInParent<Canvas>();
        canvasRect = parentCanvas.GetComponent<RectTransform>();

        originalSlot = GetComponentInParent<CardSlot>();
        handManager = GetComponentInParent<HandManager>();

        cardInteraction = GetComponent<CardInteraction>();

        canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        wasPlacedOnTable = false;

        if (returnCoroutine != null)
        {
            StopCoroutine(returnCoroutine);
        }

        // Evita que CardInteraction intente mover la carta
        // al mismo tiempo que el drag.
        if (cardInteraction != null)
        {
            cardInteraction.enabled = false;
        }

        handManager.BeginCardDrag(originalSlot);

        RectTransformUtility.ScreenPointToWorldPointInRectangle(
            canvasRect,
            eventData.position,
            eventData.pressEventCamera,
            out Vector3 pointerWorldPosition
        );

        // Guarda el punto exacto desde donde tomaste la carta.
        dragOffset = rectTransform.position - pointerWorldPosition;

        // La carta sale temporalmente del slot.
        transform.SetParent(parentCanvas.transform, true);
        transform.SetAsLastSibling();

        // Mientras se arrastra, queda completamente derecha.
        rectTransform.rotation = Quaternion.identity;

        // Permite detectar correctamente lo que está debajo.
        canvasGroup.blocksRaycasts = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        bool converted = RectTransformUtility.ScreenPointToWorldPointInRectangle(
            canvasRect,
            eventData.position,
            eventData.pressEventCamera,
            out Vector3 pointerWorldPosition
        );

        if (!converted)
        {
            return;
        }

        // Sigue libremente el cursor conservando
        // el punto desde donde tomaste la carta.
        rectTransform.position = pointerWorldPosition + dragOffset;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        canvasGroup.blocksRaycasts = true;

        if (wasPlacedOnTable)
        {
            return;
        }

        bool isInsideHand = handManager.IsPointerInsideHand(
            eventData.position,
            eventData.pressEventCamera
        );

        if (isInsideHand)
        {
            int insertionIndex = handManager.GetInsertionIndex(
                eventData.position,
                eventData.pressEventCamera
            );

            handManager.CompleteCardDrag(
                originalSlot,
                insertionIndex
            );
        }
        else
        {
            handManager.CancelCardDrag();
        }

        returnCoroutine = StartCoroutine(ReturnToSlotSmooth());
    }

    private IEnumerator ReturnToSlotSmooth()
    {
        // Cambia de padre conservando primero su posición visual.
        transform.SetParent(originalSlot.transform, true);
        transform.SetAsLastSibling();

        Vector2 startPosition = rectTransform.anchoredPosition;
        Quaternion startRotation = rectTransform.localRotation;
        Vector3 startScale = rectTransform.localScale;

        float elapsedTime = 0f;

        while (elapsedTime < returnDuration)
        {
            elapsedTime += Time.deltaTime;

            float progress = Mathf.Clamp01(
                elapsedTime / returnDuration
            );

            // Suaviza un poco el movimiento.
            float smoothProgress = Mathf.SmoothStep(
                0f,
                1f,
                progress
            );

            rectTransform.anchoredPosition = Vector2.Lerp(
                startPosition,
                Vector2.zero,
                smoothProgress
            );

            rectTransform.localRotation = Quaternion.Lerp(
                startRotation,
                Quaternion.identity,
                smoothProgress
            );

            rectTransform.localScale = Vector3.Lerp(
                startScale,
                Vector3.one,
                smoothProgress
            );

            yield return null;
        }

        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.localRotation = Quaternion.identity;
        rectTransform.localScale = Vector3.one;

        if (cardInteraction != null)
        {
            cardInteraction.enabled = true;
        }

        returnCoroutine = null;
    }

    public void PlaceOnTable(TableManager tableManager)
    {
        if (wasPlacedOnTable)
        {
            return;
        }

        if (handManager == null)
        {
            Debug.LogError("La carta no encontró su HandManager.");
            return;
        }

        if (handManager.GetCardCount() != 5)
        {
            Debug.Log("Primero debes agarrar una carta antes de descartarte.");

            return;
        }

        wasPlacedOnTable = true;

        StopAllCoroutines();
        handManager.RemoveSolt(originalSlot);
        tableManager.PlaceCard(rectTransform);
        canvasGroup.blocksRaycasts = false;

        if (cardInteraction != null)
        {
            cardInteraction.enabled = false;
        }
        enabled = false;
    }
}