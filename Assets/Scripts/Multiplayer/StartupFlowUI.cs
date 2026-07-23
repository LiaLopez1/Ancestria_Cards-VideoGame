using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controla el arranque del jugador con dos caminos posibles:
///
/// 1) CREAR SALA (host): boton "Crear sala" -> login -> pedir nick (siempre,
///    es "oneshot") -> LobbyManager crea la sala -> entra directo al GameScene,
///    donde espera a que otros se unan.
///
/// 2) UNIRSE (guest): boton "Unirse" -> login -> pedir nick (siempre) ->
///    se muestra la lista de salas disponibles -> al elegir una, LobbyManager
///    se une -> entra al GameScene.
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

    private const int MinNickLength = 3;
    private const int MaxNickLength = 16;

    // Recuerda que boton se presiono originalmente (Crear sala o Unirse),
    // para saber que hacer una vez el nick quede confirmado.
    private bool intentaSerHost;

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
        IniciarLogin();
    }

    public void OnUnirseButtonPressed()
    {
        intentaSerHost = false;
        IniciarLogin();
    }

    private void IniciarLogin()
    {
        crearSalaButton.interactable = false;
        unirseButton.interactable = false;
        SetEstadoInicio("Conectando...");
        authManager.Login();
    }

    private void HandleLoginSuccess()
    {
        // Cada partida es "oneshot": siempre se pide el nick, sin importar
        // si el jugador ya tenia uno guardado de una partida anterior.
        ShowOnly(panelNickname);
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
        VolverAlInicio();
    }

    private void VolverAlInicio()
    {
        // Invalida el nick actual: al volver, el campo queda vacio, asi que
        // la proxima vez hay que escribir uno nuevo de verdad antes de poder
        // confirmar (no queda el nick anterior precargado).
        if (nicknameInput != null) nicknameInput.text = string.Empty;
        if (nicknameErrorText != null) nicknameErrorText.gameObject.SetActive(false);

        crearSalaButton.interactable = true;
        unirseButton.interactable = true;
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