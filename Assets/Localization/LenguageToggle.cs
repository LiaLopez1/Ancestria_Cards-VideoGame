using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;
using TMPro;

public class LanguageToggle : MonoBehaviour
{
    [SerializeField] private Button toggleButton;
    [SerializeField] private TMP_Text buttonLabel; // opcional, para mostrar "EN" / "ES"

    private void Start()
    {
        toggleButton.onClick.AddListener(ToggleLanguage);
        UpdateLabel();
    }

    private void ToggleLanguage()
    {
        var current = LocalizationSettings.SelectedLocale;

        // Si el idioma actual es inglés, cambia a español y viceversa
        string newCode = current.Identifier.Code == "en" ? "es" : "en";

        var newLocale = LocalizationSettings.AvailableLocales.GetLocale(newCode);
        LocalizationSettings.SelectedLocale = newLocale;

        // Guardar preferencia
        PlayerPrefs.SetString("selected_locale", newCode);
        PlayerPrefs.Save();

        UpdateLabel();
    }

    private void UpdateLabel()
    {
        string code = LocalizationSettings.SelectedLocale.Identifier.Code;

        if (buttonLabel != null)
            buttonLabel.text = code == "en" ? "English" : "Español";
    }
}