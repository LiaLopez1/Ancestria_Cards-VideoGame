using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Muestra el marcador de rondas como "bolitas" (estilo penales de futbol):
/// 3 Image ya puestas en la escena (por ejemplo, dentro de un Horizontal
/// Layout Group), a las que este script les cambia el COLOR segun el
/// resultado de esa ronda - gris mientras no se jugo, verde si la gano un
/// jugador, rojo si la gano el boss.
///
/// No usa prefabs ni Instantiate/Destroy - las 3 bolitas ya existen en la
/// escena desde el principio, y solo se les cambia el color.
///
/// Se suscribe a GameManager.OnResultadoRondaRegistrado - no necesita saber
/// nada de la logica del juego en si, solo "que ronda" y "quien la gano".
/// </summary>
public class RoundIndicatorUI : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private GameManager gameManager;

    [Header("Bolitas, en orden (una por ronda) - ya puestas en la escena")]
    [SerializeField] private Image[] bolitas;

    [Header("Colores segun resultado")]
    [SerializeField] private Color colorPendiente = Color.gray;
    [SerializeField] private Color colorGanoJugador = Color.green;
    [SerializeField] private Color colorGanoBoss = Color.red;

    private void Start()
    {
        if (gameManager == null)
        {
            Debug.LogError("[RoundIndicatorUI] No se asignó Game Manager en el Inspector.");
            return;
        }

        gameManager.OnResultadoRondaRegistrado += ActualizarBolita;

        // Por si ya habia resultados sincronizados antes de suscribirnos
        // (por ejemplo, un cliente que se conecta a mitad de partida) -
        // reconstruimos el marcador visual completo de una.
        for (int i = 0; i < bolitas.Length; i++)
        {
            ActualizarBolita(i, gameManager.ObtenerResultadoDeRonda(i));
        }
    }

    private void OnDestroy()
    {
        if (gameManager != null)
        {
            gameManager.OnResultadoRondaRegistrado -= ActualizarBolita;
        }
    }

    /// <summary>resultado: 0=pendiente, 1=gano un jugador (verde), 2=gano el boss (rojo).</summary>
    private void ActualizarBolita(int indiceRonda, int resultado)
    {
        if (indiceRonda < 0 || indiceRonda >= bolitas.Length || bolitas[indiceRonda] == null)
        {
            return;
        }

        bolitas[indiceRonda].color = resultado switch
        {
            1 => colorGanoJugador,
            2 => colorGanoBoss,
            _ => colorPendiente
        };
    }
}