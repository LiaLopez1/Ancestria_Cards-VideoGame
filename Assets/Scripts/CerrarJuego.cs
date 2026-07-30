using UnityEngine;

public class CerrarJuego : MonoBehaviour
{
    public void SalirDelJuego()
    {
        Debug.Log("Cerrando el juego...");
        Application.Quit();

#if UNITY_EDITOR
        // Esto permite "salir" también cuando estás probando en el Editor de Unity
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}