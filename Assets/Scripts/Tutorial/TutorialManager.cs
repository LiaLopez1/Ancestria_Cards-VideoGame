using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Controla el FINAL del tutorial - no los pasos/contenido del tutorial en
/// si (eso lo maneja TutorialSequencer). Se suscribe solo a
/// TutorialSequencer.OnSecuenciaTerminada, asi que apenas se agota el
/// ultimo paso (el jugador le da "Siguiente" al ultimo video), esto se
/// dispara automaticamente sin necesidad de cablear nada mas a mano.
///
/// Al dispararse:
///   1. Guarda el progreso en PlayFab (PlayFabAuthManager.MarcarTutorialCompletado),
///      para que la proxima vez que este PC/cuenta inicie sesion, StartupFlowUI
///      ya vea el tutorial como completo y habilite "Unirse" normalmente.
///   2. Muestra el panel de felicitaciones ("Completaste el tutorial...").
///   3. Al presionar el boton de ese panel, vuelve a la escena del menu
///      principal.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    [Header("Panel de finalizacion")]
    [SerializeField] private GameObject panelCompletado;
    [SerializeField] private Button botonVolverAlMenu;

    [Header("Escena del menu principal")]
    [Tooltip("Nombre exacto de la escena a la que se vuelve al terminar - debe coincidir con la escena donde vive StartupFlowUI.")]
    [SerializeField] private string nombreEscenaMenu = "MainMenu";

    private void Awake()
    {
        if (panelCompletado != null)
        {
            panelCompletado.SetActive(false);
        }

        if (botonVolverAlMenu != null)
        {
            botonVolverAlMenu.onClick.AddListener(VolverAlMenu);
        }
    }

    private void Start()
    {
        // Start() (no OnEnable/Awake) para garantizar que TutorialSequencer
        // ya haya corrido su propio Awake y asignado Instance - Unity no
        // garantiza el orden de Awake/OnEnable entre distintos scripts,
        // pero si garantiza que TODOS los Awake de la escena ya corrieron
        // antes de que cualquier Start empiece.
        if (TutorialSequencer.Instance != null)
        {
            TutorialSequencer.Instance.OnSecuenciaTerminada += CompletarTutorial;
        }
        else
        {
            Debug.LogWarning("[TutorialManager] No se encontró TutorialSequencer.Instance - el panel de finalización no se va a mostrar automáticamente al terminar los pasos.");
        }
    }

    private void OnDestroy()
    {
        if (TutorialSequencer.Instance != null)
        {
            TutorialSequencer.Instance.OnSecuenciaTerminada -= CompletarTutorial;
        }
    }

    /// <summary>
    /// Se llama automaticamente cuando TutorialSequencer termina su
    /// secuencia de pasos (ver Start()) - tambien se puede llamar a mano
    /// si en algun momento hace falta saltear el tutorial desde otro lado.
    /// </summary>
    public void CompletarTutorial()
    {
        if (PlayFabAuthManager.Instance != null)
        {
            PlayFabAuthManager.Instance.MarcarTutorialCompletado();
        }
        else
        {
            Debug.LogError("[TutorialManager] No se encontró PlayFabAuthManager.Instance - no se pudo guardar el progreso del tutorial.");
        }

        if (panelCompletado != null)
        {
            panelCompletado.SetActive(true);
        }
    }

    private void VolverAlMenu()
    {
        SceneManager.LoadScene(nombreEscenaMenu);
    }

    /// <summary>
    /// Llamado cuando el jugador contesta que NO quiere ver el tutorial
    /// (TutorialIntroPromptUI) - marca el progreso en PlayFab, igual que
    /// CompletarTutorial(), pero SIN mostrar el panel de felicitaciones:
    /// va directo de vuelta al menú.
    /// </summary>
    public void SaltarTutorial()
    {
        if (PlayFabAuthManager.Instance != null)
        {
            PlayFabAuthManager.Instance.MarcarTutorialCompletado();
        }
        else
        {
            Debug.LogError("[TutorialManager] No se encontró PlayFabAuthManager.Instance - no se pudo guardar el progreso del tutorial.");
        }

        VolverAlMenu();
    }
}