using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class UIClickSound : MonoBehaviour, IPointerEnterHandler
{
    [Header("Sonidos")]
    [SerializeField] private SoundData clickSound;
    [SerializeField] private SoundData hoverSound;

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
    }

    private void OnEnable()
    {
        button.onClick.AddListener(PlayClickSound);
    }

    private void OnDisable()
    {
        button.onClick.RemoveListener(PlayClickSound);
    }

    private void PlayClickSound()
    {
        clickSound?.Play();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // Evita reproducir el sonido si el botón está desactivado.
        if (button.interactable)
        {
            hoverSound?.Play();
        }
    }
}