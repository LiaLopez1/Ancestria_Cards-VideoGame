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
    private CardDisplay cardDisplay;

    [Header("Turno")]
    [Tooltip("Necesario para avisar cuando la carta se descarta de verdad (no solo se suelta en la mano).")]
    [SerializeField] private TurnManager turnManager;

  
   

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
        cardDisplay = GetComponent<CardDisplay>();

        canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        if (turnManager == null)
        {
            turnManager = FindFirstObjectByType<TurnManager>();

            if (turnManager == null)
            {
                Debug.LogWarning("[CardDragHandler] No se encontró un TurnManager en la escena.");
            }
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

    /// <summary>
    /// Ya no recibe un TableManager: recibe el DeckManager, porque el
    /// descarte tiene que pasar por el servidor (para que sea publico y
    /// validado), no moverse directamente en este cliente.
    /// </summary>
    public void PlaceOnTable(DeckManager deckManager)
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

        if (deckManager != null && !deckManager.CanDiscardNow())
        {
            Debug.Log("No puedes descartar en este momento (no es tu turno).");
            return;
        }

        if (cardDisplay == null || cardDisplay.card == null)
        {
            Debug.LogError("La carta no tiene CardDisplay/CardData asignado, no se puede descartar.");
            return;
        }

        if (deckManager == null)
        {
            Debug.LogError("No se asignó el DeckManager en DropZone.");
            return;
        }

        wasPlacedOnTable = true;

        StopAllCoroutines();
        handManager.RemoveSolt(originalSlot);

        // Le pedimos al servidor que confirme el descarte - la version
        // visible en la mesa (para TODOS los jugadores) la crea el servidor
        // al aprobar, via DeckManager.MostrarCartaDescartadaClientRpc.
        deckManager.SolicitarDescarteServerRpc(cardDisplay.card.cardId);

        canvasGroup.blocksRaycasts = false;

        if (turnManager != null)
        {
            turnManager.CardWasDiscarded();
        }
        else
        {
            Debug.LogWarning("[CardDragHandler] No se asignó el TurnManager - el turno no va a avanzar.");
        }

        if (cardInteraction != null)
        {
            cardInteraction.enabled = false;
        }

        enabled = false;

        // Esta instancia era solo la representacion en tu mano - la version
        // "oficial" en la mesa se crea aparte (fresca) para todos.
        Destroy(gameObject);
    }
}