using System.Collections;
using System.Diagnostics;
using UnityEngine;

/// <summary>
/// Mesa de descarte compartida y publica: todos los jugadores deben ver
/// exactamente las mismas cartas aqui.
///
/// IMPORTANTE: ya no se le pasa la carta que el jugador arrastro (esa era
/// una idea que solo tenia sentido para un cliente local unico). Ahora
/// AgregarCartaDescartada crea una copia visual NUEVA a partir del CardData -
/// es DeckManager quien llama a este metodo, en TODOS los clientes por
/// igual, cuando el servidor confirma un descarte valido.
/// </summary>
public class TableManager : MonoBehaviour
{
    [Header("Contenedor de cartas jugadas")]
    [SerializeField] private RectTransform tableCards;

    [Header("Prefab de la carta visible en la mesa")]
    [Tooltip("Debe tener un CardDisplay, igual que el prefab de la mano (la carta se ve boca arriba en la mesa).")]
    [SerializeField] private GameObject tableCardPrefab;

    [Header("Variación visual")]
    [SerializeField] private float positionRangeX = 25f;
    [SerializeField] private float positionRangeY = 5f;
    [SerializeField] private float rotationRangeZ = 8f;

    [Header("Animación de caída")]
    [Tooltip("Desde cuántos píxeles por encima de su posición final empieza a caer la carta.")]
    [SerializeField] private float alturaCaida = 250f;
    [Tooltip("Duración de la caída, antes de que arranque el rebote de escala/rotación.")]
    [SerializeField] private float duracionCaida = 0.18f;

    [Header("Animación de entrada (rebote)")]
    [Tooltip("Duración total del rebote de escala y rotación al colocar la carta.")]
    [SerializeField] private float duracionAnimacion = 0.35f;
    [Tooltip("Cuánto 'overshoot' hace el rebote (más alto = rebote más exagerado).")]
    [SerializeField] private float overshoot = 1.7f;
    [Tooltip("Escala desde la que arranca el rebote al aterrizar (menor a 1 = se 'aplasta' un poco antes de asentarse).")]
    [SerializeField] private float escalaInicialRebote = 0.8f;

    [Header("Audio")]
    [SerializeField] private SoundData discardCardSound;

    public void AgregarCartaDescartada(CardData carta)
    {
        if (tableCardPrefab == null)
        {
            //Debug.LogError("[TableManager] Falta asignar el Table Card Prefab.");
            return;
        }

        GameObject cartaVisual = Instantiate(tableCardPrefab, tableCards);
        cartaVisual.name = "TableCard - " + carta.cardName;

        CardDisplay display = cartaVisual.GetComponent<CardDisplay>();

        if (display == null)
        {
            //Debug.LogError("El prefab de la mesa necesita un CardDisplay.");
            Destroy(cartaVisual);
            return;
        }

        display.card = carta;

        RectTransform cardRect = cartaVisual.GetComponent<RectTransform>();

        if (cardRect == null)
        {
            //Debug.LogError("El prefab de la mesa necesita RectTransform.");
            Destroy(cartaVisual);
            return;
        }

        cardRect.SetParent(tableCards, true);
        cardRect.SetAsLastSibling();

        Vector2 randomPosition = new Vector2(
            UnityEngine.Random.Range(-positionRangeX, positionRangeX),
            UnityEngine.Random.Range(-positionRangeY, positionRangeY)
        );

        float randomRotation = UnityEngine.Random.Range(-rotationRangeZ, rotationRangeZ);

        // La posicion final es la del drag & drop (randomPosition). La carta
        // arranca mas arriba de esa posicion para poder animar la caida.
        cardRect.localScale = Vector3.one;
        cardRect.localRotation = Quaternion.identity;
        cardRect.anchoredPosition = randomPosition + new Vector2(0f, alturaCaida);

        DesactivarInteraccion(cartaVisual);

        discardCardSound.Play();

        StartCoroutine(AnimarEntradaCarta(cardRect, randomPosition, randomRotation));
    }

    /// <summary>
    /// Anima la entrada de la carta en dos fases: primero cae desde arriba
    /// hasta su posicion final, y despues rebota un poco en escala y
    /// rotacion hasta asentarse del todo (el "pop").
    /// </summary>
    private IEnumerator AnimarEntradaCarta(RectTransform cardRect, Vector2 posicionFinal, float targetRotationZ)
    {
        // Fase 1: caida hacia la posicion final.
        Vector2 posicionInicial = cardRect.anchoredPosition;
        float tiempo = 0f;

        while (tiempo < duracionCaida)
        {
            tiempo += Time.deltaTime;
            float t = Mathf.Clamp01(tiempo / duracionCaida);
            float easedT = t * t; // ease-in: arranca lenta y acelera, como la gravedad

            cardRect.anchoredPosition = Vector2.LerpUnclamped(posicionInicial, posicionFinal, easedT);

            yield return null;
        }

        cardRect.anchoredPosition = posicionFinal;

        // Fase 2: rebote de escala y rotacion al aterrizar.
        tiempo = 0f;

        while (tiempo < duracionAnimacion)
        {
            tiempo += Time.deltaTime;
            float t = Mathf.Clamp01(tiempo / duracionAnimacion);
            float easedT = EaseOutBack(t);

            cardRect.localScale = Vector3.one * Mathf.LerpUnclamped(escalaInicialRebote, 1f, easedT);
            cardRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(0f, targetRotationZ, easedT));

            yield return null;
        }

        cardRect.localScale = Vector3.one;
        cardRect.localRotation = Quaternion.Euler(0f, 0f, targetRotationZ);
    }

    /// <summary>
    /// Easing "back out": el valor supera el 1 (overshoot) antes de
    /// asentarse, lo que da el efecto de rebote.
    /// </summary>
    private float EaseOutBack(float t)
    {
        float c1 = overshoot;
        float c3 = c1 + 1f;
        float tMenosUno = t - 1f;

        return 1f + c3 * tMenosUno * tMenosUno * tMenosUno + c1 * tMenosUno * tMenosUno;
    }

    /// <summary>
    /// Vacía la mesa de descarte visualmente. Lo llama DeckManager en TODOS
    /// los clientes cuando el mazo se queda sin cartas y la pila de
    /// descarte se recicla de vuelta al mazo - esas cartas ya no están en
    /// la mesa, así que hay que sacarlas de pantalla.
    /// </summary>
    public void LimpiarMesa()
    {
        if (tableCards == null)
        {
            return;
        }

        for (int i = tableCards.childCount - 1; i >= 0; i--)
        {
            Destroy(tableCards.GetChild(i).gameObject);
        }
    }

    /// <summary>
    /// El prefab de la mesa es el mismo que el de la mano, asi que trae
    /// componentes de arrastre/interaccion que ya no tienen sentido aqui -
    /// se desactivan para que la carta quede solo visual, sin poder
    /// arrastrarse ni generar errores al intentarlo.
    /// </summary>
    private void DesactivarInteraccion(GameObject cartaVisual)
    {
        CardDragHandler dragHandler = cartaVisual.GetComponent<CardDragHandler>();
        if (dragHandler != null)
        {
            dragHandler.enabled = false;
        }

        CardInteraction interaccion = cartaVisual.GetComponent<CardInteraction>();
        if (interaccion != null)
        {
            interaccion.enabled = false;
        }

        CanvasGroup canvasGroup = cartaVisual.GetComponent<CanvasGroup>();
        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = false;
        }
    }
}