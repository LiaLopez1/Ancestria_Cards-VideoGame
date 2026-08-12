using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HandManager : MonoBehaviour
{
    [Header("Creación de cartas")]
    [SerializeField] private GameObject cardPrefab;
    [SerializeField] private List<CardSlot> cardSlots = new List<CardSlot>();
    private List<CardSlot> activeSlots = new List<CardSlot>();
    private CardSlot freeSlot;

    [Header("Forma de la mano")]
    [SerializeField] private float spacing = 130f;
    [SerializeField] private float curveHeight = 25f;
    [SerializeField] private float maxRotation = 10f;

    [Header("Animacion de intercambio")]
    [Tooltip("Cuanto se desplaza verticalmente la carta que se va / la que entra durante la animacion de intercambio.")]
    [SerializeField] private float offsetAnimacionIntercambio = 200f;
    [SerializeField] private float duracionAnimacionIntercambio = 0.35f;
    [SerializeField] private AnimationCurve curvaIntercambio = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // La carta actualmente marcada para intercambio (null si ninguna, o si
    // no estamos en modo intercambio). Solo puede haber una a la vez.
    private CardInteraction cardSeleccionada;

    /// <summary>
    /// Se dispara cuando el jugador elige una carta nueva mientras esta en
    /// modo intercambio (cardId de la carta elegida). TradeCardSelectPromptUI
    /// se suscribe a esto para saber cuando mandar la confirmacion al servidor.
    /// </summary>
    public event System.Action<int> OnCartaSeleccionadaParaIntercambio;


    private RectTransform handRectTransform;
    private CardSlot draggedSlot;

    private void Awake()
    {
        handRectTransform = GetComponent<RectTransform>();

        activeSlots.Clear();

        foreach (CardSlot slot in cardSlots)
        {
            if (slot == null)
            {
                continue;
            }

            if (slot.transform.childCount > 0)
            {
                activeSlots.Add(slot);
            }
        }

        UpdateFreeSlot();
    }

    private void Start()
    {
        ArrangeHand();
    }

    public int GetCardCount()
    {
       return activeSlots.Count;
    }

//Funcion para saber si hay espacio
    public bool HasEmptySlot()
    {
        return freeSlot != null;
        //me dice, si hay una slot libre= hay espacio;
    }

    public void ArrangeHand()
    {
        List<CardSlot> visibleSlots = GetVisibleSlots(); 
        //lista que contiene únicamente las cartas que actualmente deben mostrarse.

        int count = visibleSlots.Count; // ese dato lo guardamos aqui

        if (count == 0)
            return;

        float center = (count - 1) / 2f;

        for (int i = 0; i < count; i++)
        {
            float offset = i - center; // espacio de la carta al centro

            float x = offset * spacing;
            float y = -Mathf.Abs(offset) * curveHeight; // Mathf.Abs(offset) = valor absoluto
            // esta es la que le da la forma de abanico 

            float rotationZ = -offset * maxRotation;


            CardSlot slot = visibleSlots[i]; // pide la carta que corresponde al indice

        // Actaulizamos el orden visual en la jerarquia
            slot.transform.SetSiblingIndex(i);

            slot.SetTarget( new Vector2(x, y), rotationZ);


        }
    }

    public bool AddCardToHand(CardData cardData)
    {
        if (cardData == null)
        {
            Debug.LogWarning( "No se recibió información para crear la carta.");

            return false;
        }

        if (freeSlot == null)
        {
            UpdateFreeSlot();
        }

        if (freeSlot == null)
        {
            Debug.LogWarning( "No quedan espacios vacíos en la mano.");

            return false;
        }

        CardSlot destinationSlot = freeSlot;

        GameObject newCard = Instantiate( cardPrefab, destinationSlot.RectTransform );

        newCard.name = "Card - " + cardData.cardName;

        RectTransform cardRect = newCard.GetComponent<RectTransform>();

        if (cardRect != null)
        {
            cardRect.anchoredPosition = Vector2.zero;
            cardRect.localRotation = Quaternion.identity;
            cardRect.localScale = Vector3.one;
        }

        CardDisplay cardDisplay = newCard.GetComponent<CardDisplay>();

        if (cardDisplay == null)
        {
            Debug.LogError( "El prefab Card no contiene CardDisplay." );

            Destroy(newCard);
            return false;
        }

        cardDisplay.card = cardData;

        if (!activeSlots.Contains(destinationSlot))
        {
            activeSlots.Add(destinationSlot);
        }

        UpdateFreeSlot();

        Debug.Log( "Carta agregada a la mano: " + cardData.cardName
        );

        ArrangeHand();

        return true;
    }

   public void BeginCardDrag(CardSlot slot)
    {
        draggedSlot = slot;
        ArrangeHand();
    }
    public void CancelCardDrag()
    {
        draggedSlot = null;
        ArrangeHand();
    }

    public int GetInsertionIndex(Vector2 screenPosition,Camera eventCamera)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            handRectTransform,
            screenPosition,
            eventCamera,
            out Vector2 localPointerPosition
        );

        List<CardSlot> visibleSlots = GetVisibleSlots();

        for (int i = 0; i < visibleSlots.Count; i++)
        {
            float slotX = visibleSlots[i].RectTransform.anchoredPosition.x;

            if (localPointerPosition.x < slotX)
            {
                return i;
            }
        }

        return visibleSlots.Count;
    }
  
    public void CompleteCardDrag(CardSlot slot, int newIndex)
    {
        activeSlots.Remove(slot);

        newIndex = Mathf.Clamp( newIndex, 0, activeSlots.Count);
        activeSlots.Insert(newIndex, slot);
        draggedSlot = null;
        ArrangeHand();
    }
    

    private List<CardSlot> GetVisibleSlots()
    {
        List<CardSlot> visibleSlots = new List<CardSlot>();

        foreach (CardSlot slot in activeSlots)
        {
            if (slot != draggedSlot)
            {
                visibleSlots.Add(slot);
            }
        }

        return visibleSlots;
    }

    public bool IsPointerInsideHand( Vector2 screenPosition,Camera eventCamera)
    {
        return RectTransformUtility.RectangleContainsScreenPoint(
            handRectTransform,
            screenPosition,
            eventCamera
        );
    }

    private void UpdateFreeSlot()
    {
        freeSlot = null;

        foreach (CardSlot slot in cardSlots)
        {
            if (
                slot != null && slot.transform.childCount == 0 && !activeSlots.Contains(slot))
            {
                freeSlot = slot;
                return;
            }
        }
    }

    public void RemoveSolt(CardSlot slot)
    {
        if (slot == null)
        {
            return;
        }

        activeSlots.Remove(slot);

        freeSlot = slot;

        if (draggedSlot == slot)
        {
            draggedSlot = null;
        }

        ArrangeHand();
    }

    /// <summary>
    /// Destruye TODAS las cartas de la mano y deja todos los slots vacios -
    /// se usa al reiniciar la partida (GameRestartManager), para que cada
    /// cliente arranque la ronda nueva con la mano completamente limpia
    /// antes de que le lleguen las cartas repartidas de nuevo.
    /// </summary>
    public void LimpiarManoCompleta()
    {
        foreach (CardSlot slot in cardSlots)
        {
            if (slot == null)
            {
                continue;
            }

            for (int i = slot.transform.childCount - 1; i >= 0; i--)
            {
                Destroy(slot.transform.GetChild(i).gameObject);
            }
        }

        activeSlots.Clear();
        draggedSlot = null;
        cardSeleccionada = null;

        UpdateFreeSlot();
        ArrangeHand();
    }

    // ---------------------------------------------------------------
    // Intercambio de cartas
    // ---------------------------------------------------------------

    /// <summary>
    /// Habilita la seleccion en todas las cartas actuales de la mano - SOLO
    /// debe llamarse cuando arranca el flujo de intercambio (TradeCardSelectPromptUI.Mostrar).
    /// </summary>
    public void HabilitarSeleccionParaIntercambio()
    {
        foreach (CardSlot slot in activeSlots)
        {
            CardInteraction interaccion = ObtenerInteraccion(slot);

            if (interaccion == null)
            {
                continue;
            }

            interaccion.OnSeleccionCambiada += ManejarSeleccionCambiada;
            interaccion.SetPermiteSeleccion(true);
        }
    }

    /// <summary>
    /// Deshabilita la seleccion en todas las cartas de la mano y limpia
    /// cualquier seleccion pendiente - se llama al cerrar el flujo de
    /// intercambio (completado, rechazado, o cancelado).
    /// </summary>
    public void DeshabilitarSeleccionParaIntercambio()
    {
        foreach (CardSlot slot in activeSlots)
        {
            CardInteraction interaccion = ObtenerInteraccion(slot);

            if (interaccion == null)
            {
                continue;
            }

            interaccion.OnSeleccionCambiada -= ManejarSeleccionCambiada;
            interaccion.SetPermiteSeleccion(false);
        }

        cardSeleccionada = null;
    }

    /// <summary>¿Hay alguna carta seleccionada ahora mismo para intercambio?</summary>
    public bool HaySeleccionActiva()
    {
        return cardSeleccionada != null;
    }

    /// <summary>cardId de la carta seleccionada, o -1 si no hay ninguna.</summary>
    public int ObtenerCardIdSeleccionado()
    {
        if (cardSeleccionada == null)
        {
            return -1;
        }

        CardDisplay display = cardSeleccionada.GetComponent<CardDisplay>();

        return display != null && display.card != null ? display.card.cardId : -1;
    }

    private void ManejarSeleccionCambiada(CardInteraction origen)
    {
        if (origen == null)
        {
            return;
        }

        if (origen.IsSelected)
        {
            // Solo una carta seleccionada a la vez - se desmarca cualquier otra.
            if (cardSeleccionada != null && cardSeleccionada != origen)
            {
                cardSeleccionada.ForzarDeseleccion();
            }

            cardSeleccionada = origen;

            CardDisplay display = origen.GetComponent<CardDisplay>();
            int cardId = display != null && display.card != null ? display.card.cardId : -1;

            OnCartaSeleccionadaParaIntercambio?.Invoke(cardId);
        }
        else if (cardSeleccionada == origen)
        {
            cardSeleccionada = null;
        }
    }

    /// <summary>
    /// SOLO debe llamarse desde el ClientRpc de TradeManager cuando el
    /// servidor confirma que el intercambio se ejecuto. Anima la carta que
    /// se va (la que este cliente habia seleccionado) deslizandola hacia
    /// abajo, y agrega la carta recibida con una animacion de entrada.
    /// </summary>
    public void EjecutarIntercambioVisual(CardData cartaRecibida)
    {
        if (cardSeleccionada == null)
        {
            Debug.LogError("[HandManager] Se pidio ejecutar el intercambio visual pero no hay ninguna carta seleccionada localmente.");
            return;
        }

        GameObject cartaQueSeVa = cardSeleccionada.gameObject;
        CardSlot slotQueSeVa = cardSeleccionada.GetComponentInParent<CardSlot>();
        RectTransform cartaQueSeVaRect = cartaQueSeVa.GetComponent<RectTransform>();

        // Sacamos la carta saliente de su CardSlot ANTES de liberarlo, para
        // que el slot quede realmente vacio y la carta entrante lo pueda
        // reutilizar sin pisarse con la que todavia esta animando su salida.
        if (cartaQueSeVaRect != null)
        {
            cartaQueSeVaRect.SetParent(handRectTransform, true);

            // CardInteraction memorizo su "posicion normal" relativa al
            // CardSlot anterior - al reparentar eso queda obsoleto, y si
            // sigue activo se pelearia cada frame con esta animacion de
            // salida. Como la carta se destruye en instantes, la apagamos.
            CardInteraction interaccionSaliente = cartaQueSeVaRect.GetComponent<CardInteraction>();
            if (interaccionSaliente != null)
            {
                interaccionSaliente.enabled = false;
            }
        }

        if (slotQueSeVa != null)
        {
            RemoveSolt(slotQueSeVa);
        }

        cardSeleccionada = null;

        if (cartaQueSeVaRect != null)
        {
            StartCoroutine(AnimarSalidaYDestruir(cartaQueSeVaRect));
        }

        bool agregada = AddCardToHand(cartaRecibida);

        if (agregada && activeSlots.Count > 0)
        {
            CardSlot slotNuevo = activeSlots[activeSlots.Count - 1];
            RectTransform cartaNuevaRect = slotNuevo.transform.childCount > 0
                ? slotNuevo.transform.GetChild(slotNuevo.transform.childCount - 1) as RectTransform
                : null;

            if (cartaNuevaRect != null)
            {
                StartCoroutine(AnimarEntrada(cartaNuevaRect));
            }
        }
    }

    private IEnumerator AnimarSalidaYDestruir(RectTransform carta)
    {
        Vector2 inicio = carta.anchoredPosition;
        Vector2 destino = inicio + new Vector2(0f, -offsetAnimacionIntercambio);

        float tiempoTranscurrido = 0f;

        while (tiempoTranscurrido < duracionAnimacionIntercambio)
        {
            tiempoTranscurrido += Time.deltaTime;
            float progreso = curvaIntercambio.Evaluate(Mathf.Clamp01(tiempoTranscurrido / duracionAnimacionIntercambio));

            carta.anchoredPosition = Vector2.Lerp(inicio, destino, progreso);

            yield return null;
        }

        if (carta != null)
        {
            Destroy(carta.gameObject);
        }
    }

    private IEnumerator AnimarEntrada(RectTransform carta)
    {
        Vector2 destino = carta.anchoredPosition;

        // Esperamos un frame para que CardInteraction.Start() ya haya
        // corrido y capturado ESTA posicion (destino) como su "posicion
        // normal" - si desplazaramos la carta ANTES de eso, CardInteraction
        // memorizaria la posicion desplazada como si fuera la normal, y la
        // carta quedaria desfasada para siempre despues de la animacion.
        yield return null;

        Vector2 inicio = destino + new Vector2(0f, -offsetAnimacionIntercambio);
        carta.anchoredPosition = inicio;

        float tiempoTranscurrido = 0f;

        while (tiempoTranscurrido < duracionAnimacionIntercambio)
        {
            tiempoTranscurrido += Time.deltaTime;
            float progreso = curvaIntercambio.Evaluate(Mathf.Clamp01(tiempoTranscurrido / duracionAnimacionIntercambio));

            carta.anchoredPosition = Vector2.Lerp(inicio, destino, progreso);

            yield return null;
        }

        carta.anchoredPosition = destino;
    }

    private CardInteraction ObtenerInteraccion(CardSlot slot)
    {
        return slot != null ? slot.transform.GetComponentInChildren<CardInteraction>() : null;
    }
}