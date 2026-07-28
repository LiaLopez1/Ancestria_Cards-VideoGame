using UnityEngine;
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

    public void PlayMusic(AudioClip clip, bool loop = true, float fadeTime = 1f)
    {
        StartCoroutine(CrossfadeMusic(clip, loop, fadeTime));
    }

    private IEnumerator CrossfadeMusic(AudioClip newClip, bool loop, float fadeTime)
    {
        float startVol = musicSource.volume;
        for (float t = 0; t < fadeTime; t += Time.deltaTime)
        {
            musicSource.volume = Mathf.Lerp(startVol, 0, t / fadeTime);
            yield return null;
        }
        musicSource.clip = newClip;
        musicSource.loop = loop;
        musicSource.Play();
        for (float t = 0; t < fadeTime; t += Time.deltaTime)
        {
            musicSource.volume = Mathf.Lerp(0, startVol, t / fadeTime);
            yield return null;
        }
    }
}
