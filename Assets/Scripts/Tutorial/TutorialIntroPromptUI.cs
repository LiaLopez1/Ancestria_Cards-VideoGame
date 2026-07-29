using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Primera pantalla que aparece al entrar a la escena del tutorial:
/// "¿Querés ver un tutorial?" con botones Sí/No.
///
///   - Sí: oculta este panel y arranca la secuencia normal
///     (TutorialSequencer.IniciarSecuencia()).
///   - No: se da por completado el tutorial de una - el botón "No" se
///     conecta DIRECTO en el Inspector a TutorialManager.CompletarTutorial()
///     (la misma función que ya usa el resto del tutorial para salir), y
///     tambien a OcultarPanel() de este script para que no quede este
///     panel encima del de felicitaciones.
/// </summary>
public class TutorialIntroPromptUI : MonoBehaviour
{
    [Header("Panel (arranca visible - es lo primero que se ve en la escena)")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Button botonSi;

    [Header("Referencias")]
    [SerializeField] private TutorialSequencer tutorialSequencer;

    private void Awake()
    {
        if (botonSi != null)
        {
            botonSi.onClick.AddListener(ResponderSi);
        }

        // El boton "No" NO se conecta aca por codigo - se cablea directo en
        // el Inspector a OcultarPanel() + TutorialManager.CompletarTutorial(),
        // reutilizando esa misma salida sin logica nueva.
    }

    private void ResponderSi()
    {
        OcultarPanel();

        if (tutorialSequencer != null)
        {
            tutorialSequencer.IniciarSecuencia();
        }
        else
        {
            Debug.LogError("[TutorialIntroPromptUI] No se asignó TutorialSequencer en el Inspector.");
        }
    }

    /// <summary>
    /// Público a propósito - se conecta también desde el OnClick() del
    /// botón "No" (junto con TutorialManager.CompletarTutorial()), para que
    /// este panel no quede visible encima del de felicitaciones.
    /// </summary>
    public void OcultarPanel()
    {
        if (panel != null)
        {
            panel.SetActive(false);
        }
    }
}