using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization.Settings;
using TMPro;
using System.Collections;

public class LanguagePillToggle : MonoBehaviour
{
    [Header("Referencias UI")]
    [SerializeField] private RectTransform slider;
    [SerializeField] private TMP_Text textEspañol;
    [SerializeField] private TMP_Text textEnglish;
    [SerializeField] private Button toggleButton;

    [Header("Posiciones del slider")]
    [SerializeField] private float leftPosX = -50f;
    [SerializeField] private float rightPosX = 50f;
    [SerializeField] private float animSpeed = 10f;

    [Header("Colores de texto")]
    [SerializeField] private Color activeColor = Color.white;
    [SerializeField] private Color inactiveColor = new Color(0, 0, 0, 0.5f);

    private const string PREF_KEY = "selected_locale";

    private bool isEnglish = false;
    private Vector2 targetPos;

    private void Start()
    {
        toggleButton.onClick.AddListener(ToggleLanguage);
        StartCoroutine(InitializeLanguage());
    }

    private void Update()
    {
        slider.anchoredPosition = Vector2.Lerp(slider.anchoredPosition, targetPos, Time.deltaTime * animSpeed);
    }

    private IEnumerator InitializeLanguage()
    {
        // Espera a que el sistema de Localization esté listo
        yield return LocalizationSettings.InitializationOperation;

        string savedCode = PlayerPrefs.GetString(PREF_KEY, "en");
        var locale = LocalizationSettings.AvailableLocales.GetLocale(savedCode);

        if (locale != null)
            LocalizationSettings.SelectedLocale = locale;

        isEnglish = savedCode == "en";

        // Posiciona el slider sin animación al iniciar
        targetPos = new Vector2(isEnglish ? rightPosX : leftPosX, slider.anchoredPosition.y);
        slider.anchoredPosition = targetPos;

        UpdateTextStyle();
    }

    private void ToggleLanguage()
    {
        isEnglish = !isEnglish;

        string code = isEnglish ? "en" : "es";
        var locale = LocalizationSettings.AvailableLocales.GetLocale(code);
        LocalizationSettings.SelectedLocale = locale;

        PlayerPrefs.SetString(PREF_KEY, code);
        PlayerPrefs.Save();

        targetPos = new Vector2(isEnglish ? rightPosX : leftPosX, slider.anchoredPosition.y);
        UpdateTextStyle();
    }

    private void UpdateTextStyle()
    {
        textEspañol.color = isEnglish ? inactiveColor : activeColor;
        textEnglish.color = isEnglish ? activeColor : inactiveColor;
    }
}