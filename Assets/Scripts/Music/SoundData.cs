using UnityEngine;

[CreateAssetMenu(fileName = "New Sound", menuName = "Music/Sound Data")]
public class SoundData : ScriptableObject
{
    [Header("Clips de audio (elige uno al azar si hay varios)")]
    public AudioClip[] clips;

    [Header("Configuración")]
    [Range(0f, 1f)] public float volume = 1f;
    [Range(-0.3f, 0.3f)] public float pitchVariation = 0.05f;

    public void Play()
    {
        // Evita errores si olvidaste arrastrar un clip
        if (clips == null || clips.Length == 0)
        {
            Debug.LogWarning($"SoundData '{name}' no tiene clips asignados.");
            return;
        }

        // Elige un clip al azar del array (variación si hay varios)
        var clip = clips[Random.Range(0, clips.Length)];

        // Variación de pitch para que no suene siempre igual
        float pitch = 1f + Random.Range(-pitchVariation, pitchVariation);

        // Le pide al AudioManager que lo reproduzca
        AudioManager.Instance.PlaySFX(clip, volume, pitch);
    }
}