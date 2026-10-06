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
/// - CrearSala(): usado por el camino de host -> al crear la sala, carga la
///   primera escena de jefe (la decide BossProgressionManager, al azar).
/// - BuscarSalas(): usado por el camino de guest -> llena la lista para elegir una sala;
///   al unirse a una, tambien carga la escena que el host ya tiene cargada.
///
/// Requiere que el jugador ya haya iniciado sesion (PlayFabAuthManager) y tenga
/// su EntityId/EntityType asignados, ya que la API de Lobby los necesita.
///
/// IMPORTANTE: Crear sala y Unirse tienen texto de estado y slider de progreso
/// COMPLETAMENTE INDEPENDIENTES entre si (cada uno en su propio panel).
/// </summary>
public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance { get; private set; }

    // authManager y networkBootstrap YA NO se arrastran en el Inspector: son
    // singletons persistentes, y una referencia arrastrada puede apuntar a una
    // copia que se autodestruye (ver explicacion en StartupFlowUI). Se resuelven
    // siempre por .Instance, que apunta al objeto que de verdad sigue vivo.
    private PlayFabAuthManager authManager => PlayFabAuthManager.Instance;
    private NetworkBootstrap networkBootstrap => NetworkBootstrap.Instance;

    [Header("Colores del relleno del slider (inicio -> fin)")]
    [SerializeField] private Color colorProgresoInicio = Color.red;
    [SerializeField] private Color colorProgresoFinal = Color.green;

    [Header("Animacion del slider")]
    [Tooltip("Que tan rapido se mueve el slider hacia el valor objetivo (unidades de 0 a 1 por segundo).")]
    [SerializeField] private float velocidadAnimacionSlider = 1.5f;

    [Header("Sondeo de salas disponibles")]
    [Tooltip("Cada cuantos segundos se vuelve a buscar salas mientras el jugador ve la lista.")]
    [Range(3f, 30f)]
    [SerializeField] private float intervaloBusquedaSalas = 8f;
    [Tooltip("Espera maxima entre reintentos cuando la busqueda falla (backoff).")]
    [SerializeField] private float esperaMaximaConBackoff = 60f;

    private Coroutine busquedaPeriodicaCoroutine;

    // Cada slider anima con su propia corrutina, independiente uno del otro.
    private Coroutine animacionSliderCrearSala;
    private Coroutine animacionSliderUnirse;

    // Estas referencias de UI YA NO se arrastran en el Inspector de este
    // componente: LobbyManager sobrevive los cambios de escena (DontDestroyOnLoad)
    // pero la UI del menu NO sobrevive (se recrea cada vez que se recarga la
    // escena, por ejemplo al volver por una desconexion). Por eso StartupFlowUI
    // se las entrega en tiempo de ejecucion via RegistrarReferenciasUI(), cada
    // vez que esa escena carga.
    private RectTransform listaSalasContent;
    private GameObject filaSalaPrefab;
    private TMP_Text estadoCrearSalaText;
    private Slider sliderCrearSala;
    private TMP_Text estadoUnirseText;
    private Slider sliderUnirse;

    /// <summary>
    /// StartupFlowUI llama esto en su propio Start(), cada vez que la escena
    /// de menu se carga (incluida la primera vez), para que LobbyManager
    /// siempre tenga referencias validas a la UI actual, sin importar cuantas
    /// veces se haya recargado la escena.
    /// </summary>
    public void RegistrarReferenciasUI(
        RectTransform listaSalasContentUI,
        GameObject filaSalaPrefabUI,
        TMP_Text estadoCrearSalaTextUI,
        Slider sliderCrearSalaUI,
        TMP_Text estadoUnirseTextUI,
        Slider sliderUnirseUI)
    {
        listaSalasContent = listaSalasContentUI;
        filaSalaPrefab = filaSalaPrefabUI;
        estadoCrearSalaText = estadoCrearSalaTextUI;
        sliderCrearSala = sliderCrearSalaUI;
        estadoUnirseText = estadoUnirseTextUI;
        sliderUnirse = sliderUnirseUI;

        if (sliderCrearSala != null) sliderCrearSala.gameObject.SetActive(false);
        if (sliderUnirse != null) sliderUnirse.gameObject.SetActive(false);
    }

    [Header("Escena de destino (solo para que el INVITADO espere la sincronizacion)")]
    [Tooltip("El host ya no carga esta escena: la primera escena de jefe la decide BossProgressionManager. " +
             "Se conserva solo porque LoadingScreenManager.WaitForSceneSync la recibe como parametro.")]
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

    // true cuando ya se limpiaron las salas viejas de esta cuenta en esta sesion.
    private bool limpiezaDeSalasHecha;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Debe sobrevivir el cambio de escena hacia GameScene: si no, se pierde
        // lobbyIdActual y el OnApplicationQuit ya no puede limpiar la sala al cerrar.
        DontDestroyOnLoad(gameObject);

        // El valor guardado en el Inspector pisa el del codigo: si quedo en 3 s
        // de una version anterior, se corrige aqui para no martillar la API.
        intervaloBusquedaSalas = Mathf.Max(intervaloBusquedaSalas, 10f);
    }

    public void CrearSala(System.Action onError = null)
    {
        SetEstadoCrearSala("Creando sala...", 0.1f);
        StartCoroutine(CrearSalaConLoading(onError));
    }

    private System.Collections.IEnumerator CrearSalaConLoading(System.Action onError)
    {
        // 1. Tapar la pantalla PRIMERO, antes de tocar PlayFab o Netcode
        yield return LoadingScreenManager.Instance.ShowLoading();

        // MODO OFFLINE: host local, sin PlayFab ni Relay.
        if (authManager.ModoOffline)
        {
            CrearSalaOffline(onError);
            yield break;
        }

        // 2. Ya tapado, arranca el flujo normal
        SetEstadoCrearSala("Creando sala...", 0.1f);
        LimpiarMisSalasAnteriores(() => CrearSalaInterno(onError));
    }

    /// <summary>
    /// Pide a BossProgressionManager la primera escena de la run (baraja los
    /// jefes de nuevo). Devuelve null si no se puede (no existe el manager o
    /// no hay escenas validas).
    /// </summary>
    private string ObtenerEscenaInicial()
    {
        if (BossProgressionManager.Instance == null)
        {
            Debug.LogError("[Lobby] No existe BossProgressionManager. Ponlo en la escena del menu (con DontDestroyOnLoad).");
            return null;
        }

        return BossProgressionManager.Instance.IniciarNuevaRun();
    }

    /// <summary>
    /// Modo offline: arranca un host local directo (sin Relay, sin lobby de
    /// PlayFab) y carga la escena de juego. Funciona sin internet.
    /// </summary>
    private void CrearSalaOffline(System.Action onError)
    {
        SetEstadoCrearSala("Iniciando partida sin conexión...", 0.5f);

        if (!networkBootstrap.IniciarHostOffline())
        {
            Debug.LogError("[Lobby] No se pudo iniciar el host offline.");
            FalloCrearSala("Error al iniciar la partida.", onError);
            return;
        }

        string escenaInicial = ObtenerEscenaInicial();
        if (string.IsNullOrEmpty(escenaInicial))
        {
            FalloCrearSala("No hay jefes configurados.", onError);
            return;
        }

        SetEstadoCrearSala("Iniciando partida sin conexión...", 1f);

        // Mismo metodo que el flujo online: el host carga la escena por Netcode.
        LoadingScreenManager.Instance.LoadNetworkScene(escenaInicial);
    }

    /// <summary>
    /// Camino comun para TODOS los fallos de "crear sala": apaga Netcode si
    /// el host ya habia arrancado (si no, el siguiente intento fallaria porque
    /// Netcode ya estaria escuchando), sale de la sala de PlayFab si ya se
    /// habia creado, destapa la pantalla de carga y avisa a la UI.
    /// </summary>
    private void FalloCrearSala(string mensaje, System.Action onError)
    {
        SalirDeSalaActual();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        SetEstadoCrearSala(mensaje);
        OcultarProgresoCrearSala();
        LoadingScreenManager.Instance.HideLoadingOnError();
        onError?.Invoke();
    }

    /// <summary>
    /// Limpia CUALQUIER sala anterior ligada a esta cuenta antes de crear o
    /// buscar una nueva - cubre dos casos:
    ///   - Salas donde soy DUEÑO: se vacian del todo (se saca a cualquier
    ///     miembro colgado y salgo yo tambien), porque son mias de verdad.
    ///   - Salas donde solo soy MIEMBRO (por ejemplo, me uni como invitado
    ///     en una sesion anterior y no llegue a salir limpio - Unity no
    ///     siempre llama a OnApplicationQuit al detener el Play Mode en el
    ///     Editor): SOLO salgo yo. Nunca toco a los demas miembros de una
    ///     sala que no es mia - podria seguir siendo una partida real de
    ///     otra persona.
    /// </summary>
    private void LimpiarMisSalasAnteriores(System.Action alTerminar)
    {
        BuscarYLimpiar("lobby/amOwner eq 'true'", esDueno: true, () =>
            BuscarYLimpiar("lobby/amMember eq 'true'", esDueno: false, alTerminar)
        );
    }

    private void BuscarYLimpiar(string filtro, bool esDueno, System.Action alTerminar)
    {
        var request = new FindLobbiesRequest { Filter = filtro };

        PlayFabMultiplayerAPI.FindLobbies(request,
            result =>
            {
                Debug.Log($"[Lobby] Limpieza ({filtro}): {result.Lobbies.Count} sala(s) anterior(es) encontrada(s).");

                if (result.Lobbies.Count == 0)
                {
                    alTerminar?.Invoke();
                    return;
                }

                int pendientes = result.Lobbies.Count;

                foreach (var lobbyVieja in result.Lobbies)
                {
                    System.Action onUnaTerminada = () =>
                    {
                        pendientes--;
                        if (pendientes == 0) alTerminar?.Invoke();
                    };

                    if (esDueno)
                    {
                        VaciarSalaVieja(lobbyVieja.LobbyId, onUnaTerminada);
                    }
                    else
                    {
                        // Solo miembro, no dueño - solo salgo yo, no toco a nadie mas.
                        SalirDeSalaVieja(lobbyVieja.LobbyId, onUnaTerminada);
                    }
                }
            },
            error =>
            {
                Debug.LogWarning($"[Lobby] No se pudo revisar salas anteriores ({filtro}): {error.GenerateErrorReport()}");
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

    private async void CrearSalaInterno(System.Action onError)
    {
        SetEstadoCrearSala("Creando sala...", 0.4f);

        string joinCode;
        try
        {
            joinCode = await networkBootstrap.IniciarHostYObtenerJoinCode();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Lobby] Error preparando Relay: {e}");
            FalloCrearSala("Error de conexion. Intenta de nuevo.", onError);
            return;
        }

        SetEstadoCrearSala("Creando sala...", 0.75f);

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

        PlayFabMultiplayerAPI.CreateLobby(request,
            result => OnCreateLobbySuccess(result, onError),
            error =>
            {
                Debug.LogError($"[Lobby] Error creando sala: {error.GenerateErrorReport()}");
                FalloCrearSala("Error: " + error.ErrorMessage, onError);
            });
    }

    private void OnCreateLobbySuccess(CreateLobbyResult result, System.Action onError)
    {
        lobbyIdActual = result.LobbyId;
        connectionStringActual = result.ConnectionString;

        Debug.Log($"[Lobby] Creada. LobbyId: {lobbyIdActual}, ConnectionString: {connectionStringActual}");

        // El host ya esta conectado por Netcode (arrancado dentro de
        // IniciarHostYObtenerJoinCode). Es el host quien controla la carga de
        // escena para que se sincronice automaticamente con quien se una despues.
        string escenaInicial = ObtenerEscenaInicial();
        if (string.IsNullOrEmpty(escenaInicial))
        {
            // Sala creada pero sin jefes que jugar: se deshace todo (sale de
            // la sala, apaga Netcode, destapa la pantalla).
            FalloCrearSala("No hay jefes configurados.", onError);
            return;
        }

        SetEstadoCrearSala("Creando sala...", 1f);

        LoadingScreenManager.Instance.LoadNetworkScene(escenaInicial);
    }

    public void BuscarSalas()
    {
        if (authManager.ModoOffline)
        {
            SetEstadoUnirse("Sin conexión: no hay salas disponibles.");
            return;
        }

        SetEstadoUnirse("Preparando búsqueda...");

        // La limpieza de salas viejas son 2 busquedas + varias llamadas mas
        // (GetLobby, RemoveMember, LeaveLobby). Con hacerla UNA vez por
        // sesion basta: despues, SalirDeSalaActual ya sale de la sala al volver.
        if (limpiezaDeSalasHecha)
        {
            IniciarBusquedaPeriodica();
            return;
        }

        LimpiarMisSalasAnteriores(() =>
        {
            limpiezaDeSalasHecha = true;
            IniciarBusquedaPeriodica();
        });
    }

    /// <summary>
    /// Arranca (o reinicia) el sondeo periodico de salas disponibles. No es
    /// tiempo real de verdad (eso requeriria activar conexiones en tiempo real
    /// de PlayFab), pero busca de nuevo cada pocos segundos mientras el
    /// jugador esta viendo la lista, dando el mismo efecto practico.
    /// </summary>
    private void IniciarBusquedaPeriodica()
    {
        DetenerBusquedaPeriodica();
        busquedaPeriodicaCoroutine = StartCoroutine(BusquedaPeriodicaCoroutine());
    }

    /// <summary>
    /// Se debe llamar apenas se deja de ver la lista de salas (al presionar
    /// Atras, o al empezar a unirse a una) para no seguir gastando llamadas
    /// de red de fondo sin necesidad - sobre todo porque LobbyManager
    /// sobrevive el cambio de escena, y si no se detiene, seguiria buscando
    /// salas incluso ya adentro de la partida.
    /// </summary>
    public void DetenerBusquedaPeriodica()
    {
        if (busquedaPeriodicaCoroutine != null)
        {
            StopCoroutine(busquedaPeriodicaCoroutine);
            busquedaPeriodicaCoroutine = null;
        }
    }

    /// <summary>
    /// Sondeo periodico con BACKOFF REAL: cuando una busqueda falla (por
    /// ejemplo, por limite de llamadas de la API), la espera crece cada vez
    /// (x2, hasta esperaMaximaConBackoff) en vez de insistir al mismo ritmo.
    /// IMPORTANTE: los errores NO llaman a IniciarBusquedaPeriodica() - eso
    /// destruiria esta corrutina (y su espera) y reiniciaria la busqueda al
    /// instante, justo lo contrario de lo que se quiere.
    /// </summary>
    private System.Collections.IEnumerator BusquedaPeriodicaCoroutine()
    {
        int erroresSeguidos = 0;

        // Pausa inicial: justo antes se acaban de hacer las llamadas de
        // limpieza, y pegar otra busqueda de inmediato suma al limite de la API.
        yield return new WaitForSecondsRealtime(2f);

        while (true)
        {
            // Si la UI del menu ya no existe (por ejemplo, ya estamos en la
            // partida), no hay nada que actualizar: se corta el sondeo.
            if (listaSalasContent == null)
            {
                busquedaPeriodicaCoroutine = null;
                yield break;
            }

            bool solicitudEnCurso = true;
            bool tuvoError = false;

            PlayFabMultiplayerAPI.FindLobbies(new FindLobbiesRequest(),
                result =>
                {
                    solicitudEnCurso = false;
                    OnFindLobbiesSuccess(result);
                },
                error =>
                {
                    solicitudEnCurso = false;
                    tuvoError = true;
                    Debug.LogWarning($"[Lobby] Error al buscar salas: {error.GenerateErrorReport()}");
                }
            );

            // Esperamos a que la solicitud actual termine antes de decidir
            // cuanto esperar - asi nunca se acumulan pedidos en paralelo. Si
            // no responde en 15 s se da por fallida para no quedar colgado.
            float inicio = Time.unscaledTime;
            yield return new WaitUntil(() => !solicitudEnCurso || Time.unscaledTime - inicio > 15f);
            if (solicitudEnCurso) tuvoError = true;

            float espera;
            if (!tuvoError)
            {
                erroresSeguidos = 0;
                espera = intervaloBusquedaSalas;
            }
            else
            {
                // Backoff exponencial: 2x, 4x, 8x... el intervalo, con tope.
                erroresSeguidos++;
                espera = Mathf.Min(intervaloBusquedaSalas * Mathf.Pow(2f, erroresSeguidos), esperaMaximaConBackoff);
                SetEstadoUnirse($"No se pudo actualizar la lista. Reintentando en {Mathf.CeilToInt(espera)} s...");
                Debug.LogWarning($"[Lobby] Sondeo con error ({erroresSeguidos} seguido(s)): se espera {espera}s antes de reintentar.");
            }

            yield return new WaitForSecondsRealtime(espera);
        }
    }

    private void OnFindLobbiesSuccess(FindLobbiesResult result)
    {
        // La UI pudo destruirse mientras la respuesta venia en camino.
        if (listaSalasContent == null) return;

        Debug.Log($"[Lobby] FindLobbies devolvio {result.Lobbies.Count} sala(s).");

        SetEstadoUnirse($"{result.Lobbies.Count} sala(s) encontradas.");

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

            var itemUI = fila.GetComponent<SalaListItemUI>();
            string connString = lobby.ConnectionString;

            itemUI.Configurar(
                nombreSala: $"Juego de {hostNick}",
                jugadoresLabel: $"{lobby.CurrentPlayers}/{lobby.MaxPlayers}",
                alUnirse: () => OnUnirseASalaPressed(connString)
            );
        }
    }

    private void OnUnirseASalaPressed(string connectionString)
    {
        SetEstadoUnirse("Uniendose a la sala...", 0.3f);
        StartCoroutine(UnirseConLoading(connectionString));
    }

    private System.Collections.IEnumerator UnirseConLoading(string connectionString)
    {
        // Tapar la pantalla PRIMERO, antes de tocar nada de PlayFab
        yield return LoadingScreenManager.Instance.ShowLoading();

        // Recién ahí, ya tapado, el flujo de siempre
        DetenerBusquedaPeriodica();
        DeshabilitarBotonesDeSalas();

        var request = new JoinLobbyRequest
        {
            ConnectionString = connectionString,
            MemberEntity = new EntityKey { Id = authManager.EntityId, Type = authManager.EntityType }
        };

        PlayFabMultiplayerAPI.JoinLobby(request, OnJoinLobbySuccess, OnLobbyErrorUnirse);
    }

    private void OnJoinLobbySuccess(JoinLobbyResult result)
    {
        lobbyIdActual = result.LobbyId;
        Debug.Log($"[Lobby] Unido correctamente a la sala {lobbyIdActual}. Buscando datos de conexion...");
        SetEstadoUnirse("Uniendose a la sala...", 0.6f);

        // JoinLobbyResult no trae el LobbyData, hay que pedirlo aparte.
        PlayFabMultiplayerAPI.GetLobby(new GetLobbyRequest { LobbyId = lobbyIdActual }, OnGetLobbyParaUnirse, OnLobbyErrorUnirse);
    }

    private async void OnGetLobbyParaUnirse(GetLobbyResult result)
    {
        if (result.Lobby.LobbyData == null || !result.Lobby.LobbyData.ContainsKey(LobbyDataKeyRelayJoinCode))
        {
            Debug.LogError("[Lobby] La sala no tiene join code de Relay guardado.");
            SetEstadoUnirse("Error: la sala no tiene datos de conexion.");
            RehabilitarBotonesDeSalas();
            OcultarProgresoUnirse();
            IniciarBusquedaPeriodica();
            LoadingScreenManager.Instance.HideLoadingOnError();
            return;
        }

        string joinCode = result.Lobby.LobbyData[LobbyDataKeyRelayJoinCode];

        try
        {
            await networkBootstrap.UnirseComoClienteConJoinCode(joinCode);
            Debug.Log("[Lobby] Conectado via Relay/Netcode como cliente.");
            SetEstadoUnirse("Uniendose a la sala...", 1f);
            // No hace falta cargar la escena manualmente: Netcode sincroniza
            // al cliente automaticamente con la escena que el host ya cargo.
            LoadingScreenManager.Instance.WaitForSceneSync(gameSceneName);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Lobby] Error conectando via Relay: {e}");
            SetEstadoUnirse("Error de conexion. Intenta de nuevo.");
            RehabilitarBotonesDeSalas();
            OcultarProgresoUnirse();
            IniciarBusquedaPeriodica();
            LoadingScreenManager.Instance.HideLoadingOnError();
        }
    }

    private void DeshabilitarBotonesDeSalas()
    {
        if (listaSalasContent == null) return;

        foreach (Transform fila in listaSalasContent)
        {
            var boton = fila.GetComponentInChildren<Button>();
            if (boton != null) boton.interactable = false;
        }
    }

    private void RehabilitarBotonesDeSalas()
    {
        if (listaSalasContent == null) return;

        foreach (Transform fila in listaSalasContent)
        {
            var boton = fila.GetComponentInChildren<Button>();
            if (boton != null) boton.interactable = true;
        }
    }

    private void OnApplicationQuit()
    {
        SalirDeSalaActual();
    }

    /// <summary>
    /// Sale de la sala actual en PlayFab, si hay alguna. Se usa tanto al cerrar
    /// el juego (OnApplicationQuit) como cuando Netcode detecta que se perdio
    /// la conexion con el host y hay que volver al menu.
    ///
    /// Nota: esto es "mejor esfuerzo". Si el proceso se cierra de golpe, la
    /// llamada de red puede no completarse a tiempo. Aun asi, PlayFab tiene un
    /// TTL de 1 hora que limpia la sala aunque esta llamada no llegue a tiempo.
    ///
    /// Siempre usamos LeaveLobby (sea host o invitado): DeleteLobby es exclusivo
    /// para entidades game_server. En salas client-owned, cuando el ultimo
    /// miembro se sale, PlayFab borra la sala automaticamente.
    /// </summary>
    public void SalirDeSalaActual()
    {
        if (string.IsNullOrEmpty(lobbyIdActual)) return;

        string lobbyASalir = lobbyIdActual;
        lobbyIdActual = null;

        PlayFabMultiplayerAPI.LeaveLobby(new LeaveLobbyRequest
        {
            LobbyId = lobbyASalir,
            MemberEntity = new EntityKey { Id = authManager.EntityId, Type = authManager.EntityType }
        }, null, null);
    }

    /// <summary>
    /// Errores del camino de Unirse (JoinLobby, GetLobby). Rehabilita los
    /// botones y reanuda el sondeo UNA vez (el sondeo periodico maneja sus
    /// propios errores con backoff, sin pasar por aqui).
    /// El error de CrearSala se maneja en FalloCrearSala.
    /// </summary>
    private void OnLobbyErrorUnirse(PlayFabError error)
    {
        SetEstadoUnirse("Error: " + error.ErrorMessage);
        Debug.LogError($"[Lobby] Error: {error.GenerateErrorReport()}");
        RehabilitarBotonesDeSalas();
        OcultarProgresoUnirse();
        IniciarBusquedaPeriodica();
        LoadingScreenManager.Instance.HideLoadingOnError();
    }

    // ---------- Estado y progreso: CREAR SALA ----------

    private void SetEstadoCrearSala(string mensaje, float? progreso = null)
    {
        if (estadoCrearSalaText != null)
        {
            estadoCrearSalaText.text = mensaje;
            estadoCrearSalaText.gameObject.SetActive(true);
        }
        ActualizarSlider(sliderCrearSala, progreso, ref animacionSliderCrearSala);
    }

    private void OcultarProgresoCrearSala()
    {
        if (sliderCrearSala != null) sliderCrearSala.gameObject.SetActive(false);
    }

    // ---------- Estado y progreso: UNIRSE ----------

    private void SetEstadoUnirse(string mensaje, float? progreso = null)
    {
        if (estadoUnirseText != null) estadoUnirseText.text = mensaje;
        ActualizarSlider(sliderUnirse, progreso, ref animacionSliderUnirse);
    }

    private void OcultarProgresoUnirse()
    {
        if (sliderUnirse != null) sliderUnirse.gameObject.SetActive(false);
    }

    // ---------- Comun a ambos sliders ----------

    private void ActualizarSlider(Slider slider, float? progreso, ref Coroutine animacionActual)
    {
        if (!progreso.HasValue || slider == null) return;

        slider.gameObject.SetActive(true);

        if (animacionActual != null) StopCoroutine(animacionActual);
        animacionActual = StartCoroutine(AnimarSlider(slider, progreso.Value));
    }

    private System.Collections.IEnumerator AnimarSlider(Slider slider, float objetivo)
    {
        while (slider != null && !Mathf.Approximately(slider.value, objetivo))
        {
            slider.value = Mathf.MoveTowards(slider.value, objetivo, velocidadAnimacionSlider * Time.deltaTime);
            ActualizarColorRelleno(slider, slider.value);
            yield return null;
        }

        if (slider == null) yield break;

        slider.value = objetivo;
        ActualizarColorRelleno(slider, objetivo);
    }

    private void ActualizarColorRelleno(Slider slider, float valor)
    {
        if (slider.fillRect == null) return;

        var fillImage = slider.fillRect.GetComponent<Image>();
        if (fillImage != null)
        {
            fillImage.color = Color.Lerp(colorProgresoInicio, colorProgresoFinal, valor);
        }
    }
}
