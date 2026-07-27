using UnityEngine;
using UnityEngine.EventSystems;

public class CardInteraction : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler //
{
    private RectTransform rectTransform;

    private Vector2 originalPosition;
    private Vector3 originalScale;

    private bool isHovering = false;
    private bool isSelected = false;

    [Header("Hover")]
    [SerializeField] private float hoverOffsetY = 30f;
    [SerializeField] private float hoverScale = 1.08f;

    [Header("Seleccion")]
    [SerializeField] private float selectedOffsetY = 60f;

    [Header("Suavizado")]
    [SerializeField] private float moveSpeed = 12f;
    [SerializeField] private float scaleSpeed = 12f;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    private void Start()
    {
        originalPosition = rectTransform.anchoredPosition;
        originalScale = rectTransform.localScale;
    }

    private void Update()
    {
        Vector2 targetPosition = originalPosition;
        Vector3 targetScale = originalScale;

        if (isSelected)
        {
            targetPosition += new Vector2(0, selectedOffsetY);
        }
        else if (isHovering)
        {
            targetPosition += new Vector2(0, hoverOffsetY);
            targetScale = originalScale * hoverScale;
        }

        rectTransform.anchoredPosition = Vector2.Lerp(
            rectTransform.anchoredPosition,
            targetPosition,
            Time.deltaTime * moveSpeed
        );

        rectTransform.localScale = Vector3.Lerp(
            rectTransform.localScale,
            targetScale,
            Time.deltaTime * scaleSpeed
        );
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        isSelected = !isSelected;
    }
}