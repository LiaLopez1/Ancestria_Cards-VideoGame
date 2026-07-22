using UnityEngine;
using UnityEngine.EventSystems;

public class DeckCardDrag :MonoBehaviour,IBeginDragHandler,IDragHandler,IEndDragHandler
{
    private DeckManager deckManager;
    private RectTransform rectTransform;
    private Canvas canvas;
    private CanvasGroup canvasGroup;

    private Vector2 originalPosition;
    private Quaternion originalRotation;

    private bool isDragging;

    public void Configure(DeckManager manager)
    {
        deckManager = manager;
    }

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();

        canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    public void OnBeginDrag(PointerEventData eventData) //presionas y mueves la carta
    {
        if (
            deckManager == null ||
            !deckManager.CanStartManualDraw()
        )
        {
            isDragging = false;
            return;
        }

        isDragging = true;

        originalPosition = rectTransform.anchoredPosition;
        originalRotation = rectTransform.localRotation;

        canvasGroup.blocksRaycasts = false;

        transform.SetAsLastSibling();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging)
        {
            return;
        }

        float scaleFactor = 1f;

        if (canvas != null)
        {
            scaleFactor = canvas.scaleFactor;
        }

        rectTransform.anchoredPosition +=
            eventData.delta / scaleFactor;

        rectTransform.localRotation = Quaternion.identity;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!isDragging)
        {
            return;
        }

        isDragging = false;
        canvasGroup.blocksRaycasts = true;

        bool drawAccepted = deckManager.TryManualDraw(
            eventData.position,
            eventData.pressEventCamera
        );

        if (!drawAccepted)
        {
            rectTransform.anchoredPosition = originalPosition;
            rectTransform.localRotation = originalRotation;
        }
    }
}