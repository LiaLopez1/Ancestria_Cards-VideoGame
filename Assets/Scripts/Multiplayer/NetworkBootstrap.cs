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
    [SerializeField] private LobbyManager lobbyManager;

    [Header("Escena a la que volver si se pierde la conexion")]
    [SerializeField] private string escenaMenuInicial = "Menu";

    // Mensaje que StartupFlowUI debe mostrar apenas recargue la escena de menu
    // (por ejemplo, "El dueno de la sala se desconecto"). Estatico porque la
    // instancia de StartupFlowUI se recrea de cero al recargar la escena.
    public static string MensajePendiente { get; private set; }

    private const int MaxJugadoresInvitados = 2; // 3 totales: host + 2 invitados
    private const string TipoConexion = "dtls";

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

    /// <summary>
    /// Para el host: crea la asignacion de Relay, configura el transporte,
    /// arranca Netcode como host, y devuelve el join code para guardarlo
    /// en el LobbyData de PlayFab (asi los invitados lo pueden leer).
    /// </summary>
    public async Task<string> IniciarHostYObtenerJoinCode()
    {
        await AsegurarServiciosInicializados();

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
    /// Se dispara cuando Netcode detecta que un cliente se desconecto. El
    /// servidor (host) ve este evento por cada invitado que se va, eso es
    /// normal y no debe hacer nada. Pero si SOMOS nosotros (el cliente local)
    /// quienes nos desconectamos, casi siempre significa que el host se cayo
    /// o cerro la partida - ahi hay que devolver a este jugador al menu.
    /// </summary>
    private void ManejarDesconexion(ulong clientId)
    {
        if (networkManager.IsServer) return;
        if (clientId != networkManager.LocalClientId) return;

        Debug.Log("[Netcode] El anfitrion se desconectó. Volviendo al menu.");
        VolverAlMenuPorDesconexion("El dueño de la sala se desconectó.");
    }

    private void VolverAlMenuPorDesconexion(string mensaje)
    {
        MensajePendiente = mensaje;

        lobbyManager?.SalirDeSalaActual();

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