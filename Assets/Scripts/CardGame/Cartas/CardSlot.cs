/*Este script se encarga de definir la posicion actual de la carta
y la posicion a la que debe llegar*/

using UnityEngine;

public class CardSlot : MonoBehaviour
{
    private RectTransform rectTransform;

    private Vector2 targetPosition;
    private Quaternion targetRotation;

    [Header("Movimiento")]
    [SerializeField] private float moveSpeed = 12f;
    [SerializeField] private float rotationSpeed = 12f;

    public RectTransform RectTransform => rectTransform; // escribimos una propiedad publica de solo lectura


    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();

        targetPosition = rectTransform.anchoredPosition;
        targetRotation = rectTransform.localRotation;
    }

    private void Update()
    {
        rectTransform.anchoredPosition = Vector2.Lerp(
            rectTransform.anchoredPosition,
            targetPosition,
            Time.deltaTime * moveSpeed
        );

        rectTransform.localRotation = Quaternion.Slerp(
            rectTransform.localRotation,
            targetRotation,
            Time.deltaTime * rotationSpeed
        );
    }

    public void SetTarget(Vector2 position, float rotationZ)
    {
        targetPosition = position;
        targetRotation = Quaternion.Euler(0f, 0f, rotationZ);
    }

}
