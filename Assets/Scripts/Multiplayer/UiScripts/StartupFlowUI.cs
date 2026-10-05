using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Controla el arranque del jugador. El login arranca solo al mostrarse este
/// menu (ya no espera a que aprieten un boton) porque hace falta saber si
/// el tutorial esta completo ANTES de habilitar los botones correctos.
///
/// Hay 3 botones en la pantalla de inicio:
///
/// 1) CREAR SALA (host, online): boton "Crear sala" ->
///    - si el tutorial NO esta completo: carga la escena del tutorial
///      directo (sin nick, sin sala real - es practica en solitario).
///    - si ya esta completo: pedir nick (siempre, es "oneshot") ->
///      LobbyManager crea la sala -> entra directo al GameScene, donde
///      espera a que otros se unan.
///
/// 2) UNIRSE (guest, online): solo habilitado si el tutorial ya esta completo.
///    boton "Unirse" -> pedir nick (siempre) -> se muestra la lista de
///    salas disponibles -> al elegir una, LobbyManager se une -> entra
///    al GameScene.
///
/// 3) MODO OFFLINE (100% local): no usa PlayFab ni Relay. Pide nick ->
///    LobbyManager arranca un host local -> entra al GameScene en solitario.
///    Siempre esta disponible, incluso si el login online falla.
///
/// Si se detecta que no hay conexion, "Crear sala" y "Unirse" se desactivan
/// y se avisa en el texto de estado. El jugador puede usar "Modo offline" o
/// "Reintentar" (este ultimo es opcional).
/// </summary>
public class StartupFlowUI : MonoBehaviour
{
    // authManager y lobbyManager YA NO se arrastran en el Inspector: ambos son
    // singletons persistentes (DontDestroyOnLoad). Una referencia arrastrada
    // en el Editor se re-resuelve contra la escena recien cargada cada vez que
    // esta se recarga (por ejemplo al volver por una desconexion) - apuntando
    // a una copia nueva que se autodestruye por el blindaje anti-duplicados,
    // no al objeto original que sigue vivo de verdad. Por eso se resuelven por
    // .Instance en Start(), que siempre apunta al objeto que realmente persiste.
    private PlayFabAuthManager authManager;
    private LobbyManager lobbyManager;

    [Header("Panel: inicio")]
    [SerializeField] private GameObject panelInicio;
    [SerializeField] private Button crearSalaButton;
    [SerializeField] private Button unirseButton;
    [SerializeField] private Button jugarOfflineButton;
    [Tooltip("Opcional. Aparece solo cuando no hay conexion, para volver a intentar el login online.")]
    [SerializeField] private Button reintentarButton;
    [SerializeField] private TMP_Text estadoInicioText;

    [Header("Panel: nickname")]
    [SerializeField] private GameObject panelNickname;
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private Button confirmNicknameButton;
    [SerializeField] private TMP_Text nicknameErrorText;
    [SerializeField] private Slider sliderCrearSala;

    [Header("Panel: lista de salas (solo para Unirse)")]
    [SerializeField] private GameObject panelListaSalas;
    [SerializeField] private RectTransform listaSalasContent;
    [SerializeField] private GameObject filaSalaPrefab;
    [SerializeField] private TMP_Text estadoUnirseText;
    [SerializeField] private Slider sliderUnirse;

    [Header("Panel: aviso de desconexion (se muestra encima del menu)")]
    [SerializeField] private GameObject panelAvisoDesconexion;
    [SerializeField] private TMP_Text avisoDesconexionText;
    [SerializeField] private Button cerrarAvisoButton;

    [Header("Tutorial (jugador nuevo)")]
    [Tooltip("Nombre de la escena del tutorial - se carga en vez de crear una sala si el jugador todavia no lo completo.")]
    [SerializeField] private string nombreEscenaTutorial = "Tutorial";

    private const int MinNickLength = 3;
    private const int MaxNickLength = 16;
    private const string TextoSinConexion = "No hay conexión para jugar en línea.";

    // Recuerda que boton se presiono originalmente (Crear sala o Unirse),
    // para saber que hacer una vez el nick quede confirmado.
    // En modo offline siempre es true (se comporta como host local).
    private bool intentaSerHost;

    // true una vez que el login (con el estado del tutorial ya conocido)
    // se resolvio con exito - antes de eso, ningun boton debe hacer nada
    // mas que reintentar el login.
    private bool sesionIniciada;

    private void Start()
    {
        authManager = PlayFabAuthManager.Instance;
        lobbyManager = LobbyManager.Instance;

        ShowOnly(panelInicio);

        if (nicknameErrorText != null) nicknameErrorText.gameObject.SetActive(false);
        if (panelAvisoDesconexion != null) panelAvisoDesconexion.SetActive(false);
        if (reintentarButton != null) reintentarButton.gameObject.SetActive(false);

        lobbyManager.RegistrarReferenciasUI(
            listaSalasContent, filaSalaPrefab,
            nicknameErrorText, sliderCrearSala,
            estadoUnirseText, sliderUnirse
        );

        MostrarMensajePendienteSiExiste();

        authManager.OnLoginSuccess += HandleLoginSuccess;
        authManager.OnLoginFailed += HandleLoginFailed;
        authManager.OnDisplayNameUpdated += HandleDisplayNameUpdated;

        // El login YA NO espera a que el jugador apriete un boton - arranca
        // solo apenas se muestra el menu, porque necesitamos saber si ya
        // completo el tutorial ANTES de poder habilitar los botones
        // correctos (un jugador nuevo solo debe ver "Crear sala" habilitado).
        if (!string.IsNullOrEmpty(authManager.PlayFabId))
        {
            // Ya nos habiamos logueado antes en esta misma sesion (por
            // ejemplo, al volver del tutorial o de una partida offline) -
            // no hace falta repetirlo.
            HandleLoginSuccess();
        }
        else
        {
            IniciarLoginInicial();
        }
    }

    private void MostrarMensajePendienteSiExiste()
    {
        if (string.IsNullOrEmpty(NetworkBootstrap.MensajePendiente)) return;

        avisoDesconexionText.text = NetworkBootstrap.MensajePendiente;
        panelAvisoDesconexion.SetActive(true);

        NetworkBootstrap.LimpiarMensajePendiente();
    }

    public void OnCerrarAvisoPressed()
    {
        panelAvisoDesconexion.SetActive(false);
    }

    private void OnDestroy()
    {
        if (authManager == null) return;

        authManager.OnLoginSuccess -= HandleLoginSuccess;
        authManager.OnLoginFailed -= HandleLoginFailed;
        authManager.OnDisplayNameUpdated -= HandleDisplayNameUpdated;
    }

    public void OnCrearSalaButtonPressed()
    {
        if (authManager.ModoOffline) return;

        intentaSerHost = true;

        if (!sesionIniciada)
        {
            IniciarLoginInicial();
            return;
        }

        if (!authManager.TutorialCompletado)
        {
            // Jugador nuevo: en vez del flujo normal (nick -> crear sala),
            // lo mandamos directo al tutorial - todavia no hace falta nick
            // ni sala real, es una practica en solitario.
            SceneManager.LoadScene(nombreEscenaTutorial);
            return;
        }

        ShowOnly(panelNickname);
    }

    public void OnUnirseButtonPressed()
    {
        if (authManager.ModoOffline) return;

        intentaSerHost = false;

        if (!sesionIniciada)
        {
            IniciarLoginInicial();
            return;
        }

        ShowOnly(panelNickname);
    }

    /// <summary>
    /// Boton "Modo offline": juego 100% local, sin PlayFab ni Relay.
    /// Activa el modo offline en el AuthManager (eso dispara
    /// HandleLoginSuccess) y sigue el flujo normal: nick -> CrearSala
    /// (que en modo offline arranca el host local) -> juego.
    /// </summary>
    public void OnJugarOfflinePressed()
    {
        authManager.IniciarModoOffline();
        intentaSerHost = true;
        ShowOnly(panelNickname);
    }

    /// <summary>
    /// Boton "Reintentar" (opcional): borra el estado de sesion y fuerza un
    /// intento real de login, ignorando el chequeo de red del dispositivo.
    /// </summary>
    public void OnReintentarPressed()
    {
        sesionIniciada = false;
        authManager.ReiniciarSesion();
        IniciarLoginInicial(forzar: true);
    }

    private void IniciarLoginInicial(bool forzar = false)
    {
        crearSalaButton.interactable = false;
        unirseButton.interactable = false;
        if (reintentarButton != null) reintentarButton.gameObject.SetActive(false);
        SetEstadoInicio("Conectando...");
        authManager.Login(forzar);
    }

    private void HandleLoginSuccess()
    {
        sesionIniciada = true;
        ActualizarBotonesInicio();
        SetEstadoInicio(TextoEstadoConexion());
    }

    /// <summary>
    /// "Crear sala" siempre esta disponible una vez logueado online (si el
    /// tutorial no esta completo, igual lo lleva ahi - ver
    /// OnCrearSalaButtonPressed). "Unirse" queda deshabilitado hasta completar
    /// el tutorial: un jugador nuevo no deberia poder entrar a la sala de
    /// otro todavia. En modo offline ambos quedan deshabilitados.
    /// </summary>
    private void ActualizarBotonesInicio()
    {
        bool online = sesionIniciada && !authManager.ModoOffline;

        crearSalaButton.interactable = online;
        unirseButton.interactable = online && authManager.TutorialCompletado;

        ActualizarBotonesExtra();
    }

    /// <summary>
    /// El boton offline siempre esta disponible. "Reintentar" solo aparece
    /// cuando no hay conexion (modo offline activo o login fallido por red).
    /// </summary>
    private void ActualizarBotonesExtra()
    {
        if (jugarOfflineButton != null) jugarOfflineButton.interactable = true;

        if (reintentarButton != null)
        {
            reintentarButton.gameObject.SetActive(authManager.ModoOffline || authManager.ErrorDeConexion);
        }
    }

    private string TextoEstadoConexion()
    {
        bool sinConexion = authManager.ModoOffline || (authManager.ErrorDeConexion && !sesionIniciada);
        return sinConexion ? TextoSinConexion : string.Empty;
    }

    private void HandleLoginFailed(string error)
    {
        Debug.LogWarning($"[StartupFlowUI] Login fallido: {error}");

        if (authManager.ErrorDeConexion)
        {
            // Sin conexion: no tiene sentido dejar los botones online activos.
            crearSalaButton.interactable = false;
            unirseButton.interactable = false;
            SetEstadoInicio(TextoSinConexion);
        }
        else
        {
            // Otro tipo de error (no es de red): se mantiene el comportamiento
            // de siempre, el jugador puede volver a intentar con los botones.
            crearSalaButton.interactable = true;
            unirseButton.interactable = true;
            SetEstadoInicio("No se pudo conectar. Intenta de nuevo.");
        }

        ActualizarBotonesExtra();
    }

    public void OnConfirmNicknamePressed()
    {
        string nick = nicknameInput.text.Trim();

        if (nick.Length < MinNickLength || nick.Length > MaxNickLength)
        {
            ShowNicknameError($"El nick debe tener entre {MinNickLength} y {MaxNickLength} caracteres.");
            return;
        }

        confirmNicknameButton.interactable = false;
        authManager.SetDisplayName(nick);
    }

    private void HandleDisplayNameUpdated()
    {
        if (intentaSerHost)
        {
            // Host: se crea la sala y se entra directo al GameScene,
            // ahi mismo se espera a que se unan los demas jugadores.
            // (En modo offline, LobbyManager arranca el host local.)
            lobbyManager.CrearSala(onError: () => confirmNicknameButton.interactable = true);
        }
        else
        {
            // Guest: se muestra la lista de salas disponibles para elegir una.
            ShowOnly(panelListaSalas);
            lobbyManager.BuscarSalas();
        }
    }

    private void ShowNicknameError(string message)
    {
        if (nicknameErrorText == null) return;
        nicknameErrorText.text = message;
        nicknameErrorText.gameObject.SetActive(true);
    }

    public void OnAtrasDesdeNicknamePressed()
    {
        VolverAlInicio();
    }

    public void OnAtrasDesdeListaSalasPressed()
    {
        lobbyManager.DetenerBusquedaPeriodica();
        VolverAlInicio();
    }

    private void VolverAlInicio()
    {
        // Invalida el nick actual: al volver, el campo queda vacio, asi que
        // la proxima vez hay que escribir uno nuevo de verdad antes de poder
        // confirmar (no queda el nick anterior precargado).
        if (nicknameInput != null) nicknameInput.text = string.Empty;
        if (nicknameErrorText != null) nicknameErrorText.gameObject.SetActive(false);

        ActualizarBotonesInicio();
        SetEstadoInicio(TextoEstadoConexion());

        ShowOnly(panelInicio);
    }

    private void SetEstadoInicio(string message)
    {
        if (estadoInicioText != null) estadoInicioText.text = message;
    }

    private void ShowOnly(GameObject panelToShow)
    {
        panelInicio.SetActive(panelToShow == panelInicio);
        panelNickname.SetActive(panelToShow == panelNickname);
        panelListaSalas.SetActive(panelToShow == panelListaSalas);

        // Blindaje: cada vez que se muestra el panel del nick, el boton de
        // confirmar debe quedar interactuable si o si.
        if (panelToShow == panelNickname)
        {
            confirmNicknameButton.interactable = true;
        }
    }
}