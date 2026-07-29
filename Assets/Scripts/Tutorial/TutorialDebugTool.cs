using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// SOLO PARA PRUEBAS. Poné este script en cualquier objeto de la escena de
/// menú (por ejemplo, junto a StartupFlowUI) mientras estés probando el
/// flujo del tutorial - apretando la tecla configurada, resetea la bandera
/// de "TutorialCompletado" en PlayFab y recarga esta misma escena, así
/// podés repetir "jugador nuevo -> tutorial -> volver" cuantas veces
/// necesites sin ir al dashboard de PlayFab a mano.
///
/// Se compila SOLO en el Editor y en builds de desarrollo (DEVELOPMENT_BUILD) -
/// nunca en una build final, para que un jugador real no pueda resetear su
/// propio progreso por accidente (o a propósito).
/// </summary>
public class TutorialDebugTools : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Tooltip("Tecla para resetear el tutorial y recargar la escena.")]
    [SerializeField] private Key teclaResetear = Key.F9;

    private Key teclaValidada;

    private void Awake()
    {
        // Si el campo tenia un valor viejo de KeyCode (de antes de este cambio
        // de tipo), Unity lo reinterpreta como un numero crudo que puede no
        // corresponder a ninguna tecla real del nuevo enum - eso rompe
        // Keyboard.current[...] con un ArgumentOutOfRangeException. Validamos
        // antes de usarlo, y si es invalido, caemos a F9 por defecto.
        if (System.Enum.IsDefined(typeof(Key), teclaResetear))
        {
            teclaValidada = teclaResetear;
        }
        else
        {
            teclaValidada = Key.F9;
            Debug.LogWarning($"[TutorialDebugTools] El valor guardado en 'Tecla Resetear' no es válido (probablemente quedó de cuando el campo era KeyCode) - usando F9 por defecto. Volvé a asignarlo manualmente en el Inspector para que quede guardado bien.");
        }

        Debug.Log($"[TutorialDebugTools] Activo - apretá {teclaValidada} para resetear el tutorial (o conectá un botón de UI a ResetearYRecargar()).");
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current[teclaValidada].wasPressedThisFrame)
        {
            ResetearYRecargar();
        }
    }

    /// <summary>Público a propósito - se puede conectar directo al OnClick() de un botón de UI, además de la tecla.</summary>
    public void ResetearYRecargar()
    {
        if (PlayFabAuthManager.Instance == null)
        {
            Debug.LogWarning("[TutorialDebugTools] No hay PlayFabAuthManager.Instance todavía (¿ya hiciste login al menos una vez?).");
            return;
        }

        PlayFabAuthManager.Instance.ResetearTutorialCompletado();

        Debug.Log("[TutorialDebugTools] Tutorial reseteado - recargando la escena actual...");

        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
        );
    }
#endif
}