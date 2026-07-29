using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class AudioOptionsUI : MonoBehaviour
{
    [Header("Audio Mixer")]
    [SerializeField] private AudioMixer mainMixer;

    [Header("Sliders")]
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;

    private const string MusicParameter = "MusicVolume";
    private const string SFXParameter = "SFXVolume";

    private const string MusicPreference = "MusicVolumeValue";
    private const string SFXPreference = "SFXVolumeValue";

    private void Start()
    {
        // Recupera los valores guardados.
        float savedMusicVolume =
            PlayerPrefs.GetFloat(MusicPreference, 1f);

        float savedSFXVolume =
            PlayerPrefs.GetFloat(SFXPreference, 1f);

        // Actualiza visualmente los sliders sin ejecutar sus eventos.
        musicSlider.SetValueWithoutNotify(savedMusicVolume);
        sfxSlider.SetValueWithoutNotify(savedSFXVolume);

        // Aplica los valores al mixer.
        SetMusicVolume(savedMusicVolume);
        SetSFXVolume(savedSFXVolume);

        // Escucha los cambios realizados por el jugador.
        musicSlider.onValueChanged.AddListener(SetMusicVolume);
        sfxSlider.onValueChanged.AddListener(SetSFXVolume);
    }

    private void OnDestroy()
    {
        musicSlider.onValueChanged.RemoveListener(SetMusicVolume);
        sfxSlider.onValueChanged.RemoveListener(SetSFXVolume);
    }

    public void SetMusicVolume(float value)
    {
        SetMixerVolume(MusicParameter, value);
        PlayerPrefs.SetFloat(MusicPreference, value);
    }

    public void SetSFXVolume(float value)
    {
        SetMixerVolume(SFXParameter, value);
        PlayerPrefs.SetFloat(SFXPreference, value);
    }

    private void SetMixerVolume(string parameter, float value)
    {
        // Evita calcular Log10(0).
        float volumeInDecibels =
            value <= 0.0001f
                ? -80f
                : Mathf.Log10(value) * 20f;

        mainMixer.SetFloat(parameter, volumeInDecibels);
    }

    private void OnDisable()
    {
        PlayerPrefs.Save();
    }
}