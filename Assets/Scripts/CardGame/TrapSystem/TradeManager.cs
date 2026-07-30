using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Orquesta el intercambio de cartas entre dos jugadores humanos. SOLO el
/// jugador con el turno puede iniciarlo, y SOLO en la ventana entre robar y
/// descartar (misma condicion que TurnManager.CanDiscard(), pero validada
/// del lado del servidor via TurnManager.PuedeSolicitarIntercambio(slot)).
///
/// Como solo puede iniciarlo quien tiene el turno, y solo hay un turno
/// activo a la vez, esto garantiza que nunca hay mas de UN intercambio en
/// curso en toda la partida - por eso el estado se guarda en variables
/// simples (no una lista de sesiones concurrentes).
///
/// Flujo completo (3 paneles distintos, cada uno visto por un solo rol):
///   1. El iniciador pide la lista de companeros posibles (SolicitarListaDeJugadores)
///      -> TradePlayerSelectPanelUI.
///   2. Elige un slot -> se le pide a EL (y solo a el) que seleccione su
///      carta (SolicitarIntercambioConSlot) -> TradeInitiatorPromptUI.
///      Aca, clickear una carta la confirma de una, sin boton aparte.
///   3. El iniciador confirma su carta (ConfirmarCartaIniciador) -> se le
///      manda la propuesta al elegido -> TradeIncomingRequestUI, con
///      opcion de aceptar/rechazar.
///   4. Si rechaza -> TradeInitiatorPromptUI (reutilizado) le avisa al
///      iniciador, y se cancela todo.
///      Si acepta -> se le pide a EL (y solo a el) que seleccione su carta
///      -> TradeTargetSelectCardUI, que a diferencia del iniciador SI tiene
///      un boton "Confirmar" explicito (elegir la carta no la manda sola).
///   5. El elegido confirma (ConfirmarCartaObjetivo) -> el servidor ejecuta
///      el intercambio real en DeckManager, y ambos clientes reciben la
///      animacion de "mi carta se va, la del otro entra".
///
/// El boss nunca participa (no tiene clientId real, y las trampas/canjes
/// son cosas de jugadores humanos entre si).
/// </summary>

public class TradeManager : NetworkBehaviour
{
    [Header("Referencias")]
    [SerializeField] private DeckManager deckManager;
    [SerializeField] private TurnManager turnManager;
    [SerializeField] private GameManager gameManager;

    [Header("Mano local (de este cliente)")]
    [SerializeField] private HandManager handManager;

    [Header("UI local (de este cliente)")]
    [SerializeField] private TradeUIManager tradeUI;

    // ------------------- Estado SOLO en el servidor -------------------
    private bool intercambioEnProgreso;
    private ulong clienteIniciador;
    private ulong clienteObjetivo;
    private int cardIdIniciador = -1;
    private int cardIdObjetivo = -1;

    public override void OnNetworkSpawn()
    {
        if (gameManager != null)
        {
            gameManager.OnResultadoCambio += ManejarFinDePartida;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (gameManager != null)
        {
            gameManager.OnResultadoCambio -= ManejarFinDePartida;
        }
    }

    /// <summary>
    /// Se dispara en TODOS los clientes cuando cambia el resultado - SOLO
    /// el servidor actua aca (cancela cualquier intercambio a mitad de
    /// camino, para que no quede "trabado" bloqueando futuros intercambios
    /// despues de reiniciar). La parte visual (cerrar los paneles en cada
    /// cliente) la maneja TradeUIManager por su cuenta, suscrito al mismo evento.
    /// </summary>
    private void ManejarFinDePartida(ResultadoPartida resultado)
    {
        if (!IsServer || resultado == ResultadoPartida.EnCurso || !intercambioEnProgreso)
        {
            return;
        }

        Debug.Log("[Servidor] La partida terminó con un intercambio a mitad de camino - se cancela.");
        CancelarIntercambio();
    }

    // ---------------------------------------------------------------
    // Paso 1: pedir la lista de companeros posibles
    // ---------------------------------------------------------------

    /// <summary>Conectar al boton "Intercambio" del panel de trampas.</summary>
    public void SolicitarListaDeJugadores()
    {
        SolicitarListaDeJugadoresServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void SolicitarListaDeJugadoresServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong solicitante = rpcParams.Receive.SenderClientId;

        if (!ValidarPuedeIniciar(solicitante))
        {
            return;
        }

        List<int> slots = new List<int>();
        List<FixedString64Bytes> nombres = new List<FixedString64Bytes>();

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (clientId == solicitante)
            {
                continue;
            }

            if (!NetworkBootstrap.Instance.TryObtenerSlot(clientId, out int slot))
            {
                continue;
            }

            slots.Add(slot);
            nombres.Add(new FixedString64Bytes(ObtenerNombrePorSlot(slot)));
        }

        MostrarListaDeJugadoresClientRpc(slots.ToArray(), nombres.ToArray(), EnviarSoloA(solicitante));
    }

    [ClientRpc]
    private void MostrarListaDeJugadoresClientRpc(int[] slots, FixedString64Bytes[] nombres, ClientRpcParams rpcParams = default)
    {
        string[] nombresString = new string[nombres.Length];

        for (int i = 0; i < nombres.Length; i++)
        {
            nombresString[i] = nombres[i].ToString();
        }

        tradeUI?.MostrarListaDeJugadores(slots, nombresString);
    }

    // ---------------------------------------------------------------
    // Paso 2: elijo companero -> le pido al iniciador que seleccione su carta
    // ---------------------------------------------------------------

    /// <summary>Llamado por TradePlayerSelectPanelUI cuando el jugador elige con quien intercambiar.</summary>
    public void SolicitarIntercambioConSlot(int slotObjetivo)
    {
        SolicitarIntercambioServerRpc(slotObjetivo);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SolicitarIntercambioServerRpc(int slotObjetivo, ServerRpcParams rpcParams = default)
    {
        ulong solicitante = rpcParams.Receive.SenderClientId;

        if (!ValidarPuedeIniciar(solicitante))
        {
            return;
        }

        if (intercambioEnProgreso)
        {
            Debug.LogWarning("[Servidor] Ya hay un intercambio en curso - se ignora el pedido.");
            return;
        }

        if (!TryObtenerClientePorSlot(slotObjetivo, out ulong clienteDestino) || clienteDestino == solicitante)
        {
            Debug.LogWarning($"[Servidor] Slot objetivo invalido para intercambio: {slotObjetivo}.");
            return;
        }

        intercambioEnProgreso = true;
        clienteIniciador = solicitante;
        clienteObjetivo = clienteDestino;
        cardIdIniciador = -1;
        cardIdObjetivo = -1;

        Debug.Log($"[Servidor] Cliente {solicitante} inicia intercambio con cliente {clienteDestino} (slot {slotObjetivo}).");

        PedirSeleccionInicialClientRpc(EnviarSoloA(solicitante));
    }

    [ClientRpc]
    private void PedirSeleccionInicialClientRpc(ClientRpcParams rpcParams = default)
    {
        tradeUI?.MostrarSeleccionIniciador();
    }

    // ---------------------------------------------------------------
    // Paso 3: el iniciador confirma su carta (lo llama TradeInitiatorPromptUI)
    // ---------------------------------------------------------------

    public void ConfirmarCartaIniciador(int cardId)
    {
        ConfirmarCartaIniciadorServerRpc(cardId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ConfirmarCartaIniciadorServerRpc(int cardId, ServerRpcParams rpcParams = default)
    {
        ulong remitente = rpcParams.Receive.SenderClientId;

        if (!intercambioEnProgreso || remitente != clienteIniciador)
        {
            Debug.LogWarning("[Servidor] Confirmacion de carta (iniciador) fuera de contexto.");
            return;
        }

        if (!deckManager.ClienteTieneCarta(remitente, cardId))
        {
            Debug.LogWarning($"[Servidor] Cliente {remitente} intento ofrecer una carta que no tiene (cardId={cardId}).");
            return;
        }

        cardIdIniciador = cardId;

        FixedString64Bytes nombreIniciador = new FixedString64Bytes(ObtenerNombreDeCliente(clienteIniciador));

        MostrarPropuestaClientRpc(nombreIniciador, EnviarSoloA(clienteObjetivo));
    }

    [ClientRpc]
    private void MostrarPropuestaClientRpc(FixedString64Bytes nombreIniciador, ClientRpcParams rpcParams = default)
    {
        tradeUI?.MostrarPropuesta(nombreIniciador.ToString());
    }

    // ---------------------------------------------------------------
    // Paso 4: el elegido acepta o rechaza
    // ---------------------------------------------------------------

    public void ResponderPropuesta(bool acepta)
    {
        ResponderPropuestaServerRpc(acepta);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ResponderPropuestaServerRpc(bool acepta, ServerRpcParams rpcParams = default)
    {
        ulong remitente = rpcParams.Receive.SenderClientId;

        if (!intercambioEnProgreso || remitente != clienteObjetivo)
        {
            Debug.LogWarning("[Servidor] Respuesta de intercambio fuera de contexto.");
            return;
        }

        if (!acepta)
        {
            Debug.Log($"[Servidor] Cliente {remitente} rechazo el intercambio.");
            AvisarRechazoClientRpc(EnviarSoloA(clienteIniciador));
            CancelarIntercambio();
            return;
        }

        PedirSeleccionObjetivoClientRpc(EnviarSoloA(clienteObjetivo));
    }

    [ClientRpc]
    private void PedirSeleccionObjetivoClientRpc(ClientRpcParams rpcParams = default)
    {
        tradeUI?.MostrarSeleccionObjetivo();
    }

    [ClientRpc]
    private void AvisarRechazoClientRpc(ClientRpcParams rpcParams = default)
    {
        tradeUI?.MostrarRechazoAlIniciador();
    }

    // ---------------------------------------------------------------
    // Paso 5: el elegido confirma su carta (lo llama TradeTargetSelectCardUI)
    // ---------------------------------------------------------------

    public void ConfirmarCartaObjetivo(int cardId)
    {
        ConfirmarCartaObjetivoServerRpc(cardId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ConfirmarCartaObjetivoServerRpc(int cardId, ServerRpcParams rpcParams = default)
    {
        ulong remitente = rpcParams.Receive.SenderClientId;

        if (!intercambioEnProgreso || remitente != clienteObjetivo || cardIdIniciador < 0)
        {
            Debug.LogWarning("[Servidor] Confirmacion de carta (objetivo) fuera de contexto.");
            return;
        }

        if (!deckManager.ClienteTieneCarta(remitente, cardId))
        {
            Debug.LogWarning($"[Servidor] Cliente {remitente} intento ofrecer una carta que no tiene (cardId={cardId}).");
            return;
        }

        cardIdObjetivo = cardId;

        bool exito = deckManager.EjecutarIntercambio(clienteIniciador, cardIdIniciador, clienteObjetivo, cardIdObjetivo);

        if (!exito)
        {
            Debug.LogError("[Servidor] El intercambio fallo al ejecutarse (alguna de las dos cartas ya no estaba disponible).");
            CancelarIntercambio();
            return;
        }

        // A cada uno le llega la carta que le dio el OTRO.
        EjecutarAnimacionDeIntercambioClientRpc(cardIdObjetivo, EnviarSoloA(clienteIniciador));
        EjecutarAnimacionDeIntercambioClientRpc(cardIdIniciador, EnviarSoloA(clienteObjetivo));

        Debug.Log($"[Servidor] Intercambio finalizado entre {clienteIniciador} y {clienteObjetivo}.");

        CancelarIntercambio();
    }

    [ClientRpc]
    private void EjecutarAnimacionDeIntercambioClientRpc(int cardIdRecibido, ClientRpcParams rpcParams = default)
    {
        CardData cartaRecibida = CardDatabase.Instance.ObtenerPorId(cardIdRecibido);

        if (cartaRecibida == null)
        {
            Debug.LogError($"[Cliente] cardId recibido invalido en el intercambio: {cardIdRecibido}");
            return;
        }

        handManager?.EjecutarIntercambioVisual(cartaRecibida);

        tradeUI?.CerrarTodo();
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private void CancelarIntercambio()
    {
        intercambioEnProgreso = false;
        clienteIniciador = 0;
        clienteObjetivo = 0;
        cardIdIniciador = -1;
        cardIdObjetivo = -1;
    }

    /// <summary>SOLO servidor. ¿Ese cliente puede iniciar un intercambio ahora?</summary>
    private bool ValidarPuedeIniciar(ulong clientId)
    {
        if (!NetworkBootstrap.Instance.TryObtenerSlot(clientId, out int slot))
        {
            return false;
        }

        return turnManager.PuedeSolicitarIntercambio(slot);
    }

    /// <summary>SOLO servidor. Busca el clientId conectado que ocupa ese slot.</summary>
    private bool TryObtenerClientePorSlot(int slot, out ulong clientId)
    {
        foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (NetworkBootstrap.Instance.TryObtenerSlot(id, out int s) && s == slot)
            {
                clientId = id;
                return true;
            }
        }

        clientId = 0;
        return false;
    }

    /// <summary>SOLO servidor. Nombre real (via PlayerNamePanelsUI) para un slot dado.</summary>
    private string ObtenerNombrePorSlot(int slot)
    {
        return PlayerNamePanelsUI.Instance != null
            ? PlayerNamePanelsUI.Instance.ObtenerNombrePorSlot(slot)
            : $"Jugador {slot}";
    }

    private string ObtenerNombreDeCliente(ulong clientId)
    {
        return NetworkBootstrap.Instance.TryObtenerSlot(clientId, out int slot)
            ? ObtenerNombrePorSlot(slot)
            : "Jugador";
    }

    private static ClientRpcParams EnviarSoloA(ulong clientId)
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };
    }
}