using System.Collections.Generic;
using PlayFab;
using PlayFab.MultiplayerModels;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Maneja las salas (lobbies) de PlayFab: crear una sala publica nombrada
/// segun el nick del host ("Juego de <nick>"), buscar salas abiertas y unirse a una.
///
/// Lo llama StartupFlowUI una vez el nick ya quedo confirmado:
/// - CrearSala(): usado por el camino de host -> al crear la sala, carga el GameScene.
/// - BuscarSalas(): usado por el camino de guest -> llena la lista para elegir una sala;
///   al unirse a una, tambien carga el GameScene.
///
/// Requiere que el jugador ya haya iniciado sesion (PlayFabAuthManager) y tenga
/// su EntityId/EntityType asignados, ya que la API de Lobby los necesita.
/// </summary>
public class LobbyManager : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private PlayFabAuthManager authManager;
    [SerializeField] private NetworkBootstrap networkBootstrap;

    [Header("UI: lista de salas (panel de Unirse)")]
    [SerializeField] private RectTransform listaSalasContent;
    [SerializeField] private GameObject filaSalaPrefab;
    [SerializeField] private TMP_Text estadoText;

    [Header("Escena de destino")]
    [SerializeField] private string gameSceneName = "Game";

    private const int MaxJugadoresPorSala = 3;

    // PlayFab Lobby solo acepta nombres reservados para SearchData
    // (string_key1..string_key30, number_key1..number_key30), no nombres libres.
    // Usamos string_key1 para guardar el nick del host.
    private const string SearchKeyHostNick = "string_key1";

    // A diferencia de SearchData, LobbyData si acepta nombres libres.
    // Aqui guardamos el join code de Relay para que los invitados lo lean.
    private const string LobbyDataKeyRelayJoinCode = "RelayJoinCode";

    private string lobbyIdActual;
    private string connectionStringActual;

    private void Awake()
    {
        // Debe sobrevivir el cambio de escena hacia GameScene: si no, se pierde
        // lobbyIdActual y el OnApplicationQuit ya no puede limpiar la sala al cerrar.
        DontDestroyOnLoad(gameObject);
    }

    public void CrearSala()
    {
        SetEstado("Limpiando salas anteriores...");
        LimpiarMisSalasAnteriores(CrearSalaInterno);
    }

    /// <summary>
    /// Busca todas las salas de las que el jugador actual es dueno (sin importar
    /// el nick que tuvieran al crearlas) y las deja completamente vacias:
    /// primero saca a cualquier otro miembro colgado (RemoveMember) y despues
    /// sale el mismo (LeaveLobby). En salas client-owned, al quedar vacia,
    /// PlayFab la borra sola.
    /// </summary>
    private void LimpiarMisSalasAnteriores(System.Action alTerminar)
    {
        var request = new FindLobbiesRequest { Filter = "lobby/amOwner eq 'true'" };

        PlayFabMultiplayerAPI.FindLobbies(request,
            result =>
            {
                Debug.Log($"[Lobby] Limpieza: {result.Lobbies.Count} sala(s) propia(s) anterior(es) encontrada(s).");

                if (result.Lobbies.Count == 0)
                {
                    alTerminar?.Invoke();
                    return;
                }

                int pendientes = result.Lobbies.Count;

                foreach (var lobbyVieja in result.Lobbies)
                {
                    VaciarSalaVieja(lobbyVieja.LobbyId, () =>
                    {
                        pendientes--;
                        if (pendientes == 0) alTerminar?.Invoke();
                    });
                }
            },
            error =>
            {
                Debug.LogWarning($"[Lobby] No se pudo revisar salas anteriores: {error.GenerateErrorReport()}");
                alTerminar?.Invoke();
            }
        );
    }

    private void VaciarSalaVieja(string lobbyId, System.Action onDone)
    {
        PlayFabMultiplayerAPI.GetLobby(new GetLobbyRequest { LobbyId = lobbyId },
            result =>
            {
                var otrosMiembros = new List<Member>();
                foreach (var m in result.Lobby.Members)
                {
                    if (m.MemberEntity.Id != authManager.EntityId)
                    {
                        otrosMiembros.Add(m);
                    }
                }

                if (otrosMiembros.Count == 0)
                {
                    SalirDeSalaVieja(lobbyId, onDone);
                    return;
                }

                Debug.Log($"[Lobby] Sala {lobbyId}: sacando {otrosMiembros.Count} miembro(s) colgado(s).");

                int pendientesMiembros = otrosMiembros.Count;

                foreach (var miembroColgado in otrosMiembros)
                {
                    PlayFabMultiplayerAPI.RemoveMember(
                        new RemoveMemberFromLobbyRequest
                        {
                            LobbyId = lobbyId,
                            MemberEntity = miembroColgado.MemberEntity
                        },
                        removeResult =>
                        {
                            pendientesMiembros--;
                            if (pendientesMiembros == 0) SalirDeSalaVieja(lobbyId, onDone);
                        },
                        error =>
                        {
                            Debug.LogWarning($"[Lobby] No se pudo sacar miembro colgado de {lobbyId}: {error.GenerateErrorReport()}");
                            pendientesMiembros--;
                            if (pendientesMiembros == 0) SalirDeSalaVieja(lobbyId, onDone);
                        }
                    );
                }
            },
            error =>
            {
                Debug.LogWarning($"[Lobby] No se pudo revisar miembros de {lobbyId}: {error.GenerateErrorReport()}");
                onDone?.Invoke();
            }
        );
    }

    private void SalirDeSalaVieja(string lobbyId, System.Action onDone)
    {
        // DeleteLobby es exclusivo para entidades game_server. Como cliente,
        // la forma correcta de limpiar una sala propia es salir de ella (LeaveLobby):
        // en salas client-owned, al quedar vacia, PlayFab la borra sola.
        PlayFabMultiplayerAPI.LeaveLobby(
            new LeaveLobbyRequest
            {
                LobbyId = lobbyId,
                MemberEntity = new EntityKey { Id = authManager.EntityId, Type = authManager.EntityType }
            },
            leaveResult =>
            {
                Debug.Log($"[Lobby] Salida OK de sala vieja (deberia autoborrarse): {lobbyId}");
                onDone?.Invoke();
            },
            error =>
            {
                Debug.LogError($"[Lobby] FALLO al salir de sala vieja {lobbyId}: {error.GenerateErrorReport()}");
                onDone?.Invoke();
            }
        );
    }

    private async void CrearSalaInterno()
    {
        SetEstado("Preparando conexion...");

        string joinCode;
        try
        {
            joinCode = await networkBootstrap.IniciarHostYObtenerJoinCode();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Lobby] Error preparando Relay: {e.Message}");
            SetEstado("Error de conexion. Intenta de nuevo.");
            return;
        }

        SetEstado("Creando sala...");

        var miEntity = new EntityKey { Id = authManager.EntityId, Type = authManager.EntityType };

        var request = new CreateLobbyRequest
        {
            Owner = miEntity,
            MaxPlayers = MaxJugadoresPorSala,
            AccessPolicy = AccessPolicy.Public,
            OwnerMigrationPolicy = OwnerMigrationPolicy.None,
            UseConnections = false,
            Members = new List<Member>
            {
                new Member { MemberEntity = miEntity }
            },
            SearchData = new Dictionary<string, string>
            {
                { SearchKeyHostNick, authManager.DisplayName }
            },
            LobbyData = new Dictionary<string, string>
            {
                { LobbyDataKeyRelayJoinCode, joinCode }
            }
        };

        PlayFabMultiplayerAPI.CreateLobby(request, OnCreateLobbySuccess, OnLobbyError);
    }

    private void OnCreateLobbySuccess(CreateLobbyResult result)
    {
        lobbyIdActual = result.LobbyId;
        connectionStringActual = result.ConnectionString;

        Debug.Log($"[Lobby] Creada. LobbyId: {lobbyIdActual}, ConnectionString: {connectionStringActual}");

        // El host ya esta conectado por Netcode (arrancado dentro de
        // IniciarHostYObtenerJoinCode). Es el host quien controla la carga de
        // escena para que se sincronice automaticamente con quien se una despues.
        NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
    }

    public void BuscarSalas()
    {
        SetEstado("Buscando salas...");
        PlayFabMultiplayerAPI.FindLobbies(new FindLobbiesRequest(), OnFindLobbiesSuccess, OnLobbyError);
    }

    private void OnFindLobbiesSuccess(FindLobbiesResult result)
    {
        Debug.Log($"[Lobby] FindLobbies devolvio {result.Lobbies.Count} sala(s).");

        SetEstado($"{result.Lobbies.Count} sala(s) encontradas.");

        // Limpiar la lista anterior antes de mostrar los resultados nuevos.
        foreach (Transform child in listaSalasContent)
        {
            Destroy(child.gameObject);
        }

        foreach (var lobby in result.Lobbies)
        {
            string hostNick = (lobby.SearchData != null && lobby.SearchData.ContainsKey(SearchKeyHostNick))
                ? lobby.SearchData[SearchKeyHostNick]
                : "Jugador";

            GameObject fila = Instantiate(filaSalaPrefab, listaSalasContent);

            var label = fila.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.text = $"Juego de {hostNick}  ({lobby.CurrentPlayers}/{lobby.MaxPlayers})";
            }

            string connString = lobby.ConnectionString;
            var boton = fila.GetComponent<Button>();
            if (boton != null)
            {
                boton.onClick.AddListener(() => OnUnirseASalaPressed(connString));
            }
        }
    }

    private void OnUnirseASalaPressed(string connectionString)
    {
        SetEstado("Uniendose a la sala...");

        var request = new JoinLobbyRequest
        {
            ConnectionString = connectionString,
            MemberEntity = new EntityKey { Id = authManager.EntityId, Type = authManager.EntityType }
        };

        PlayFabMultiplayerAPI.JoinLobby(request, OnJoinLobbySuccess, OnLobbyError);
    }

    private void OnJoinLobbySuccess(JoinLobbyResult result)
    {
        lobbyIdActual = result.LobbyId;
        Debug.Log($"[Lobby] Unido correctamente a la sala {lobbyIdActual}. Buscando datos de conexion...");
        SetEstado("Conectando a la partida...");

        // JoinLobbyResult no trae el LobbyData, hay que pedirlo aparte.
        PlayFabMultiplayerAPI.GetLobby(new GetLobbyRequest { LobbyId = lobbyIdActual }, OnGetLobbyParaUnirse, OnLobbyError);
    }

    private async void OnGetLobbyParaUnirse(GetLobbyResult result)
    {
        if (result.Lobby.LobbyData == null || !result.Lobby.LobbyData.ContainsKey(LobbyDataKeyRelayJoinCode))
        {
            Debug.LogError("[Lobby] La sala no tiene join code de Relay guardado.");
            SetEstado("Error: la sala no tiene datos de conexion.");
            return;
        }

        string joinCode = result.Lobby.LobbyData[LobbyDataKeyRelayJoinCode];

        try
        {
            await networkBootstrap.UnirseComoClienteConJoinCode(joinCode);
            Debug.Log("[Lobby] Conectado via Relay/Netcode como cliente.");
            // No hace falta cargar la escena manualmente: Netcode sincroniza
            // al cliente automaticamente con la escena que el host ya cargo.
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Lobby] Error conectando via Relay: {e.Message}");
            SetEstado("Error de conexion. Intenta de nuevo.");
        }
    }

    private void OnApplicationQuit()
    {
        if (string.IsNullOrEmpty(lobbyIdActual)) return;

        // Nota: esto es "mejor esfuerzo". Al cerrar el juego (sobre todo en un build,
        // no tanto en el Editor) el proceso puede terminar antes de que la llamada de
        // red termine de completarse. Aun asi, PlayFab tiene un TTL de 1 hora que
        // limpia la sala aunque esta llamada no llegue a tiempo.
        //
        // Siempre usamos LeaveLobby (sea host o invitado): DeleteLobby es exclusivo
        // para entidades game_server. En salas client-owned, cuando el ultimo
        // miembro se sale, PlayFab borra la sala automaticamente.
        PlayFabMultiplayerAPI.LeaveLobby(new LeaveLobbyRequest
        {
            LobbyId = lobbyIdActual,
            MemberEntity = new EntityKey { Id = authManager.EntityId, Type = authManager.EntityType }
        }, null, null);
    }

    private void OnLobbyError(PlayFabError error)
    {
        SetEstado("Error: " + error.ErrorMessage);
        Debug.LogError($"[Lobby] Error: {error.GenerateErrorReport()}");
    }

    private void SetEstado(string mensaje)
    {
        if (estadoText != null) estadoText.text = mensaje;
    }
}