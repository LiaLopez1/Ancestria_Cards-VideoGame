using UnityEngine;
using UnityEngine.EventSystems;

public class CardInteraction : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler //
{
    private RectTransform rectTransform;

    private Vector2 originalPosition;
    private Vector3 originalScale;

    private bool isHovering = false;
    private bool isSelected = false;

    // Antes se podia seleccionar cualquier cantidad de cartas, en cualquier
    // momento (con solo hacer click). Ahora la seleccion SOLO funciona
    // mientras el jugador esta en medio de un intercambio - HandManager
    // habilita/deshabilita esto en todas las cartas de la mano segun
    // corresponda (ver SetPermiteSeleccion).
    private bool permiteSeleccion = false;

    [Header("Hover")]
    [SerializeField] private float hoverOffsetY = 30f;
    [SerializeField] private float hoverScale = 1.08f;

    [Header("Seleccion")]
    [SerializeField] private float selectedOffsetY = 60f;

    [Header("Suavizado")]
    [SerializeField] private float moveSpeed = 12f;
    [SerializeField] private float scaleSpeed = 12f;

    public bool IsSelected => isSelected;

    /// <summary>
    /// Se dispara cada vez que cambia el estado de seleccion de esta carta
    /// (por click propio, o por ForzarDeseleccion llamado desde afuera).
    /// Manda esta misma instancia como parametro. HandManager se suscribe
    /// a esto mientras dura un intercambio, para imponer "una sola carta
    /// seleccionada a la vez" y para enterarse de cual carta eligio el
    /// jugador.
    /// </summary>
    public event System.Action<CardInteraction> OnSeleccionCambiada;

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
        if (!permiteSeleccion)
        {
            return;
        }

        isSelected = !isSelected;
        OnSeleccionCambiada?.Invoke(this);
    }

    /// <summary>
    /// Habilita o deshabilita que esta carta pueda seleccionarse. SOLO
    /// debe ponerse en true mientras el jugador esta en medio de un
    /// intercambio (HandManager lo llama en todas las cartas de la mano al
    /// entrar/salir de ese modo). Al deshabilitar, tambien fuerza la
    /// deseleccion para no dejar el visual "trabado" arriba.
    /// </summary>
    public void SetPermiteSeleccion(bool permitir)
    {
        permiteSeleccion = permitir;

        if (!permitir)
        {
            ForzarDeseleccion();
        }
    }

    /// <summary>
    /// Deselecciona esta carta desde afuera (no requiere permiteSeleccion) -
    /// lo usa HandManager para imponer "una sola carta seleccionada a la
    /// vez": cuando se elige una carta nueva, la anterior se desmarca asi.
    /// </summary>
    public void ForzarDeseleccion()
    {
        if (!isSelected)
        {
            return;
        }

        isSelected = false;
        OnSeleccionCambiada?.Invoke(this);
    }
}