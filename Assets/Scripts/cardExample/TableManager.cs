using UnityEngine;

public class TableManager : MonoBehaviour
{
    [Header ("Contenedor de cartas jugadas")]
    [SerializeField] private RectTransform tableCards;

    [Header("Variación visual")]
    [SerializeField] private float positionRangeX = 25f;
    [SerializeField] private float positionRangeY = 5f;
    [SerializeField] private float rotationRangeZ = 8f;

    public void PlaceCard(RectTransform cardRect)
    {
        cardRect.SetParent(tableCards, true);
        cardRect.SetAsLastSibling();

        Vector2 randomPosition = new Vector2(
            Random.Range(-positionRangeX, positionRangeX),
            Random.Range(-positionRangeY, positionRangeY)
        );

        float randomRotation = Random.Range(
            -rotationRangeZ,
            rotationRangeZ
        );

        cardRect.anchoredPosition = randomPosition;
        cardRect.localRotation = Quaternion.Euler(
            0f,
            0f,
            randomRotation
        );

        cardRect.localScale = Vector3.one;

        // SetParent(tableCards, true) conserva la posición en el MUNDO al
        // cambiar de padre - eso incluye el Z, que anchoredPosition (solo
        // X/Y) nunca corrige. Si la carta viene de un punto de la jerarquía
        // con un Z distinto al de la mesa (por ejemplo, la mano del boss vs.
        // la mano del jugador), arrastra ese Z y termina en un plano distinto
        // - por eso forzamos el Z local a 0 acá, así toda carta que llega a
        // la mesa queda exactamente en el mismo plano sin importar de dónde
        // venía.
        Vector3 posicionLocal = cardRect.localPosition;
        posicionLocal.z = 0f;
        cardRect.localPosition = posicionLocal;
    }

}