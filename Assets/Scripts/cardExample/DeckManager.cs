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

    [Header("Mesa de descarte")]
    [SerializeField] private TableManager tableManager;

    [Header("Iniciar partida (solo host)")]
    [SerializeField] private GameObject botonIniciarPartida;

    // Cartas disponibles para robar. SOLO tiene contenido real en el servidor.
    private readonly List<CardData> drawPile = new List<CardData>();

    // Objetos que representan las cartas apiladas en pantalla (genericos,
    // no revelan identidad - por eso se pueden construir igual en todos lados).
    private readonly List<GameObject> visualDeck = new List<GameObject>();

    private bool partidaIniciada;

    // Cuantas cartas quedan en el mazo - esto SI se sincroniza a todos,
    // para que el mazo visual se vea igual de "alto" en todas las pantallas.
    private readonly NetworkVariable<int> cartasEnMazo = new NetworkVariable<int>(0);

    // Que cartas (por ID) tiene cada cliente EN SU MANO ahora mismo - no
    // solo cuantas, sino cuales exactamente. Necesario para poder validar
    // el descarte (¿de verdad tienes esa carta?) ademas de reglas de conteo.
    private readonly Dictionary<ulong, List<int>> manoPorCliente = new Dictionary<ulong, List<int>>();

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
        }

        // El boton de iniciar partida solo lo puede usar el host.
        if (botonIniciarPartida != null)
        {
            botonIniciarPartida.SetActive(IsServer);
        }

        // Todos (incluido el host) arman su propio mazo visual con el
        // conteo actual - el host lo hace de una porque cartasEnMazo.Value
        // ya quedo asignado arriba antes de llegar aqui.
        ActualizarMazoVisual(cartasEnMazo.Value);
    }

    /// <summary>
    /// Conectado al boton "Iniciar partida" (solo visible para el host).
    /// Reparte la mano inicial a TODOS los jugadores conectados en este
    /// momento (1, 2 o 3), simultaneamente, en vez de repartir uno por uno
    /// a medida que se conectan.
    /// </summary>
    public void OnIniciarPartidaPressed()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[DeckManager] Solo el host puede iniciar la partida.");
            return;
        }

        if (partidaIniciada)
        {
            Debug.LogWarning("[DeckManager] La partida ya fue iniciada.");
            return;
        }

        partidaIniciada = true;

        if (botonIniciarPartida != null)
        {
            botonIniciarPartida.SetActive(false);
        }

        int cantidadJugadores = NetworkManager.Singleton.ConnectedClientsIds.Count;

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            StartCoroutine(RepartirManoAJugador(clientId));
        }

        if (turnManager != null)
        {
            turnManager.IniciarPrimerTurno(cantidadJugadores);
        }

        Debug.Log($"[Servidor] Partida iniciada. Repartiendo a {cantidadJugadores} jugador(es).");
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
    /// SOLO corre en el servidor. Reparte la mano inicial a UN jugador en
    /// particular, mandandole UNICAMENTE sus propias cartas. OnIniciarPartidaPressed
    /// arranca una de estas corrutinas por jugador EN PARALELO, asi todos
    /// reciben sus cartas al mismo tiempo (no uno detras de otro).
    /// </summary>
    private IEnumerator RepartirManoAJugador(ulong clientId)
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

            AgregarCartaAManoDeCliente(clientId, drawnCard.cardId);
        }

        Debug.Log($"[Servidor] Reparto inicial terminado para el cliente {clientId}.");

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
        return handManager != null
            && handManager.GetCardCount() == initialHandSize
            && (turnManager == null || turnManager.EsMiTurno());
    }

    public bool CanDiscardNow()
    {
        return turnManager == null || turnManager.CanDiscard();
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

        if (turnManager != null && !turnManager.CanDraw())
        {
            Debug.Log("No puedes robar una carta en este momento (no es tu turno).");
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

        if (!ValidarTurnoDelCliente(clienteSolicitante, "robar"))
        {
            return;
        }

        if (drawPile.Count == 0)
        {
            Debug.LogWarning($"[Servidor] Cliente {clienteSolicitante} pidió robar pero no quedan cartas.");
            return;
        }

        int cartasActuales = ObtenerManoDeCliente(clienteSolicitante).Count;

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

        AgregarCartaAManoDeCliente(clienteSolicitante, drawnCard.cardId);

        EnviarCartaAlJugadorClientRpc(drawnCard.cardId, ParaCliente(clienteSolicitante));

        if (turnManager != null)
        {
            turnManager.NotificarRoboRealizado();
        }

        Debug.Log($"[Servidor] Cliente {clienteSolicitante} robó correctamente. Ahora tiene {cartasActuales + 1} carta(s).");
    }

    /// <summary>
    /// SOLO corre en el servidor. El cliente pide descartar una carta
    /// puntual (por su cardId) - se valida que de verdad la tenga en mano
    /// antes de aceptar, y se le avisa a TODOS los jugadores (la mesa de
    /// descarte es publica, a diferencia de la mano).
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void SolicitarDescarteServerRpc(int cardId, ServerRpcParams rpcParams = default)
    {
        ulong clienteSolicitante = rpcParams.Receive.SenderClientId;

        if (!ValidarTurnoDelCliente(clienteSolicitante, "descartar"))
        {
            return;
        }

        List<int> mano = ObtenerManoDeCliente(clienteSolicitante);

        if (!mano.Remove(cardId))
        {
            Debug.LogWarning($"[Servidor] Cliente {clienteSolicitante} intentó descartar una carta que no tiene (cardId={cardId}).");
            return;
        }

        Debug.Log($"[Servidor] Cliente {clienteSolicitante} descartó cardId={cardId}. Le quedan {mano.Count} carta(s).");

        MostrarCartaDescartadaClientRpc(cardId);

        if (turnManager != null)
        {
            turnManager.NotificarDescarteRealizado();
        }
    }

    /// <summary>
    /// Se ejecuta en TODOS los clientes (a diferencia del reparto, que es
    /// privado) - la mesa de descarte es publica, todos deben ver lo mismo.
    /// </summary>
    [ClientRpc]
    private void MostrarCartaDescartadaClientRpc(int cardId)
    {
        CardData carta = CardDatabase.Instance.ObtenerPorId(cardId);

        if (carta == null)
        {
            Debug.LogError($"[Cliente] Llegó un cardId inválido para descarte: {cardId}");
            return;
        }

        if (tableManager != null)
        {
            tableManager.AgregarCartaDescartada(carta);
        }
    }

    /// <summary>
    /// SOLO corre en el servidor. Traduce el clientId al slot (0/1/2) y le
    /// pregunta a TurnManager si de verdad es el turno de ese slot.
    /// </summary>
    private bool ValidarTurnoDelCliente(ulong clientId, string accion)
    {
        if (turnManager == null)
        {
            return true; // sin TurnManager asignado, no bloqueamos (modo de prueba)
        }

        if (!NetworkBootstrap.Instance.TryObtenerSlot(clientId, out int slot))
        {
            Debug.LogWarning($"[Servidor] No se encontró el slot del cliente {clientId} al intentar {accion}.");
            return false;
        }

        if (!turnManager.EsTurnoDelSlot(slot))
        {
            Debug.LogWarning($"[Servidor] Cliente {clientId} (slot {slot}) intentó {accion} fuera de su turno.");
            return false;
        }

        return true;
    }

    private List<int> ObtenerManoDeCliente(ulong clientId)
    {
        if (!manoPorCliente.TryGetValue(clientId, out List<int> mano))
        {
            mano = new List<int>();
            manoPorCliente[clientId] = mano;
        }
        return mano;
    }

    private void AgregarCartaAManoDeCliente(ulong clientId, int cardId)
    {
        ObtenerManoDeCliente(clientId).Add(cardId);
    }
}