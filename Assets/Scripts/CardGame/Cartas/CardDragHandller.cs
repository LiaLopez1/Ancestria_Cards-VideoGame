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

 

    [Header("Red")]
    [Tooltip("Vive en la escena, no en el prefab - se busca solo si no se asigna.")]
    [SerializeField] private DeckManager deckManager;

    [Header("Audio")]
    [SerializeField] private SoundData drawCardSound;
    [SerializeField] private SoundData DiscardCardSound;

  
   

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

        // DeckManager vive en la escena, no en el prefab - no se puede
        // arrastrar en el Inspector del prefab, así que lo buscamos acá.
        if (deckManager == null)
        {
            deckManager = Object.FindFirstObjectByType<DeckManager>();

            if (deckManager == null)
            {
                Debug.LogWarning("[CardDragHandler] No se encontró un DeckManager en la escena.");
            }
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {

        drawCardSound.Play();

    
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
        DiscardCardSound.Play();
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
    /// Se llama desde DropZone al soltar la carta sobre la zona de descarte.
    /// A diferencia de la version local vieja, esto NO pone nada en la mesa
    /// directamente - solo le pide permiso al servidor
    /// (DeckManager.SolicitarDescarteServerRpc). La version "oficial" que
    /// aparece en la mesa de TODOS los jugadores la crea el servidor via
    /// MostrarCartaDescartadaClientRpc - esta carta local se destruye
    /// apenas se manda el pedido.
    /// </summary>
    public void PlaceOnTable(DeckManager deckManagerDestino)
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

        DeckManager destino = deckManagerDestino != null ? deckManagerDestino : deckManager;

        if (destino == null)
        {
            Debug.LogWarning("[CardDragHandler] No se asignó el DeckManager - no se pudo pedir el descarte.");
            return;
        }

        if (!destino.CanDiscardNow())
        {
            Debug.Log("No puedes descartar ahora (no es tu turno).");
            return;
        }

        CardDisplay display = GetComponent<CardDisplay>();

        if (display == null || display.card == null)
        {
            Debug.LogError("La carta no tiene CardDisplay/CardData asignado - no se puede identificar para el servidor.");
            return;
        }

        int cardId = display.card.cardId;

        wasPlacedOnTable = true;

        StopAllCoroutines();
        handManager.RemoveSolt(originalSlot);
        canvasGroup.blocksRaycasts = false;

        // Le pedimos permiso al servidor - si lo acepta, la carta real
        // aparece en la mesa de TODOS via ClientRpc. Si lo rechaza (por
        // ejemplo, alguien mintió sobre el turno), solo queda un warning del
        // lado del servidor - la mano local ya se vació de todas formas,
        // igual que el patrón que ya usa el robo (optimista, sin esperar
        // confirmación).
        destino.SolicitarDescarteServerRpc(cardId);

        if (cardInteraction != null)
        {
            cardInteraction.enabled = false;
        }

        enabled = false;

        // Esta copia local ya cumplió su función (mostrar el arrastre) - se
        // destruye, porque la version "oficial" en la mesa la crea el
        // servidor para todos por igual, no esta instancia.
        Destroy(gameObject);
    }
}