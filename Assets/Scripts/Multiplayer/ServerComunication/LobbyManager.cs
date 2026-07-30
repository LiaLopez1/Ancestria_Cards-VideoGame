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
///
/// IMPORTANTE: Crear sala y Unirse tienen texto de estado y slider de progreso
/// COMPLETAMENTE INDEPENDIENTES entre si (cada uno en su propio panel).
/// </summary>
public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance { get; private set; }

    [Header("Referencias")]
    [SerializeField] private PlayFabAuthManager authManager;
    [SerializeField] private NetworkBootstrap networkBootstrap;

    [Header("Colores del relleno del slider (inicio -> fin)")]
    [SerializeField] private Color colorProgresoInicio = Color.red;
    [SerializeField] private Color colorProgresoFinal = Color.green;

    [Header("Animacion del slider")]
    [Tooltip("Que tan rapido se mueve el slider hacia el valor objetivo (unidades de 0 a 1 por segundo).")]
    [SerializeField] private float velocidadAnimacionSlider = 1.5f;

    [Header("Sondeo de salas disponibles")]
    [Tooltip("Cada cuantos segundos se vuelve a buscar salas mientras el jugador ve la lista.")]
    [Range(1f, 10f)]
    [SerializeField] private float intervaloBusquedaSalas = 3f;

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
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Debe sobrevivir el cambio de escena hacia GameScene: si no, se pierde
        // lobbyIdActual y el OnApplicationQuit ya no puede limpiar la sala al cerrar.
        DontDestroyOnLoad(gameObject);
    }

    public void CrearSala(System.Action onError = null)
    {
        SetEstadoCrearSala("Creando sala...", 0.1f);
        LimpiarMisSalasAnteriores(() => CrearSalaInterno(onError));
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
            Debug.LogError($"[Lobby] Error preparando Relay: {e.Message}");
            SetEstadoCrearSala("Error de conexion. Intenta de nuevo.");
            OcultarProgresoCrearSala();
            onError?.Invoke();
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

        PlayFabMultiplayerAPI.CreateLobby(request, OnCreateLobbySuccess, error =>
        {
            SetEstadoCrearSala("Error: " + error.ErrorMessage);
            Debug.LogError($"[Lobby] Error creando sala: {error.GenerateErrorReport()}");
            OcultarProgresoCrearSala();
            onError?.Invoke();
        });
    }

    private void OnCreateLobbySuccess(CreateLobbyResult result)
    {
        lobbyIdActual = result.LobbyId;
        connectionStringActual = result.ConnectionString;

        Debug.Log($"[Lobby] Creada. LobbyId: {lobbyIdActual}, ConnectionString: {connectionStringActual}");

        SetEstadoCrearSala("Creando sala...", 1f);

        // El host ya esta conectado por Netcode (arrancado dentro de
        // IniciarHostYObtenerJoinCode). Es el host quien controla la carga de
        // escena para que se sincronice automaticamente con quien se una despues.
        LoadingScreenManager.Instance.LoadNetworkScene(gameSceneName);
    }

    public void BuscarSalas()
    {
        SetEstadoUnirse("Preparando búsqueda...");
        LimpiarMisSalasAnteriores(IniciarBusquedaPeriodica);
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

    private System.Collections.IEnumerator BusquedaPeriodicaCoroutine()
    {
        while (true)
        {
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
                    OnLobbyErrorUnirse(error);
                }
            );

            // Esperamos a que la solicitud actual termine antes de decidir
            // cuanto esperar - asi nunca se acumulan pedidos en paralelo si
            // la respuesta tarda mas que el intervalo configurado.
            yield return new WaitUntil(() => !solicitudEnCurso);

            if (tuvoError)
            {
                // Si fallo (por ejemplo, por limite de tasa de la API), en
                // vez de insistir al mismo ritmo esperamos bastante mas -
                // asi no seguimos golpeando la API mientras esta rechazando
                // pedidos.
                float esperaConBackoff = intervaloBusquedaSalas * 4f;
                Debug.LogWarning($"[Lobby] El sondeo de salas tuvo un error - se espera {esperaConBackoff}s antes de reintentar (en vez de los {intervaloBusquedaSalas}s normales).");
                yield return new WaitForSeconds(esperaConBackoff);
            }
            else
            {
                yield return new WaitForSeconds(intervaloBusquedaSalas);
            }
        }
    }

    private void OnFindLobbiesSuccess(FindLobbiesResult result)
    {
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
        DetenerBusquedaPeriodica();
        SetEstadoUnirse("Uniendose a la sala...", 0.3f);
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
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Lobby] Error conectando via Relay: {e.Message}");
            SetEstadoUnirse("Error de conexion. Intenta de nuevo.");
            RehabilitarBotonesDeSalas();
            OcultarProgresoUnirse();
            IniciarBusquedaPeriodica();
        }
    }

    private void DeshabilitarBotonesDeSalas()
    {
        foreach (Transform fila in listaSalasContent)
        {
            var boton = fila.GetComponentInChildren<Button>();
            if (boton != null) boton.interactable = false;
        }
    }

    private void RehabilitarBotonesDeSalas()
    {
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
    /// Errores del camino de Unirse (BuscarSalas, JoinLobby, GetLobby).
    /// El error de CrearSala se maneja directo en su propio lambda porque
    /// ademas necesita disparar el callback onError hacia StartupFlowUI.
    /// </summary>
    private void OnLobbyErrorUnirse(PlayFabError error)
    {
        SetEstadoUnirse("Error: " + error.ErrorMessage);
        Debug.LogError($"[Lobby] Error: {error.GenerateErrorReport()}");
        RehabilitarBotonesDeSalas();
        OcultarProgresoUnirse();
        IniciarBusquedaPeriodica();
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
        while (!Mathf.Approximately(slider.value, objetivo))
        {
            slider.value = Mathf.MoveTowards(slider.value, objetivo, velocidadAnimacionSlider * Time.deltaTime);
            ActualizarColorRelleno(slider, slider.value);
            yield return null;
        }

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