using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// El mazo real (drawPile) SOLO existe en el servidor - los clientes nunca lo
/// tienen en memoria, para que ninguno pueda "ver" el orden de las cartas.
/// Lo mismo pasa con la pila de descarte real (discardPile): el servidor la
/// usa para reciclar cartas al mazo cuando este se queda vacio (mezcla todo
/// de vuelta y avisa a los clientes que limpien la mesa visual).
///
/// Cada cliente arma su propio mazo VISUAL (la pila boca abajo) segun un
/// contador sincronizado (cartasVisiblesEnMazo), no segun las cartas reales -
/// por eso el mazo visual nunca revela identidad, solo cantidad. Este
/// contador visual tiene un piso de 1 mientras haya algo reciclable en
/// discardPile, para que siempre quede una carta arrastrable con la que un
/// jugador humano pueda pedir la siguiente (ver ActualizarContadoresDeMazo).
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

    public int InitialHandSize => initialHandSize;

    [Header("Configuración del mazo")]
    [SerializeField] private int copiesPerCard = 4;

    [Header("Representación visual")]
    [SerializeField] private RectTransform deckArea;
    [SerializeField] private GameObject deckCardPrefab;
    [SerializeField] private Vector2 cardOffset = new Vector2(0.25f, -0.25f);

    [Header("Turno")]
    [SerializeField] private TurnManager turnManager;
    [SerializeField] private GameManager gameManager;
    [Tooltip("Necesario para poder chequear las reglas CartaInfiltrada / CartaInfiltrada2 al descartar.")]
    [SerializeField] private InfiltratedCardManager infiltratedCardManager;

    [Header("Boss")]
    [Tooltip("Se le avisa cuando la partida arranca, para que reparta su mano inicial igual que a un jugador más.")]
    [SerializeField] private BossManager bossManager;
    [SerializeField] private SuspicionManager suspicionManager;

    // Identidad reservada para el boss dentro de manoPorCliente - reutiliza
    // exactamente la misma estructura que ya usan los jugadores reales, así
    // el boss "es un jugador más" también en la mano, no solo en el turno.
    // ulong.MaxValue nunca lo va a asignar Netcode a un cliente real.
    private const ulong BOSS_ID = ulong.MaxValue;

    [Header("Mano del jugador")]
    [SerializeField] private HandManager handManager;

    [Header("Mesa de descarte")]
    [SerializeField] private TableManager tableManager;

    [Header("Iniciar partida (solo host)")]
    [SerializeField] private GameObject botonIniciarPartida;

    [Header("Audio")]
    [SerializeField] private SoundData ShuffleCardsSound;

    // Cartas disponibles para robar. SOLO tiene contenido real en el servidor.
    private readonly List<CardData> drawPile = new List<CardData>();

    // Cartas descartadas por todos los jugadores. SOLO tiene contenido real
    // en el servidor - igual que drawPile, es la fuente de verdad para
    // reciclar cartas cuando el mazo para robar se queda vacio.
    private readonly List<CardData> discardPile = new List<CardData>();

    // Objetos que representan las cartas apiladas en pantalla (genericos,
    // no revelan identidad - por eso se pueden construir igual en todos lados).
    private readonly List<GameObject> visualDeck = new List<GameObject>();

    // Sincronizado para que TODOS los clientes sepan si la partida ya
    // arranco de verdad - se usa tanto para bloquear un segundo "Iniciar
    // partida" como para decidir si la pila visual del mazo debe verse o
    // no (no debe aparecer hasta este momento).
    private readonly NetworkVariable<bool> partidaIniciada = new NetworkVariable<bool>(false);

    // Cuantas cartas quedan en el mazo - esto SI se sincroniza a todos,
    // para que el mazo visual se vea igual de "alto" en todas las pantallas.
    private readonly NetworkVariable<int> cartasEnMazo = new NetworkVariable<int>(0);

    // Cuantas cartas se MUESTRAN en la pila visual - normalmente igual a
    // cartasEnMazo, pero con un piso de 1 mientras haya algo reciclable en
    // discardPile. Sin esto, cuando drawPile llega a 0 la pila visual queda
    // vacia (0 objetos instanciados) y ningun jugador humano tiene de donde
    // arrastrar para pedir la siguiente carta - el gesto nunca ocurre, el
    // ServerRpc nunca se llama, y el reciclado (que SI esta bien implementado
    // del lado del servidor) nunca llega a dispararse. El boss no sufre esto
    // porque no depende de arrastrar nada.
    private readonly NetworkVariable<int> cartasVisiblesEnMazo = new NetworkVariable<int>(0);

    // Que cartas (por ID) tiene cada cliente EN SU MANO ahora mismo - no
    // solo cuantas, sino cuales exactamente. Necesario para poder validar
    // el descarte (¿de verdad tienes esa carta?) ademas de reglas de conteo.
    private readonly Dictionary<ulong, List<int>> manoPorCliente = new Dictionary<ulong, List<int>>();

    // Trackea la corrutina de reparto activa por cliente, para poder
    // cancelar cualquier reparto anterior que hubiera quedado corriendo
    // (por ejemplo, si la partida termino justo a mitad del reparto inicial)
    // antes de arrancar uno nuevo - sin esto, dos corrutinas podian terminar
    // agregando cartas a la misma mano, haciendo que el conteo se fuera
    // acumulando de mas en vez de resetear limpio en cada ronda.
    private readonly Dictionary<ulong, Coroutine> corrutinasDeReparto = new Dictionary<ulong, Coroutine>();

    public int CardsRemaining
    {
        get { return cartasEnMazo.Value; }
    }

    public override void OnNetworkSpawn()
    {
        cartasVisiblesEnMazo.OnValueChanged += (anterior, nuevo) => ActualizarMazoVisualSiCorresponde();
        partidaIniciada.OnValueChanged += (anterior, nuevo) => ActualizarMazoVisualSiCorresponde();

        if (IsServer)
        {
            BuildLogicalDeck();
            ShuffleDeck();
            ActualizarContadoresDeMazo();
        }

        if (gameManager != null)
        {
            gameManager.OnResultadoCambio += ManejarFinDePartida;
        }

        // El boton de iniciar partida solo lo puede usar el host.
        if (botonIniciarPartida != null)
        {
            botonIniciarPartida.SetActive(IsServer);
        }

        // El mazo NO debe verse hasta que la partida arranque de verdad -
        // por eso no usamos directamente cartasVisiblesEnMazo.Value aca.
        // Si un cliente se conecta cuando la partida YA esta en curso (por
        // ejemplo, reconectando a mitad de partida), esto ya lo muestra
        // correctamente de una, gracias a partidaIniciada.Value.
        ActualizarMazoVisualSiCorresponde();
    }

    public override void OnNetworkDespawn()
    {
        if (gameManager != null)
        {
            gameManager.OnResultadoCambio -= ManejarFinDePartida;
        }
    }

    /// <summary>
    /// SOLO actua del lado del servidor, apenas la partida termina (no
    /// espera a "Volver a jugar" ni al reinicio real). Oculta el mazo de
    /// nuevo (no debe verse hasta que el host arranque la ronda nueva) y
    /// limpia la mesa de descarte - el resto (mano, mazo real, turno,
    /// boss, sospecha) se reinicia recien cuando el host aprieta "Iniciar
    /// partida" (o "Siguiente ronda") de nuevo (ver EjecutarResetDeRonda).
    /// </summary>
    private void ManejarFinDePartida(ResultadoPartida resultado)
    {
        if (!IsServer || resultado == ResultadoPartida.EnCurso)
        {
            return;
        }

        partidaIniciada.Value = false;

        discardPile.Clear();
        ReiniciarMesaDeDescarteClientRpc();

        // Por si algun reparto quedo a mitad de camino justo cuando termino
        // la partida - se cancela para que no siga agregando cartas de mas
        // cuando ya no corresponde.
        foreach (Coroutine corrutina in corrutinasDeReparto.Values)
        {
            if (corrutina != null)
            {
                StopCoroutine(corrutina);
            }
        }

        corrutinasDeReparto.Clear();
    }

    /// <summary>
    /// Muestra la pila visual del mazo con el conteo real, o la mantiene
    /// vacia/oculta si la partida todavia no arranco.
    /// </summary>
    private void ActualizarMazoVisualSiCorresponde()
    {
        ActualizarMazoVisual(partidaIniciada.Value ? cartasVisiblesEnMazo.Value : 0);
    }

    /// <summary>
    /// Conectado al boton "Iniciar partida" (solo visible para el host).
    /// Reparte la mano inicial a TODOS los jugadores conectados en este
    /// momento (1, 2 o 3), simultaneamente, en vez de repartir uno por uno
    /// a medida que se conectan.
    /// </summary>
    /// <summary>
    /// Conectar ESTO al boton "Iniciar partida" en el Inspector. Antes
    /// decidia entre "primera vez" y "reinicio" mirando partidaIniciada,
    /// pero ahora ese flag tambien vuelve a false apenas termina una
    /// ronda (para ocultar el mazo) - asi que ya no sirve para distinguir
    /// "primera vez" de "reinicio". A diferencia de OnBotonSiguienteRondaPressed()
    /// (que conserva el marcador de rondas), esto arranca todo de cero via
    /// GameManager.ReiniciarPartidaCompleta().
    /// </summary>
    public void OnBotonIniciarPartidaPressed()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[DeckManager] Solo el host puede iniciar/reiniciar la partida.");
            return;
        }

        if (botonIniciarPartida != null)
        {
            botonIniciarPartida.SetActive(false);
        }

        // Partida COMPLETAMENTE nueva: ronda 1, marcador 0-0.
        if (gameManager != null)
        {
            gameManager.ReiniciarPartidaCompleta();
        }

        EjecutarResetDeRonda();
    }

    /// <summary>
    /// Conectar al boton "Siguiente ronda" (dentro del panel de "ronda
    /// ganada" de GameManager - SOLO visible/interactuable para el host).
    /// Avanza a la ronda siguiente CONSERVANDO el marcador de rondas
    /// ganadas, a diferencia de OnBotonIniciarPartidaPressed() que arranca
    /// todo de cero.
    /// </summary>
    public void OnBotonSiguienteRondaPressed()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[DeckManager] Solo el host puede avanzar a la siguiente ronda.");
            return;
        }

        if (gameManager != null)
        {
            gameManager.AvanzarRonda();
        }

        EjecutarResetDeRonda();
    }

    /// <summary>
    /// Reactiva el boton "Iniciar partida" (SOLO tiene efecto en el host) -
    /// lo llama GameRestartManager cuando el host aprieta "Volver a jugar"
    /// en el panel de resultado, para que la ronda nueva no arranque sola:
    /// el host tiene que apretar este boton de nuevo a mano.
    /// </summary>
    public void MostrarBotonIniciarPartida()
    {
        if (IsServer && botonIniciarPartida != null)
        {
            botonIniciarPartida.SetActive(true);
        }
    }

    /// <summary>
    /// LEGACY: si el boton del Inspector todavia apunta a este metodo en vez
    /// de OnBotonIniciarPartidaPressed, esto redirige igual para que
    /// funcione correctamente sin importar cual de los dos este conectado.
    /// </summary>
    public void OnIniciarPartidaPressed()
    {
        OnBotonIniciarPartidaPressed();
    }

    /// <summary>
    /// Rutina compartida por OnBotonIniciarPartidaPressed() y
    /// OnBotonSiguienteRondaPressed() - reconstruye y mezcla el mazo,
    /// limpia la mesa y las manos visuales de TODOS los clientes, reparte
    /// una mano nueva a cada uno (incluido el boss), reinicia el turno, y
    /// vuelve la sospecha a 0. NO toca ronda/marcador/resultado de
    /// GameManager - eso ya lo resolvio quien llamo a este metodo, cada
    /// uno a su manera (partida nueva vs. siguiente ronda).
    /// </summary>
    private void EjecutarResetDeRonda()
    {
        if (!IsServer)
        {
            return;
        }

        partidaIniciada.Value = true;

        drawPile.Clear();
        discardPile.Clear();
        manoPorCliente.Clear();

        BuildLogicalDeck();
        ShuffleDeck();
        ActualizarContadoresDeMazo();

        LimpiarManosClientRpc();
        ReiniciarMesaDeDescarteClientRpc();

        if (suspicionManager != null)
        {
            suspicionManager.ReiniciarSospecha();
        }

        int cantidadJugadores = NetworkManager.Singleton.ConnectedClientsIds.Count;

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            IniciarRepartoParaCliente(clientId);
        }

        if (turnManager != null)
        {
            turnManager.IniciarPrimerTurno(cantidadJugadores);
        }

        if (bossManager != null)
        {
            bossManager.IniciarManoInicial();
        }

        Debug.Log($"[Servidor] Ronda reiniciada. Repartiendo a {cantidadJugadores} jugador(es).");
    }

    /// <summary>Se ejecuta en TODOS los clientes: vacía la mano visual antes de que lleguen las cartas de la ronda nueva.</summary>
    [ClientRpc]
    private void LimpiarManosClientRpc()
    {
        if (handManager != null)
        {
            handManager.LimpiarManoCompleta();
        }
    }

    /// <summary>
    /// SOLO debe llamarse desde el servidor. Actualiza cartasEnMazo (el
    /// conteo real) y cartasVisiblesEnMazo (el que usa la pila en pantalla,
    /// con piso de 1 mientras haya algo reciclable) juntos, para que nunca
    /// queden desincronizados.
    /// </summary>
    private void ActualizarContadoresDeMazo()
    {
        cartasEnMazo.Value = drawPile.Count;

        cartasVisiblesEnMazo.Value = drawPile.Count > 0
            ? drawPile.Count
            : (discardPile.Count > 0 ? 1 : 0);
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
        //sonido
        ShuffleCardsSound.Play();
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
            visualCard.name = "DeckCard " + i; // cambiarlo despues solo por i

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
            ReciclarDescarteEnMazo();
        }

        if (drawPile.Count == 0)
        {
            Debug.LogWarning("[Servidor] No quedan cartas en el mazo (ni en la pila de descarte para remezclar).");
            return null;
        }

        int topCardIndex = drawPile.Count - 1;
        CardData drawnCard = drawPile[topCardIndex];
        drawPile.RemoveAt(topCardIndex);

        ActualizarContadoresDeMazo();

        Debug.Log("[Servidor] Carta robada: " + drawnCard.cardName + " | Cartas restantes: " + drawPile.Count);

        return drawnCard;
    }

    /// <summary>
    /// SOLO se llama desde el servidor, cuando el mazo para robar se queda
    /// sin cartas. Recicla toda la pila de descarte de vuelta al mazo, la
    /// mezcla, y avisa a todos los clientes que limpien la mesa (esas
    /// cartas ya no estan ahi, volvieron al mazo).
    /// </summary>
    private void ReciclarDescarteEnMazo()
    {
        if (discardPile.Count == 0)
        {
            return; // no hay nada para reciclar
        }

        Debug.Log($"[Servidor] Mazo vacío - remezclando {discardPile.Count} carta(s) de la pila de descarte.");

        drawPile.AddRange(discardPile);
        discardPile.Clear();

        ShuffleDeck();

        ActualizarContadoresDeMazo();

        ReiniciarMesaDeDescarteClientRpc();
    }

    /// <summary>
    /// Se ejecuta en TODOS los clientes: le pide a TableManager que vacíe
    /// la mesa, porque las cartas descartadas acaban de volver al mazo.
    /// </summary>
    [ClientRpc]
    private void ReiniciarMesaDeDescarteClientRpc()
    {
        if (tableManager != null)
        {
            tableManager.LimpiarMesa();
        }
    }

    /// <summary>
    /// SOLO corre en el servidor. Reparte la mano inicial a UN jugador en
    /// particular, mandandole UNICAMENTE sus propias cartas. OnIniciarPartidaPressed
    /// arranca una de estas corrutinas por jugador EN PARALELO, asi todos
    /// reciben sus cartas al mismo tiempo (no uno detras de otro).
    /// </summary>
    /// <summary>
    /// Arranca el reparto inicial para un cliente, cancelando primero
    /// cualquier corrutina de reparto anterior que hubiera quedado
    /// corriendo para ese mismo cliente - sin esto, si una ronda terminaba
    /// justo a mitad de un reparto, la corrutina vieja podia seguir viva y
    /// agregar cartas de mas a la mano en la ronda siguiente.
    /// </summary>
    private void IniciarRepartoParaCliente(ulong clientId)
    {
        if (corrutinasDeReparto.TryGetValue(clientId, out Coroutine corrutinaVieja) && corrutinaVieja != null)
        {
            StopCoroutine(corrutinaVieja);
        }

        corrutinasDeReparto[clientId] = StartCoroutine(RepartirManoAJugador(clientId));
    }

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

        if (drawPile.Count == 0 && discardPile.Count == 0)
        {
            Debug.LogWarning($"[Servidor] Cliente {clienteSolicitante} pidió robar pero no quedan cartas (ni en el mazo ni en el descarte).");
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

        CardData cartaDescartada = CardDatabase.Instance.ObtenerPorId(cardId);

        if (cartaDescartada != null)
        {
            discardPile.Add(cartaDescartada);
        }
        else
        {
            Debug.LogError($"[Servidor] cardId inválido al descartar: {cardId}");
        }

        Debug.Log($"[Servidor] Cliente {clienteSolicitante} descartó cardId={cardId}. Le quedan {mano.Count} carta(s).");

        MostrarCartaDescartadaClientRpc(cardId);

        if (turnManager != null && VictoryRules.SeCumple(turnManager.ReglaActiva, mano, ObtenerCategoriaInfiltradaActual()))
        {
            if (NetworkBootstrap.Instance.TryObtenerSlot(clienteSolicitante, out int slotGanador))
            {
                if (gameManager != null)
                {
                    gameManager.DeclararVictoriaJugador(slotGanador);
                }
                else
                {
                    Debug.LogError("[DeckManager] Se cumplió la regla de victoria, pero no se asignó GameManager en el Inspector - no se puede declarar la victoria.");
                }

                return; // no avanzamos el turno, la partida ya termino
            }
        }

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

    /// <summary>
    /// Atajo para no repetir esta condicion en cada chequeo de victoria -
    /// null si no hay categoria infiltrada elegida esta ronda (regla
    /// distinta, o todavia no se sorteo nada).
    /// </summary>
    private CardCategory? ObtenerCategoriaInfiltradaActual()
    {
        return infiltratedCardManager != null && infiltratedCardManager.HayCategoriaInfiltrada
            ? infiltratedCardManager.CategoriaInfiltrada
            : (CardCategory?)null;
    }

    private void AgregarCartaAManoDeCliente(ulong clientId, int cardId)
    {
        ObtenerManoDeCliente(clientId).Add(cardId);
    }

    // ---------- Intercambio entre jugadores (usado por TradeManager) ----------

    /// <summary>¿Ese cliente tiene esa carta en su mano ahora mismo? Válido en el servidor.</summary>
    public bool ClienteTieneCarta(ulong clientId, int cardId)
    {
        return manoPorCliente.TryGetValue(clientId, out List<int> mano) && mano.Contains(cardId);
    }

    /// <summary>
    /// SOLO debe llamarse desde el servidor (TradeManager), ya validado que
    /// ambos clientes tienen la carta que estan ofreciendo. Intercambia las
    /// cartas en manoPorCliente - no toca drawPile ni discardPile, porque
    /// estas cartas nunca salen de manos de jugadores. Devuelve false si en
    /// el momento de ejecutar, alguno de los dos ya no tiene la carta que
    /// habia ofrecido (por ejemplo, si alcanzo a descartarla mientras se
    /// esperaba la respuesta del otro).
    /// </summary>
    public bool EjecutarIntercambio(ulong clienteA, int cardIdA, ulong clienteB, int cardIdB)
    {
        if (!ClienteTieneCarta(clienteA, cardIdA) || !ClienteTieneCarta(clienteB, cardIdB))
        {
            return false;
        }

        List<int> manoA = ObtenerManoDeCliente(clienteA);
        List<int> manoB = ObtenerManoDeCliente(clienteB);

        manoA.Remove(cardIdA);
        manoB.Remove(cardIdB);

        manoA.Add(cardIdB);
        manoB.Add(cardIdA);

        Debug.Log($"[Servidor] Intercambio completado entre {clienteA} y {clienteB} (cardId {cardIdA} <-> cardId {cardIdB}).");

        return true;
    }

    // ---------- Boss: mismos mecanismos que un jugador, sin ServerRpc ----------
    // El boss corre del lado del servidor (BossManager), así que no necesita
    // pedir permiso por red como un cliente real - pero SÍ reusa exactamente
    // la misma mano por cliente (manoPorCliente[BOSS_ID]), la misma pila de
    // descarte, y el mismo ClientRpc de aviso a todos (MostrarCartaDescartadaClientRpc).

    /// <summary>
    /// Lógica compartida: roba y agrega a la mano del boss, sin tocar el
    /// turno para nada - la usan tanto el reparto inicial como el turno real.
    /// </summary>
    private CardData RobarYAgregarAlBoss()
    {
        CardData cartaRobada = DrawCard();

        if (cartaRobada != null)
        {
            AgregarCartaAManoDeCliente(BOSS_ID, cartaRobada.cardId);
        }

        return cartaRobada;
    }

    /// <summary>
    /// SOLO desde el servidor (BossManager, durante el reparto inicial).
    /// A propósito NO avisa a TurnManager - mismo motivo que
    /// RepartirManoAJugador() con los jugadores: repartir la mano inicial no
    /// es "robar en un turno", y avisarle a TurnManager acá adelantaría el
    /// estado a WaitingToDiscard antes de que arranque la partida de verdad.
    /// </summary>
    public CardData RobarCartaInicialBoss()
    {
        if (!IsServer)
        {
            return null;
        }

        return RobarYAgregarAlBoss();
    }

    /// <summary>
    /// SOLO desde el servidor (BossManager, durante SU TURNO real). A
    /// diferencia de RobarCartaInicialBoss(), esta SÍ avisa a TurnManager -
    /// es la acción real de robar en su turno, no el reparto inicial.
    /// </summary>
    public CardData RobarCartaParaBoss()
    {
        if (!IsServer)
        {
            return null;
        }

        CardData cartaRobada = RobarYAgregarAlBoss();

        if (cartaRobada != null && turnManager != null)
        {
            turnManager.NotificarRoboRealizado();
        }

        return cartaRobada;
    }

    /// <summary>Mano actual del boss (solo cardIds) - la usa BossStrategy para decidir.</summary>
    public List<int> ObtenerManoDelBoss()
    {
        return ObtenerManoDeCliente(BOSS_ID);
    }

    /// <summary>
    /// SOLO desde el servidor (BossManager). Descarta una carta puntual de la
    /// mano del boss - mismo camino que SolicitarDescarteServerRpc, pero sin
    /// la capa de ServerRpc (no hace falta validar un cliente que no existe).
    /// </summary>
    public void DescartarCartaDelBoss(int cardId)
    {
        if (!IsServer)
        {
            return;
        }

        List<int> mano = ObtenerManoDeCliente(BOSS_ID);

        if (!mano.Remove(cardId))
        {
            Debug.LogError($"[Servidor] El boss intentó descartar una carta que no tiene (cardId={cardId}).");
            return;
        }

        CardData cartaDescartada = CardDatabase.Instance.ObtenerPorId(cardId);

        if (cartaDescartada != null)
        {
            discardPile.Add(cartaDescartada);
        }
        else
        {
            Debug.LogError($"[Servidor] cardId inválido al descartar (boss): {cardId}");
        }

        Debug.Log($"[Servidor] El boss descartó cardId={cardId}. Le quedan {mano.Count} carta(s).");

        MostrarCartaDescartadaClientRpc(cardId);

        if (turnManager != null && VictoryRules.SeCumple(turnManager.ReglaActiva, mano, ObtenerCategoriaInfiltradaActual()))
        {
            if (gameManager != null)
            {
                gameManager.DeclararVictoriaBoss();
            }
            else
            {
                Debug.LogError("[DeckManager] El boss cumplió la regla de victoria, pero no se asignó GameManager en el Inspector - no se puede declarar la derrota.");
            }

            return; // no avanzamos el turno, la partida ya terminó
        }

        if (turnManager != null)
        {
            turnManager.NotificarDescarteRealizado();
        }
    }
}