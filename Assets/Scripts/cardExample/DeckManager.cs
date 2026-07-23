using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// El mazo real (drawPile) SOLO existe en el servidor - los clientes nunca lo
/// tienen en memoria, para que ninguno pueda "ver" el orden de las cartas.
///
/// Cada cliente arma su propio mazo VISUAL (la pila boca abajo) segun un
/// contador sincronizado (cartasEnMazo), no segun las cartas reales - por
/// eso el mazo visual nunca revela identidad, solo cantidad.
///
/// El reparto inicial lo hace el servidor, y le manda a cada jugador
/// UNICAMENTE sus propias cartas via ClientRpc dirigido (no a todos).
///
/// IMPORTANTE: el robo manual (arrastrar del mazo a la mano) queda
/// temporalmente deshabilitado en este paso - eso se conecta en el
/// siguiente paso (ServerRpc de robo). Por ahora solo se blinda con un
/// aviso claro en vez de fallar en silencio.
/// </summary>
public class DeckManager : NetworkBehaviour
{
    [Header("Reparto inicial")]
    [SerializeField] private int initialHandSize = 4;
    [SerializeField] private float delayBetweenCards = 0.25f;

    [Header("Configuración del mazo")]
    [SerializeField] private int copiesPerCard = 4;

    [Header("Representación visual")]
    [SerializeField] private RectTransform deckArea;
    [SerializeField] private GameObject deckCardPrefab;
    [SerializeField] private Vector2 cardOffset = new Vector2(0.25f, -0.25f);

    [Header("Turno")]
    [SerializeField] private TurnManager turnManager;

    [Header("Mano del jugador")]
    [SerializeField] private HandManager handManager;

    // Cartas disponibles para robar. SOLO tiene contenido real en el servidor.
    private readonly List<CardData> drawPile = new List<CardData>();

    // Objetos que representan las cartas apiladas en pantalla (genericos,
    // no revelan identidad - por eso se pueden construir igual en todos lados).
    private readonly List<GameObject> visualDeck = new List<GameObject>();

    private bool initialDealFinished;

    // Cuantas cartas quedan en el mazo - esto SI se sincroniza a todos,
    // para que el mazo visual se vea igual de "alto" en todas las pantallas.
    private readonly NetworkVariable<int> cartasEnMazo = new NetworkVariable<int>(0);

    // Cuantas cartas tiene cada cliente EN TOTAL - no cuales, solo cuantas.
    // Necesario para validar reglas (ej: "solo puedes robar con 4 cartas")
    // sin que el servidor necesite saber el contenido de tu mano.
    private readonly Dictionary<ulong, int> cartasEnManoPorCliente = new Dictionary<ulong, int>();

    public int CardsRemaining
    {
        get { return cartasEnMazo.Value; }
    }

    public override void OnNetworkSpawn()
    {
        cartasEnMazo.OnValueChanged += (anterior, nuevo) => ActualizarMazoVisual(nuevo);

        if (IsServer)
        {
            BuildLogicalDeck();
            ShuffleDeck();
            cartasEnMazo.Value = drawPile.Count;

            StartCoroutine(RepartirManoInicialATodos());
        }

        // Todos (incluido el host) arman su propio mazo visual con el
        // conteo actual - el host lo hace de una porque cartasEnMazo.Value
        // ya quedo asignado arriba antes de llegar aqui.
        ActualizarMazoVisual(cartasEnMazo.Value);
    }

    private void BuildLogicalDeck()
    {
        drawPile.Clear();

        foreach (CardData cardData in CardDatabase.Instance.ObtenerTodas())
        {
            if (cardData == null)
            {
                continue;
            }

            for (int copy = 0; copy < copiesPerCard; copy++)
            {
                drawPile.Add(cardData);
            }
        }

        Debug.Log("[Servidor] Mazo creado con " + drawPile.Count + " cartas.");
    }

    private void ShuffleDeck()
    {
        for (int i = drawPile.Count - 1; i > 0; i--)
        {
            int randomIndex = Random.Range(0, i + 1);

            CardData temporaryCard = drawPile[i];
            drawPile[i] = drawPile[randomIndex];
            drawPile[randomIndex] = temporaryCard;
        }

        Debug.Log("[Servidor] El mazo fue mezclado.");
    }

    /// <summary>
    /// Reconstruye la pila visual (generica, sin identidad) para que tenga
    /// exactamente "cantidad" cartas boca abajo.
    /// </summary>
    private void ActualizarMazoVisual(int cantidad)
    {
        ClearVisualDeck();

        for (int i = 0; i < cantidad; i++)
        {
            GameObject visualCard = Instantiate(deckCardPrefab, deckArea);
            visualCard.name = "DeckCard " + i;

            DeckCardDrag deckCardDrag = visualCard.GetComponent<DeckCardDrag>();

            if (deckCardDrag != null)
            {
                deckCardDrag.Configure(this);
                deckCardDrag.enabled = false;
            }

            RectTransform cardRect = visualCard.GetComponent<RectTransform>();

            if (cardRect == null)
            {
                Debug.LogError("El prefab del mazo necesita RectTransform.");
                Destroy(visualCard);
                continue;
            }

            cardRect.anchoredPosition = cardOffset * i;
            cardRect.localRotation = Quaternion.identity;
            cardRect.localScale = Vector3.one;

            visualCard.transform.SetSiblingIndex(i);

            visualDeck.Add(visualCard);
        }

        RefreshTopCardDrag();
    }

    private void ClearVisualDeck()
    {
        foreach (GameObject visualCard in visualDeck)
        {
            if (visualCard != null)
            {
                Destroy(visualCard);
            }
        }
        visualDeck.Clear();
    }

    /// <summary>
    /// SOLO debe llamarse desde el servidor - roba del mazo real.
    /// </summary>
    private CardData DrawCard()
    {
        if (drawPile.Count == 0)
        {
            Debug.LogWarning("[Servidor] No quedan cartas en el mazo.");
            return null;
        }

        int topCardIndex = drawPile.Count - 1;
        CardData drawnCard = drawPile[topCardIndex];
        drawPile.RemoveAt(topCardIndex);

        cartasEnMazo.Value = drawPile.Count;

        Debug.Log("[Servidor] Carta robada: " + drawnCard.cardName + " | Cartas restantes: " + drawPile.Count);

        return drawnCard;
    }

    /// <summary>
    /// SOLO corre en el servidor. Reparte la mano inicial a cada jugador
    /// conectado, mandandole a cada uno UNICAMENTE sus propias cartas.
    /// </summary>
    private IEnumerator RepartirManoInicialATodos()
    {
        initialDealFinished = false;

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            for (int i = 0; i < initialHandSize; i++)
            {
                yield return new WaitForSeconds(delayBetweenCards);

                CardData drawnCard = DrawCard();

                if (drawnCard == null)
                {
                    yield break;
                }

                EnviarCartaAlJugadorClientRpc(drawnCard.cardId, ParaCliente(clientId));

                cartasEnManoPorCliente[clientId] = (cartasEnManoPorCliente.TryGetValue(clientId, out int actual) ? actual : 0) + 1;
            }
        }

        initialDealFinished = true;

        Debug.Log("[Servidor] Reparto inicial terminado para todos los jugadores.");

        // TODO (paso 5): reconectar esto con un TurnManager sincronizado.
        // Todavia no llamamos turnManager.InitialDealFinished() aqui porque
        // el turno todavia no esta en red - lo hacemos en el siguiente paso.
    }

    /// <summary>
    /// Se ejecuta SOLO en el cliente al que se dirigio (gracias a
    /// ClientRpcParams) - por eso ningun otro jugador ve esta carta.
    /// </summary>
    [ClientRpc]
    private void EnviarCartaAlJugadorClientRpc(int cardId, ClientRpcParams clientRpcParams = default)
    {
        CardData carta = CardDatabase.Instance.ObtenerPorId(cardId);

        if (carta == null)
        {
            Debug.LogError($"[Cliente] Llego un cardId invalido: {cardId}");
            return;
        }

        bool cardAdded = handManager.AddCardToHand(carta);

        if (!cardAdded)
        {
            Debug.LogError("[Cliente] No fue posible agregar la carta recibida a la mano.");
        }
    }

    private ClientRpcParams ParaCliente(ulong clientId)
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new ulong[] { clientId }
            }
        };
    }

    private void RefreshTopCardDrag()
    {
        for (int i = 0; i < visualDeck.Count; i++)
        {
            GameObject visualCard = visualDeck[i];

            if (visualCard == null)
            {
                continue;
            }

            DeckCardDrag drag = visualCard.GetComponent<DeckCardDrag>();

            if (drag == null)
            {
                continue;
            }

            drag.Configure(this);

            bool isTopCard = i == visualDeck.Count - 1;

            drag.enabled = isTopCard;
        }
    }

    // ---------- Robo manual (drag del mazo a la mano) ----------

    public bool CanStartManualDraw()
    {
        return initialDealFinished && handManager != null && handManager.GetCardCount() == initialHandSize;
    }

    /// <summary>
    /// Corre en el CLIENTE que arrastro la carta. Solo hace la validacion
    /// visual basica (¿la soltaste dentro de tu mano?) y le pide permiso al
    /// servidor - la validacion de verdad (¿tienes las cartas correctas?
    /// ¿queda mazo?) pasa del lado del servidor, nunca aqui.
    /// </summary>
    public bool TryManualDraw(Vector2 screenPosition, Camera eventCamera)
    {
        return TryDrawCardToHand(screenPosition, eventCamera);
    }

    public bool CanDrawCard()
    {
        return CanStartManualDraw();
    }

    public bool TryDrawCardToHand(Vector2 screenPosition, Camera eventCamera)
    {
        if (handManager == null)
        {
            Debug.LogError("No se asignó el HandManager.");
            return false;
        }

        bool isInsideHand = handManager.IsPointerInsideHand(screenPosition, eventCamera);

        if (!isInsideHand)
        {
            Debug.Log("La carta se soltó fuera de la mano.");
            return false;
        }

        // No agregamos la carta aqui: solo pedimos permiso. Si el servidor
        // aprueba, la carta real llega despues via EnviarCartaAlJugadorClientRpc.
        SolicitarRoboServerRpc();
        return true;
    }

    /// <summary>
    /// El cliente PIDE robar, no roba directamente - el servidor decide.
    /// RequireOwnership=false porque DeckManager es un objeto de escena del
    /// servidor, no le pertenece a ningun cliente en particular.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void SolicitarRoboServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clienteSolicitante = rpcParams.Receive.SenderClientId;

        if (drawPile.Count == 0)
        {
            Debug.LogWarning($"[Servidor] Cliente {clienteSolicitante} pidió robar pero no quedan cartas.");
            return;
        }

        int cartasActuales = cartasEnManoPorCliente.TryGetValue(clienteSolicitante, out int valor) ? valor : 0;

        if (cartasActuales != initialHandSize)
        {
            Debug.LogWarning($"[Servidor] Cliente {clienteSolicitante} intentó robar con {cartasActuales} carta(s) (debe tener {initialHandSize}).");
            return;
        }

        CardData drawnCard = DrawCard();

        if (drawnCard == null)
        {
            return;
        }

        cartasEnManoPorCliente[clienteSolicitante] = cartasActuales + 1;

        EnviarCartaAlJugadorClientRpc(drawnCard.cardId, ParaCliente(clienteSolicitante));

        Debug.Log($"[Servidor] Cliente {clienteSolicitante} robó correctamente. Ahora tiene {cartasActuales + 1} carta(s).");
    }
}