using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Controla el FINAL del tutorial - no los pasos/contenido del tutorial en
/// si (eso lo maneja TutorialSequencer). Se suscribe solo a
/// TutorialSequencer.OnSecuenciaTerminada, asi que apenas se agota el
/// ultimo paso (el jugador le da "Siguiente" al ultimo paso), esto se
/// dispara automaticamente sin necesidad de cablear nada mas a mano.
///
/// SIN panel de felicitaciones intermedio: al terminar el tutorial (o al
/// saltarlo con "No" desde TutorialIntroPromptUI), esto guarda el progreso
/// en PlayFab y va DIRECTO de vuelta al menu principal en el mismo click.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    [Header("Escena del menu principal")]
    [Tooltip("Nombre exacto de la escena a la que se vuelve al terminar - debe coincidir con la escena donde vive StartupFlowUI.")]
    [SerializeField] private string nombreEscenaMenu = "MainMenu";

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
            Debug.Log("[TutorialManager] Suscripto a TutorialSequencer.OnSecuenciaTerminada.");
        }
        else
        {
            Debug.LogWarning("[TutorialManager] No se encontró TutorialSequencer.Instance - no se va a volver al menú automáticamente al terminar los pasos.");
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
    /// si en algun momento hace falta completar el tutorial desde otro lado.
    /// </summary>
    public void CompletarTutorial()
    {
        Debug.Log("[TutorialManager] CompletarTutorial() ejecutado.");
        MarcarCompletadoYVolverAlMenu();
    }

    /// <summary>
    /// Llamado cuando el jugador contesta que NO quiere ver el tutorial
    /// (TutorialIntroPromptUI) - hace exactamente lo mismo que
    /// CompletarTutorial(): guarda el progreso y va directo al menú.
    /// </summary>
    public void SaltarTutorial()
    {
        Debug.Log("[TutorialManager] SaltarTutorial() ejecutado.");
        MarcarCompletadoYVolverAlMenu();
    }

    private void MarcarCompletadoYVolverAlMenu()
    {
        if (PlayFabAuthManager.Instance != null)
        {
            PlayFabAuthManager.Instance.MarcarTutorialCompletado();
        }
        else
        {
            Debug.LogError("[TutorialManager] No se encontró PlayFabAuthManager.Instance - no se pudo guardar el progreso del tutorial.");
        }

        SceneManager.LoadScene(nombreEscenaMenu);
    }
}