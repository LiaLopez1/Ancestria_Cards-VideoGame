using System.Collections;
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
using UnityEngine.Networking;
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
///
/// VIGILANCIA DE INTERNET: en partidas ONLINE (host o invitado) se comprueba
/// cada pocos segundos que haya internet de verdad. Si falla varias veces
/// seguidas, se saca al jugador al menu. Esto es necesario porque Netcode
/// por si solo tarda mucho en notar la caida (y si el que pierde internet es
/// el HOST, Netcode nunca lanza una desconexion para el). En modo offline
/// la vigilancia NUNCA corre.
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

    [Header("Modo offline (host local, sin Relay)")]
    [SerializeField] private string direccionLocal = "127.0.0.1";
    [SerializeField] private ushort puertoLocal = 7777;

    [Header("Vigilancia de internet (solo partidas online)")]
    [SerializeField] private bool vigilarInternet = true;
    [Tooltip("URL liviana que responde si hay internet de verdad.")]
    [SerializeField] private string urlChequeoRed = "https://clients3.google.com/generate_204";
    [SerializeField] private float intervaloChequeoRed = 4f;
    [Tooltip("Chequeos fallidos SEGUIDOS antes de sacar al jugador al menu.")]
    [SerializeField] private int fallosParaDesconectar = 3;

    // Mensaje que StartupFlowUI debe mostrar apenas recargue la escena de menu
    // (por ejemplo, "El dueno de la sala se desconecto"). Estatico porque la
    // instancia de StartupFlowUI se recrea de cero al recargar la escena.
    public static string MensajePendiente { get; private set; }

    /// <summary>
    /// Marcar esto en true JUSTO ANTES de llamar a NetworkManager.Shutdown()
    /// por decision propia (por ejemplo, el boton "Volver al menu" dentro
    /// del juego) - sin esto, ManejarDesconexion() no puede distinguir
    /// "me desconecte yo a proposito" de "el host se cayo", y siempre
    /// asumia lo segundo, mostrando ese mensaje aunque fuera mentira.
    /// Se consume solo (vuelve a false) apenas se usa una vez.
    /// </summary>
    public bool SalidaVoluntaria { get; set; }

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

    private Coroutine vigilancia;

    // true si en ESTA sesion yo soy el host (online u offline). Se usa en vez
    // de networkManager.IsServer porque durante un Shutdown ese valor puede
    // cambiar justo cuando llega el aviso de desconexion.
    private bool soyHostDeEstaSesion;

    // true apenas se decide volver al menu por una desconexion: asi el
    // vigilante de internet y el callback de Netcode no se pisan entre si
    // (gana el primero, el otro se ignora).
    private bool saliendoAlMenu;

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
    /// Reinicia los slots: cada sesion de hosting es nueva, sin importar
    /// cuantas veces se haya hosteado antes en este mismo proceso.
    /// </summary>
    private void ReiniciarSlots()
    {
        slotsAsignados.Clear();
        slotsLibres.Clear();
        for (int i = 0; i < TotalSlots; i++)
        {
            slotsLibres.Add(i);
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

        SalidaVoluntaria = false;
        ReiniciarSlots();
        soyHostDeEstaSesion = true;
        saliendoAlMenu = false;

        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxJugadoresInvitados);

        var transport = networkManager.GetComponent<UnityTransport>();
        transport.SetRelayServerData(new RelayServerData(allocation, TipoConexion));

        string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

        networkManager.StartHost();

        IniciarVigilancia();

        return joinCode;
    }

    /// <summary>
    /// MODO OFFLINE: arranca un host local directo, SIN Relay ni servicios de
    /// Unity (no necesita internet). Solo escucha en la direccion local, asi
    /// que nadie mas puede conectarse. SetConnectionData tambien cambia el
    /// transporte a conexion directa, aunque antes se haya usado Relay.
    /// La vigilancia de internet se apaga: offline nunca debe cortarse.
    /// </summary>
    public bool IniciarHostOffline()
    {
        DetenerVigilancia();

        SalidaVoluntaria = false;
        ReiniciarSlots();
        soyHostDeEstaSesion = true;
        saliendoAlMenu = false;

        var transport = networkManager.GetComponent<UnityTransport>();
        transport.SetConnectionData(direccionLocal, puertoLocal);

        return networkManager.StartHost();
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

        SalidaVoluntaria = false;
        soyHostDeEstaSesion = false;
        saliendoAlMenu = false;

        JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

        var transport = networkManager.GetComponent<UnityTransport>();
        transport.SetRelayServerData(new RelayServerData(allocation, TipoConexion));

        networkManager.StartClient();

        IniciarVigilancia();
    }

    // ---------- Vigilancia de internet ----------

    private void IniciarVigilancia()
    {
        DetenerVigilancia();

        if (!vigilarInternet)
        {
            Debug.Log("[Red] Vigilancia de internet desactivada en el Inspector.");
            return;
        }

        Debug.Log($"[Red] Vigilancia de internet ACTIVADA (cada {intervaloChequeoRed}s, {fallosParaDesconectar} fallos seguidos = salir).");
        vigilancia = StartCoroutine(VigilarInternet());
    }

    private void DetenerVigilancia()
    {
        if (vigilancia != null)
        {
            StopCoroutine(vigilancia);
            vigilancia = null;
            Debug.Log("[Red] Vigilancia de internet detenida.");
        }
    }

    private IEnumerator VigilarInternet()
    {
        int fallos = 0;
        var espera = new WaitForSecondsRealtime(intervaloChequeoRed);

        while (true)
        {
            yield return espera;

            // Si Netcode ya no esta activo (salida normal), no hay nada que vigilar.
            if (!networkManager.IsListening)
            {
                Debug.Log("[Red] Netcode ya no esta activo, termina la vigilancia.");
                vigilancia = null;
                yield break;
            }

            bool hayInternet = false;

            if (Application.internetReachability != NetworkReachability.NotReachable)
            {
                using (var req = UnityWebRequest.Get(urlChequeoRed))
                {
                    req.timeout = 4;
                    yield return req.SendWebRequest();
                    hayInternet = req.result == UnityWebRequest.Result.Success;
                }
            }

            fallos = hayInternet ? 0 : fallos + 1;
            Debug.Log($"[Red] Chequeo de internet: {(hayInternet ? "OK" : "FALLO")} (fallos seguidos: {fallos}/{fallosParaDesconectar})");

            if (fallos >= fallosParaDesconectar)
            {
                Debug.LogWarning("[Red] Sin internet de forma sostenida. Volviendo al menu.");
                vigilancia = null;
                SalidaVoluntaria = true; // evita el mensaje falso de "el dueno se desconecto"
                VolverAlMenuPorDesconexion("Perdiste la conexión.");
                yield break;
            }
        }
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
        // Ya se decidio volver al menu (por ejemplo, lo hizo el vigilante de
        // internet): este aviso de Netcode es consecuencia de eso, se ignora.
        if (saliendoAlMenu) return;

        // ---- SOY EL HOST ----
        // Nunca debe ver "el host abandono": si lo que se cayo es mi propia
        // conexion, el mensaje es "Perdiste la conexion". Si se fue un
        // invitado, solo se libera su slot.
        if (soyHostDeEstaSesion)
        {
            if (clientId == networkManager.LocalClientId)
            {
                if (SalidaVoluntaria)
                {
                    SalidaVoluntaria = false;
                    return;
                }

                Debug.Log("[Netcode] El host perdio su propia conexion. Volviendo al menu.");
                DetenerVigilancia();
                VolverAlMenuPorDesconexion("Perdiste la conexión.");
                return;
            }

            LiberarSlot(clientId);
            return;
        }

        // ---- SOY INVITADO ----
        if (clientId != networkManager.LocalClientId) return;

        if (SalidaVoluntaria)
        {
            // Me fui por decision propia (por ejemplo, "Volver al menu") -
            // no es que el host se haya caido, no hace falta avisar nada.
            SalidaVoluntaria = false;
            return;
        }

        // Me desconectaron, pero Netcode no dice por que: puede ser que el
        // host se fue O que yo me quede sin internet. Se comprueba mi red.
        DetenerVigilancia();
        StartCoroutine(DeterminarCausaDeDesconexion());
    }

    /// <summary>
    /// Solo para invitados. Si yo tengo internet, la causa es que el host se
    /// fue ("El host ha abandonado la partida"). Si yo NO tengo internet, la
    /// causa soy yo ("Perdiste la conexion").
    /// </summary>
    private IEnumerator DeterminarCausaDeDesconexion()
    {
        bool hayInternet = false;

        if (Application.internetReachability != NetworkReachability.NotReachable)
        {
            using (var req = UnityWebRequest.Get(urlChequeoRed))
            {
                req.timeout = 4;
                yield return req.SendWebRequest();
                hayInternet = req.result == UnityWebRequest.Result.Success;
            }
        }

        if (hayInternet)
        {
            Debug.Log("[Netcode] Desconectado pero con internet: el host abandono la partida.");
            VolverAlMenuPorDesconexion("El host ha abandonado la partida.");
        }
        else
        {
            Debug.Log("[Netcode] Desconectado y sin internet: el invitado perdio su conexion.");
            VolverAlMenuPorDesconexion("Perdiste la conexión.");
        }
    }

    private void VolverAlMenuPorDesconexion(string mensaje)
    {
        if (saliendoAlMenu) return;
        saliendoAlMenu = true;

        Debug.Log($"[Red] Volviendo al menu. Mensaje: \"{mensaje}\"");

        MensajePendiente = mensaje;

        // Salir de la sala de PlayFab es "mejor esfuerzo": sin internet puede
        // fallar, y eso NUNCA debe impedir que el jugador vuelva al menu.
        try
        {
            LobbyManager.Instance?.SalirDeSalaActual();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Red] No se pudo salir de la sala de PlayFab (se ignora): {e.Message}");
        }

        try
        {
            if (networkManager.IsListening)
            {
                networkManager.Shutdown();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Red] Error al apagar Netcode (se ignora): {e.Message}");
        }

        if (!Application.CanStreamedLevelBeLoaded(escenaMenuInicial))
        {
            Debug.LogError($"[Red] La escena '{escenaMenuInicial}' no existe en Build Settings o el nombre no coincide: no se puede volver al menu.");
            return;
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