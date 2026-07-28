using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [SerializeField] private AudioMixerGroup musicGroup;
    [SerializeField] private AudioMixerGroup sfxGroup;
    [SerializeField] private AudioSource musicSource;

    [SerializeField] private int poolSize = 10;
    private Queue<AudioSource> sfxPool;

    // Referencia a la coroutine de crossfade activa, para poder cancelarla
    // si se llama PlayMusic de nuevo antes de que termine la anterior.
    private Coroutine musicFadeRoutine;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        InitPool();
    }

    void InitPool()
    {
        sfxPool = new Queue<AudioSource>();
        for (int i = 0; i < poolSize; i++)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.outputAudioMixerGroup = sfxGroup;
            src.playOnAwake = false;
            sfxPool.Enqueue(src);
        }
    }

    public void PlaySFX(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (clip == null) return;
        var src = sfxPool.Dequeue();
        src.clip = clip;
        src.volume = volume;
        src.pitch = pitch;
        src.Play();
        sfxPool.Enqueue(src); // vuelve a la cola, se reutiliza cuando termine
    }

    /// <summary>
    /// Reproduce música con crossfade. Si ya hay una canción sonando, primero
    /// hace fade-out y después fade-in de la nueva. Si no hay nada sonando
    /// (ej. al entrar a la partida), arranca la nueva canción de inmediato
    /// y solo hace fade-in, sin esperar un fade-out innecesario.
    /// </summary>
    public void PlayMusic(AudioClip clip, bool loop = true, float fadeTime = 1f)
    {
        if (clip == null) return;

        // Si ya está sonando exactamente el mismo clip, no hacemos nada.
        if (musicSource.isPlaying && musicSource.clip == clip) return;

        // Cancelamos cualquier fade que esté corriendo para que no se pisen.
        if (musicFadeRoutine != null)
        {
            StopCoroutine(musicFadeRoutine);
            musicFadeRoutine = null;
        }

        bool thereIsMusicPlaying = musicSource.isPlaying && musicSource.clip != null;

        if (thereIsMusicPlaying)
        {
            musicFadeRoutine = StartCoroutine(CrossfadeMusic(clip, loop, fadeTime));
        }
        else
        {
            // Nada sonando: arrancamos ya mismo, solo con fade-in.
            musicFadeRoutine = StartCoroutine(FadeInMusic(clip, loop, fadeTime));
        }
    }

    private IEnumerator CrossfadeMusic(AudioClip newClip, bool loop, float fadeTime)
    {
        float startVol = musicSource.volume;

        // Fade-out del clip actual
        for (float t = 0; t < fadeTime; t += Time.deltaTime)
        {
            musicSource.volume = Mathf.Lerp(startVol, 0f, t / fadeTime);
            yield return null;
        }
        musicSource.volume = 0f;

        // Cambio de clip
        musicSource.clip = newClip;
        musicSource.loop = loop;
        musicSource.Play();

        // Fade-in del clip nuevo
        for (float t = 0; t < fadeTime; t += Time.deltaTime)
        {
            musicSource.volume = Mathf.Lerp(0f, startVol, t / fadeTime);
            yield return null;
        }
        musicSource.volume = startVol;
        musicFadeRoutine = null;
    }

    private IEnumerator FadeInMusic(AudioClip newClip, bool loop, float fadeTime)
    {
        float targetVol = musicSource.volume > 0f ? musicSource.volume : 1f;

        musicSource.clip = newClip;
        musicSource.loop = loop;
        musicSource.volume = 0f;
        musicSource.Play(); // arranca de inmediato, sin esperar

        for (float t = 0; t < fadeTime; t += Time.deltaTime)
        {
            musicSource.volume = Mathf.Lerp(0f, targetVol, t / fadeTime);
            yield return null;
        }
        musicSource.volume = targetVol;
        musicFadeRoutine = null;
    }

    /// <summary>
    /// Corta la música con fade-out (útil para pausar/salir de la partida).
    /// </summary>
    public void StopMusic(float fadeTime = 1f)
    {
        if (musicFadeRoutine != null) StopCoroutine(musicFadeRoutine);
        musicFadeRoutine = StartCoroutine(FadeOutAndStop(fadeTime));
    }

    private IEnumerator FadeOutAndStop(float fadeTime)
    {
        float startVol = musicSource.volume;
        for (float t = 0; t < fadeTime; t += Time.deltaTime)
        {
            musicSource.volume = Mathf.Lerp(startVol, 0f, t / fadeTime);
            yield return null;
        }
        musicSource.Stop();
        musicSource.volume = startVol; // restauramos para la próxima vez que suene
        musicFadeRoutine = null;
    }
}