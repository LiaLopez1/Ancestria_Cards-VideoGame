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
    }

}
