using UnityEngine;

/// <summary>
/// Escucha los cambios de sospecha y cambia la música de fondo cuando se
/// cruza el 50% (y la revierte si vuelve a bajar, ej. al reiniciar la
/// partida). Vive del lado de presentación, como MonoBehaviour local -
/// SuspicionManager no sabe ni le importa que esto exista.
///
/// El cambio de canción usa AudioManager.PlayMusic(), que ya hace crossfade
/// (sube la nueva mientras baja la anterior) - no hay que agregar nada
/// nuevo para "que no se corte", ya viene incluido.
/// </summary>
public class SuspicionMusicController : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private SuspicionManager suspicionManager;

    [Header("Música")]
    [SerializeField] private SoundData musicaNormal;
    [SerializeField] private SoundData musicaAlerta; // a partir del 50% de sospecha

    // Evita llamar PlayMusic() de nuevo en cada tick de sospecha mientras
    // ya estamos en la fase correcta (el evento se dispara seguido, no
    // solo en el momento exacto de cruzar el 50%).
    private bool enFaseAlerta;

    private void OnEnable()
    {
        suspicionManager.OnSospechaCambio += ManejarCambioDeSospecha;
    }

    private void OnDisable()
    {
        suspicionManager.OnSospechaCambio -= ManejarCambioDeSospecha;
    }

    private void ManejarCambioDeSospecha(float nuevoValor)
    {
        bool debeEstarEnAlerta = suspicionManager.AlcanzoMitadDeSospecha;

        // Ya estamos en el estado de música correcto - no repetir la llamada.
        if (debeEstarEnAlerta == enFaseAlerta) return;

        enFaseAlerta = debeEstarEnAlerta;

        AudioManager.Instance.PlayMusic(enFaseAlerta ? musicaAlerta : musicaNormal);
    }
}