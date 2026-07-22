using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

/// <summary>
/// Maneja la capa de conexion en tiempo real (Unity Relay + Netcode for GameObjects).
/// Esto es DISTINTO de PlayFab Lobby: PlayFab solo maneja quien esta en la sala,
/// esto es lo que realmente sincroniza el juego entre los jugadores conectados.
///
/// Debe vivir en la primera escena y sobrevivir el cambio a GameScene.
/// </summary>
public class NetworkBootstrap : MonoBehaviour
{
    [SerializeField] private NetworkManager networkManager;

    private const int MaxJugadoresInvitados = 2; // 3 totales: host + 2 invitados
    private const string TipoConexion = "dtls";

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
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