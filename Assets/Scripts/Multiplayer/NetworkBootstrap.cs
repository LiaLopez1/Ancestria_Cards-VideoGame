using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Maneja la capa de conexion en tiempo real (Unity Relay + Netcode for GameObjects).
/// Esto es DISTINTO de PlayFab Lobby: PlayFab solo maneja quien esta en la sala,
/// esto es lo que realmente sincroniza el juego entre los jugadores conectados.
///
/// Debe vivir en la primera escena y sobrevivir el cambio a GameScene. Usa el
/// mismo patron singleton que PlayFabAuthManager: si al recargar la escena de
/// menu se crea una copia nueva, esa copia se autodestruye (la que sobrevive
/// de verdad es la original, marcada DontDestroyOnLoad).
/// </summary>
public class NetworkBootstrap : MonoBehaviour
{
    public static NetworkBootstrap Instance { get; private set; }

    [SerializeField] private NetworkManager networkManager;
    // LobbyManager se resuelve por .Instance (no arrastrado en el Inspector) por
    // el mismo motivo explicado en StartupFlowUI: una referencia arrastrada no
    // sobrevive a una recarga de escena.

    [Header("Escena a la que volver si se pierde la conexion")]
    [SerializeField] private string escenaMenuInicial = "Menu";

    // Mensaje que StartupFlowUI debe mostrar apenas recargue la escena de menu
    // (por ejemplo, "El dueno de la sala se desconecto"). Estatico porque la
    // instancia de StartupFlowUI se recrea de cero al recargar la escena.
    public static string MensajePendiente { get; private set; }

    private const int MaxJugadoresInvitados = 2; // 3 totales: host + 2 invitados
    private const int TotalSlots = MaxJugadoresInvitados + 1;
    private const string TipoConexion = "dtls";

    // Slot (0=host, 1=invitado1, 2=invitado2) por orden de conexion de ESTA
    // sesion de hosting - a diferencia de OwnerClientId (que Netcode NO
    // reinicia, ni siquiera dentro de la misma sesion si alguien se
    // desconecta y se vuelve a conectar), esto usa un POOL de slots libres:
    // al desconectarse alguien, su slot vuelve a estar disponible para el
    // proximo que se conecte, en vez de seguir avanzando indefinidamente.
    private readonly Dictionary<ulong, int> slotsAsignados = new Dictionary<ulong, int>();
    private readonly SortedSet<int> slotsLibres = new SortedSet<int>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        networkManager.OnClientDisconnectCallback += ManejarDesconexion;
    }

    private void OnDestroy()
    {
        if (networkManager != null)
        {
            networkManager.OnClientDisconnectCallback -= ManejarDesconexion;
        }
    }

    public int ObtenerOAsignarSlot(ulong clientId)
    {
        if (slotsAsignados.TryGetValue(clientId, out int slotExistente))
        {
            return slotExistente;
        }

        int slot = slotsLibres.Count > 0 ? Min(slotsLibres) : TotalSlots - 1;
        slotsLibres.Remove(slot);
        slotsAsignados[clientId] = slot;
        return slot;
    }

    /// <summary>
    /// Solo consulta, no asigna - para que otros sistemas del servidor
    /// (como TurnManager) puedan validar "¿este clientId es el slot X?"
    /// sin arriesgarse a asignarle un slot nuevo por accidente.
    /// </summary>
    public bool TryObtenerSlot(ulong clientId, out int slot)
    {
        return slotsAsignados.TryGetValue(clientId, out slot);
    }

    /// <summary>
    /// Libera el slot de un jugador que se desconecto, para que el proximo
    /// que se una pueda ocuparlo (en vez de que los slots solo avancen).
    /// </summary>
    private void LiberarSlot(ulong clientId)
    {
        if (slotsAsignados.TryGetValue(clientId, out int slot))
        {
            slotsAsignados.Remove(clientId);
            slotsLibres.Add(slot);
            Debug.Log($"[Netcode] Slot {slot} liberado (cliente {clientId} se desconecto).");
        }
    }

    private static int Min(SortedSet<int> set)
    {
        using var enumerador = set.GetEnumerator();
        enumerador.MoveNext();
        return enumerador.Current;
    }

    /// <summary>
    /// Para el host: crea la asignacion de Relay, configura el transporte,
    /// arranca Netcode como host, y devuelve el join code para guardarlo
    /// en el LobbyData de PlayFab (asi los invitados lo pueden leer).
    /// </summary>
    public async Task<string> IniciarHostYObtenerJoinCode()
    {
        await AsegurarServiciosInicializados();

        // Reinicia los slots: esta es una sesion de hosting nueva, sin importar
        // cuantas veces se haya hosteado antes en este mismo proceso.
        slotsAsignados.Clear();
        slotsLibres.Clear();
        for (int i = 0; i < TotalSlots; i++)
        {
            slotsLibres.Add(i);
        }

        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxJugadoresInvitados);

        var transport = networkManager.GetComponent<UnityTransport>();
        transport.SetRelayServerData(new RelayServerData(allocation, TipoConexion));

        string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

        networkManager.StartHost();

        return joinCode;
    }

    /// <summary>
    /// Para el invitado: se une a la asignacion de Relay usando el join code
    /// leido del LobbyData de PlayFab, configura el transporte y arranca
    /// Netcode como cliente. No hace falta cargar la escena manualmente:
    /// Netcode sincroniza al cliente con la escena que el host ya cargo.
    /// </summary>
    public async Task UnirseComoClienteConJoinCode(string joinCode)
    {
        await AsegurarServiciosInicializados();

        JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

        var transport = networkManager.GetComponent<UnityTransport>();
        transport.SetRelayServerData(new RelayServerData(allocation, TipoConexion));

        networkManager.StartClient();
    }

    /// <summary>
    /// Se dispara cuando Netcode detecta que un cliente se desconecto.
    ///
    /// - En el SERVIDOR (host): esto se dispara por cada invitado que se va.
    ///   Aqui liberamos su slot para que el proximo que se una lo pueda usar.
    /// - En un CLIENTE (invitado): si SOMOS nosotros los que nos desconectamos,
    ///   casi siempre significa que el host se cayo o cerro la partida - ahi
    ///   hay que devolver a este jugador al menu.
    /// </summary>
    private void ManejarDesconexion(ulong clientId)
    {
        if (networkManager.IsServer)
        {
            LiberarSlot(clientId);
            return;
        }

        if (clientId != networkManager.LocalClientId) return;

        Debug.Log("[Netcode] Se perdio la conexion con el host. Volviendo al menu.");
        VolverAlMenuPorDesconexion("El dueño de la sala se desconectó.");
    }

    private void VolverAlMenuPorDesconexion(string mensaje)
    {
        MensajePendiente = mensaje;

        LobbyManager.Instance?.SalirDeSalaActual();

        if (networkManager.IsListening)
        {
            networkManager.Shutdown();
        }

        SceneManager.LoadScene(escenaMenuInicial);
    }

    /// <summary>
    /// Para cuando StartupFlowUI ya mostro el mensaje pendiente y hay que
    /// limpiarlo, asi no se vuelve a mostrar en la siguiente vuelta al menu.
    /// </summary>
    public static void LimpiarMensajePendiente()
    {
        MensajePendiente = null;
    }

    private async Task AsegurarServiciosInicializados()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            await UnityServices.InitializeAsync();
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }
}