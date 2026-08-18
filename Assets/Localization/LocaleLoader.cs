using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Localization.Settings;

public static class LocaleLoader
{
    private const string PREF_KEY = "selected_locale";

    // Esto corre automáticamente ANTES de que cargue cualquier escena
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        // Necesitamos una corrutina, pero los métodos estáticos no pueden iniciarlas directamente,
        // así que usamos un GameObject temporal invisible solo para esto
        var runner = new GameObject("LocaleLoaderRunner");
        UnityEngine.Object.DontDestroyOnLoad(runner);
        runner.AddComponent<LocaleLoaderRunner>().StartLoading();
    }
}

public class LocaleLoaderRunner : MonoBehaviour
{
    private const string PREF_KEY = "selected_locale";

    public void StartLoading()
    {
        StartCoroutine(LoadLocale());
    }

    private IEnumerator LoadLocale()
    {
        yield return LocalizationSettings.InitializationOperation;

        string savedCode = PlayerPrefs.GetString(PREF_KEY, "es");
        var locale = LocalizationSettings.AvailableLocales.GetLocale(savedCode);

        if (locale != null)
            LocalizationSettings.SelectedLocale = locale;

        // Ya cumplió su propósito, se destruye
        Destroy(gameObject);
    }
}