using UnityEngine;

/// <summary>
/// Escucha el resultado de la partida (GameManager.OnResultadoCambio) y
/// cambia la música según cuál panel se muestra. Vive del lado de
/// presentación local, como MonoBehaviour normal - GameManager no sabe ni
/// le importa que esto exista, igual que SuspicionMusicController con
/// SuspicionManager.
/// </summary>
public class GameResultMusic : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private GameManager gameManager;

    [Header("Música por resultado (deja vacío el que no quieras cambiar)")]
    [SerializeField] private SoundData musicaVictoria;
    [SerializeField] private SoundData musicaDerrotaPorBoss;
    [SerializeField] private SoundData musicaDerrotaPorSospecha;

    [SerializeField] private float fadeDuration = 1.5f;

    private void OnEnable()
    {
        gameManager.OnResultadoCambio += ManejarResultado;
    }

    private void OnDisable()
    {
        gameManager.OnResultadoCambio -= ManejarResultado;
    }

    private void ManejarResultado(ResultadoPartida resultado)
    {
        SoundData musica = resultado switch
        {
            ResultadoPartida.VictoriaJugadores => musicaVictoria,
            ResultadoPartida.DerrotaPorBoss => musicaDerrotaPorBoss,
            ResultadoPartida.DerrotaPorSospecha => musicaDerrotaPorSospecha,
            _ => null // EnCurso: no hay cambio de música asociado a este evento
        };

        if (musica == null)
        {
            return;
        }

        AudioManager.Instance.PlayMusic(musica, fadeDuration);
    }
}