using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Controla el arranque del jugador. El login arranca solo al mostrarse este
/// menu (ya no espera a que aprieten un boton) porque hace falta saber si
/// el tutorial esta completo ANTES de habilitar los botones correctos.
///
/// 1) CREAR SALA (host): boton "Crear sala" ->
///    - si el tutorial NO esta completo: carga la escena del tutorial
///      directo (sin nick, sin sala real - es practica en solitario).
///    - si ya esta completo: pedir nick (siempre, es "oneshot") ->
///      LobbyManager crea la sala -> entra directo al GameScene, donde
///      espera a que otros se unan.
///
/// 2) UNIRSE (guest): solo habilitado si el tutorial ya esta completo.
///    boton "Unirse" -> pedir nick (siempre) -> se muestra la lista de
///    salas disponibles -> al elegir una, LobbyManager se une -> entra
///    al GameScene.
///
/// Este script necesita una referencia directa a un PlayFabAuthManager y a un
/// LobbyManager (arrastralas en el Inspector).
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

    // Recuerda que boton se presiono originalmente (Crear sala o Unirse),
    // para saber que hacer una vez el nick quede confirmado.
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
            // ejemplo, al volver del tutorial) - no hace falta repetirlo.
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
        intentaSerHost = false;

        if (!sesionIniciada)
        {
            IniciarLoginInicial();
            return;
        }

        ShowOnly(panelNickname);
    }

    private void IniciarLoginInicial()
    {
        crearSalaButton.interactable = false;
        unirseButton.interactable = false;
        SetEstadoInicio("Conectando...");
        authManager.Login();
    }

    private void HandleLoginSuccess()
    {
        sesionIniciada = true;
        ActualizarBotonesInicio();
        SetEstadoInicio(string.Empty);
    }

    /// <summary>
    /// "Crear sala" siempre esta disponible una vez logueado (si el tutorial
    /// no esta completo, igual lo lleva ahi - ver OnCrearSalaButtonPressed).
    /// "Unirse" queda deshabilitado hasta completar el tutorial: un jugador
    /// nuevo no deberia poder entrar a la sala de otro todavia.
    /// </summary>
    private void ActualizarBotonesInicio()
    {
        crearSalaButton.interactable = sesionIniciada;
        unirseButton.interactable = sesionIniciada && authManager.TutorialCompletado;
    }

    private void HandleLoginFailed(string error)
    {
        crearSalaButton.interactable = true;
        unirseButton.interactable = true;
        SetEstadoInicio("No se pudo conectar. Intenta de nuevo.");
        Debug.LogWarning($"[StartupFlowUI] Login fallido: {error}");
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
        SetEstadoInicio(string.Empty);

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