using UnityEngine;

public class CerrarJuego : MonoBehaviour
{
    [Header("Volver al menu (con todo lo que conlleva: sala, servidor, etc.)")]
    [SerializeField] private GameRestartManager gameRestartManager;

    public void SalirDelJuego()
    {
        Debug.Log("Cerrando el juego...");
        Application.Quit();

#if UNITY_EDITOR
        // Esto permite "salir" también cuando estás probando en el Editor de Unity
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    /// <summary>
    /// Conectar al botón "Volver al menú" del panel de pausa (en la escena
    /// de juego). A diferencia de SalirDelJuego() (que cierra la app),
    /// esto sale de la sala de PlayFab, apaga Netcode, y carga el menú
    /// principal - reutiliza GameRestartManager.VolverAlMenu(), el mismo
    /// método que ya usan los paneles de victoria/derrota, para no
    /// duplicar esa lógica en dos lugares distintos.
    /// </summary>
    public void VolverAlMenuPrincipal()
    {
        if (gameRestartManager == null)
        {
            Debug.LogError("[CerrarJuego] No se asignó 'Game Restart Manager' en el Inspector - no se puede volver al menú.");
            return;
        }

        gameRestartManager.VolverAlMenu();
    }
}