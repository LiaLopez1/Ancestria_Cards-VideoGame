using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controla las 2 pantallas del arranque (flujo "oneshot": se repite en cada partida):
/// 1) Panel con el boton "Jugar" -> hace login silencioso contra PlayFab
/// 2) Panel para pedir el nick -> se pide SIEMPRE, se guarda en PlayFab
///    (sobreescribiendo el anterior si ya existia) y se carga la escena del juego
///
/// Todavia no hay sistema de salas ni Netcode: el paso a la escena de juego
/// es un SceneManager.LoadScene normal.
///
/// Este script necesita una referencia directa a un PlayFabAuthManager
/// (arrastrala en el Inspector, campo "Auth Manager").
/// </summary>
public class StartupFlowUI : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private PlayFabAuthManager authManager;

    [Header("Paneles")]
    [SerializeField] private GameObject panelPlayButton;
    [SerializeField] private GameObject panelNickname;

    [Header("Panel: boton jugar")]
    [SerializeField] private Button playButton;
    [SerializeField] private TMP_Text playButtonStatusText;

    [Header("Panel: nickname")]
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private Button confirmNicknameButton;
    [SerializeField] private TMP_Text nicknameErrorText;

    [Header("Escena de destino")]
    [SerializeField] private string gameSceneName = "Game";

    private const int MinNickLength = 3;
    private const int MaxNickLength = 16;

    private void Start()
    {
        ShowOnly(panelPlayButton);

        if (nicknameErrorText != null) nicknameErrorText.gameObject.SetActive(false);

        authManager.OnLoginSuccess += HandleLoginSuccess;
        authManager.OnLoginFailed += HandleLoginFailed;
        authManager.OnDisplayNameUpdated += HandleDisplayNameUpdated;
    }

    private void OnDestroy()
    {
        if (authManager == null) return;

        authManager.OnLoginSuccess -= HandleLoginSuccess;
        authManager.OnLoginFailed -= HandleLoginFailed;
        authManager.OnDisplayNameUpdated -= HandleDisplayNameUpdated;
    }

    public void OnPlayPressed()
    {
        playButton.interactable = false;
        SetStatus("Conectando...");
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
        playButton.interactable = true;
        SetStatus("No se pudo conectar. Intenta de nuevo.");
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
        confirmNicknameButton.interactable = true;
        LoadGameScene();
    }

    private void LoadGameScene()
    {
        // Por ahora sin salas ni Netcode: solo confirmamos que el nick
        // quedo guardado y pasamos directo a la escena de juego.
        UnityEngine.SceneManagement.SceneManager.LoadScene(gameSceneName);
    }

    private void ShowNicknameError(string message)
    {
        if (nicknameErrorText == null) return;
        nicknameErrorText.text = message;
        nicknameErrorText.gameObject.SetActive(true);
    }

    private void SetStatus(string message)
    {
        if (playButtonStatusText != null) playButtonStatusText.text = message;
    }

    private void ShowOnly(GameObject panelToShow)
    {
        panelPlayButton.SetActive(panelToShow == panelPlayButton);
        panelNickname.SetActive(panelToShow == panelNickname);

        // Blindaje: cada vez que se muestra el panel del nick, el boton de
        // confirmar debe quedar interactuable si o si, sin depender de que
        // el Inspector haya quedado bien configurado a mano.
        if (panelToShow == panelNickname)
        {
            confirmNicknameButton.interactable = true;
        }
    }
}