using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Mixer Groups")]
    [SerializeField] private AudioMixerGroup musicGroup;
    [SerializeField] private AudioMixerGroup sfxGroup;

    [Header("Música (crossfade con 2 fuentes, se crean solas)")]
    [Tooltip("Duración por defecto del crossfade al cambiar de música. 0 = corte instantáneo.")]
    [SerializeField] private float defaultFadeDuration = 0.4f;

    private AudioSource musicSourceA;
    private AudioSource musicSourceB;
    private AudioSource activeMusicSource;
    private Coroutine crossfadeRoutine;

    [Header("Pool de SFX")]
    [SerializeField] private int poolSize = 10;
    private Queue<AudioSource> sfxPool;

    [System.Serializable]
    public struct SceneMusicEntry
    {
        [Tooltip("Debe coincidir EXACTAMENTE con el nombre de la escena (el que aparece en Build Settings).")]
        public string sceneName;
        public SoundData music;
    }

    [Header("Música por escena")]
    [Tooltip("Al cargar cada escena, se busca aquí el SoundData correspondiente y se reproduce de inmediato.")]
    [SerializeField] private List<SceneMusicEntry> sceneMusicMap;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Ya existe un AudioManager (viene de DontDestroyOnLoad de la escena anterior).
            // Este duplicado se destruye antes de tocar nada más.
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        InitPool();
        InitMusicSources();

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        // La escena inicial ya terminó de cargar antes de que Awake() se suscribiera
        // al evento sceneLoaded, así que la disparamos manualmente una vez al arrancar.
        HandleSceneMusic(SceneManager.GetActiveScene().name);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        HandleSceneMusic(scene.name);
    }

    private void HandleSceneMusic(string sceneName)
    {
        for (int i = 0; i < sceneMusicMap.Count; i++)
        {
            if (sceneMusicMap[i].sceneName == sceneName)
            {
                PlayMusic(sceneMusicMap[i].music); // usa defaultFadeDuration
                return;
            }
        }

        Debug.LogWarning($"AudioManager: no hay música asignada para la escena '{sceneName}'.");
    }

    // ---------------- SFX ----------------

    private void InitPool()
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
        sfxPool.Enqueue(src);
    }

    // ---------------- Música (crossfade con 2 fuentes) ----------------

    private void InitMusicSources()
    {
        musicSourceA = gameObject.AddComponent<AudioSource>();
        musicSourceB = gameObject.AddComponent<AudioSource>();

        foreach (var src in new[] { musicSourceA, musicSourceB })
        {
            src.outputAudioMixerGroup = musicGroup;
            src.playOnAwake = false;
            src.loop = true;
            src.volume = 0f;
        }

        activeMusicSource = musicSourceA;
    }

    /// <summary>
    /// Reproduce música con crossfade: la fuente entrante empieza en volumen 0 y sube
    /// mientras la saliente baja, ambas al mismo tiempo (no hay silencio entre medio).
    /// fadeDuration = 0 -> corte instantáneo (comportamiento anterior).
    /// </summary>
    public void PlayMusic(SoundData data, float fadeDuration = -1f)
    {
        if (data == null)
        {
            Debug.LogWarning("AudioManager.PlayMusic: SoundData nulo.");
            return;
        }

        AudioClip clip = data.GetClip();
        if (clip == null) return;

        // Ya está sonando exactamente este clip: no reiniciamos.
        if (activeMusicSource.isPlaying && activeMusicSource.clip == clip) return;

        float duration = fadeDuration >= 0f ? fadeDuration : defaultFadeDuration;

        AudioSource outgoing = activeMusicSource;
        AudioSource incoming = (activeMusicSource == musicSourceA) ? musicSourceB : musicSourceA;

        incoming.clip = clip;
        incoming.volume = duration <= 0f ? data.volume : 0f;
        incoming.Play();

        if (crossfadeRoutine != null) StopCoroutine(crossfadeRoutine);

        if (duration <= 0f)
        {
            // Corte instantáneo: la saliente se detiene ya mismo.
            outgoing.Stop();
        }
        else
        {
            crossfadeRoutine = StartCoroutine(CrossfadeRoutine(outgoing, incoming, data.volume, duration));
        }

        activeMusicSource = incoming;
    }

    private IEnumerator CrossfadeRoutine(AudioSource outgoing, AudioSource incoming, float targetVolume, float duration)
    {
        float outgoingStartVolume = outgoing.volume;
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float p = t / duration;
            outgoing.volume = Mathf.Lerp(outgoingStartVolume, 0f, p);
            incoming.volume = Mathf.Lerp(0f, targetVolume, p);
            yield return null;
        }

        outgoing.Stop();
        outgoing.volume = 0f;
        incoming.volume = targetVolume;
        crossfadeRoutine = null;
    }

    public void StopMusic(float fadeDuration = -1f)
    {
        float duration = fadeDuration >= 0f ? fadeDuration : defaultFadeDuration;

        if (crossfadeRoutine != null) StopCoroutine(crossfadeRoutine);

        if (duration <= 0f)
        {
            activeMusicSource.Stop();
        }
        else
        {
            crossfadeRoutine = StartCoroutine(FadeOutAndStop(activeMusicSource, duration));
        }
    }

    private IEnumerator FadeOutAndStop(AudioSource source, float duration)
    {
        float startVolume = source.volume;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            source.volume = Mathf.Lerp(startVolume, 0f, t / duration);
            yield return null;
        }
        source.Stop();
        source.volume = 0f;
        crossfadeRoutine = null;
    }
}