using System.Collections.Generic;
using PlayFab;
using PlayFab.MultiplayerModels;
using TMPro;
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

    private string lobbyIdActual;
    private string connectionStringActual;

    public void CrearSala()
    {
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
            }
        };

        PlayFabMultiplayerAPI.CreateLobby(request, OnCreateLobbySuccess, OnLobbyError);
    }

    private void OnCreateLobbySuccess(CreateLobbyResult result)
    {
        lobbyIdActual = result.LobbyId;
        connectionStringActual = result.ConnectionString;

        Debug.Log($"[Lobby] Creada. LobbyId: {lobbyIdActual}, ConnectionString: {connectionStringActual}");

        // El host entra directo al GameScene y ahi espera a que se unan los demas.
        SceneManager.LoadScene(gameSceneName);
    }

    public void BuscarSalas()
    {
        SetEstado("Buscando salas...");
        PlayFabMultiplayerAPI.FindLobbies(new FindLobbiesRequest(), OnFindLobbiesSuccess, OnLobbyError);
    }

    private void OnFindLobbiesSuccess(FindLobbiesResult result)
    {
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
        Debug.Log($"[Lobby] Unido correctamente. LobbyId: {lobbyIdActual}");

        // El guest tambien entra directo al GameScene al unirse.
        SceneManager.LoadScene(gameSceneName);
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