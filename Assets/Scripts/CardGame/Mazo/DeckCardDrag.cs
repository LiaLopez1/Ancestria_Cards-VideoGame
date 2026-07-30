using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public class DeckCardDrag : MonoBehaviour,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    private RectTransform rectTransform;
    private Canvas parentCanvas;
    private RectTransform canvasRect;
    private CanvasGroup canvasGroup;

    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private Vector3 dragOffset;

    private Transform originalParent;
    private int originalSiblingIndex;

    private Vector2 originalAnchoredPosition;
    private Quaternion originalLocalRotation;
    private Vector3 originalLocalScale;

    private DeckManager deckManager;

    [Header("Regreso al mazo")]
    [SerializeField] private float returnDuration = 0.2f;
    private Coroutine returnCoroutine;
    private Vector3 originalWorldPosition;
    private Quaternion originalWorldRotation;

    [Header("Audio")]
    [SerializeField] private SoundData drawCardSound;
    [SerializeField] private SoundData PutCardSound;


    public void Configure(DeckManager manager)
    {
        deckManager = manager;
    }

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();

        parentCanvas = GetComponentInParent<Canvas>();
        canvasRect = parentCanvas.GetComponent<RectTransform>();

        canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

 

    public void OnBeginDrag(PointerEventData eventData)
    {

        drawCardSound.Play();

        originalPosition = rectTransform.position;
        originalRotation = rectTransform.rotation;

        if (returnCoroutine != null)
        {
            StopCoroutine(returnCoroutine);
            returnCoroutine = null;
        }

        originalParent = transform.parent;
        originalSiblingIndex = transform.GetSiblingIndex();

        originalAnchoredPosition = rectTransform.anchoredPosition;
        originalLocalRotation = rectTransform.localRotation;
        originalLocalScale = rectTransform.localScale;

        originalWorldPosition = rectTransform.position;
        originalWorldRotation = rectTransform.rotation;


        RectTransformUtility.ScreenPointToWorldPointInRectangle( canvasRect, eventData.position, eventData.pressEventCamera,
            out Vector3 pointerWorldPosition );

        dragOffset = rectTransform.position - pointerWorldPosition;

        transform.SetParent(parentCanvas.transform, true);
        transform.SetAsLastSibling();

        rectTransform.rotation = Quaternion.identity;

        canvasGroup.blocksRaycasts = false;

        Debug.Log("Comenzó el arrastre desde el mazo.");
    }

    public void OnDrag(PointerEventData eventData)
    {
        bool converted =
            RectTransformUtility.ScreenPointToWorldPointInRectangle( canvasRect,eventData.position,
                eventData.pressEventCamera,
                out Vector3 pointerWorldPosition );

        if (!converted)
        {
            return;
        }

        rectTransform.position = pointerWorldPosition + dragOffset;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        PutCardSound.Play();
        
        bool drawAccepted = false;

        if (deckManager != null)
        {
            drawAccepted = deckManager.TryDrawCardToHand( eventData.position, eventData.pressEventCamera );
        }

        if (drawAccepted)
        {
            return;
        }

        canvasGroup.blocksRaycasts = false;

        if (returnCoroutine != null)
        {
            StopCoroutine(returnCoroutine);
        }

        returnCoroutine = StartCoroutine(ReturnToDeckSmooth());
    }

    private IEnumerator ReturnToDeckSmooth()
    {
        Vector3 startPosition = rectTransform.position;
        Quaternion startRotation = rectTransform.rotation;

        float elapsedTime = 0f;

        while (elapsedTime < returnDuration)
        {
            elapsedTime += Time.deltaTime;

            float progress = Mathf.Clamp01(
                elapsedTime / returnDuration
            );

            float smoothProgress = Mathf.SmoothStep(0f,1f,progress);

            rectTransform.position = Vector3.Lerp(startPosition, originalWorldPosition, smoothProgress);

            rectTransform.rotation = Quaternion.Lerp(
                startRotation,
                originalWorldRotation,
                smoothProgress
            );

            yield return null;
        }

        transform.SetParent(originalParent, true);
        transform.SetSiblingIndex(originalSiblingIndex);
        rectTransform.anchoredPosition = originalAnchoredPosition;
        rectTransform.localRotation = originalLocalRotation;
        rectTransform.localScale =originalLocalScale;
        canvasGroup.blocksRaycasts = true;
        returnCoroutine = null;

        Debug.Log("La carta regresó visualmente al mazo.");
    }
}