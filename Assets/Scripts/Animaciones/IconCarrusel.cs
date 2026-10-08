using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

public class IconCarousel : MonoBehaviour
{
    [Header("Íconos (en orden de ciclo)")]
    public Image[] icons;

    [Header("Posiciones X (relativas al centro del contenedor)")]
    public float leftX = -180f;
    public float centerX = 0f;
    public float rightX = 180f;
    public float hiddenX = 400f;

    [Header("Escalas")]
    public float smallScale = 0.6f;
    public float bigScale = 1.0f;

    [Header("Animación")]
    public float transitionSpeed = 8f;

    [Header("Auto-avance (opcional)")]
    public bool autoAdvance = true;
    public float autoAdvanceInterval = 1.5f;

    [Header("Texto localizado (máquina de escribir)")]
    [SerializeField] private LocalizedString fullText;
    public TMP_Text typewriterText;
    public float typingSpeed = 0.05f;
    public float pauseBeforeRepeat = 1.2f;
    public float pauseBeforeErase = 0f;

    private int[] slotIndex;
    private float timer;

    private string textoLocalizado = "";
    private Coroutine typewriterCoroutine;

    void OnEnable()
    {
        // Escuchar cambios de idioma
        fullText.StringChanged += ActualizarTextoLocalizado;

        // Pedir el texto correspondiente al idioma actual
        fullText.RefreshString();
    }

    void OnDisable()
    {
        // Dejar de escuchar cambios de idioma
        fullText.StringChanged -= ActualizarTextoLocalizado;
    }

    void Start()
    {
        slotIndex = new int[icons.Length];

        for (int i = 0; i < icons.Length; i++)
        {
            slotIndex[i] = i < 3 ? (3 - i) : 0;
            SnapToSlot(i, slotIndex[i]);
        }

        if (typewriterText != null)
        {
            typewriterCoroutine = StartCoroutine(TypewriterLoop());
        }
    }

    void Update()
    {
        if (autoAdvance)
        {
            timer += Time.deltaTime;

            if (timer >= autoAdvanceInterval)
            {
                timer = 0f;
                NextIcon();
            }
        }

        for (int i = 0; i < icons.Length; i++)
        {
            RectTransform rt = icons[i].rectTransform;

            (float x, float scale, float alpha) target =
                SlotTarget(slotIndex[i]);

            Vector2 pos = rt.anchoredPosition;

            pos.x = Mathf.Lerp(
                pos.x,
                target.x,
                Time.deltaTime * transitionSpeed
            );

            rt.anchoredPosition = pos;

            rt.localScale = Vector3.Lerp(
                rt.localScale,
                Vector3.one * target.scale,
                Time.deltaTime * transitionSpeed
            );

            Color c = icons[i].color;

            c.a = Mathf.Lerp(
                c.a,
                target.alpha,
                Time.deltaTime * transitionSpeed
            );

            icons[i].color = c;
        }
    }

    public void NextIcon()
    {
        for (int i = 0; i < icons.Length; i++)
        {
            int oldSlot = slotIndex[i];
            int newSlot = (oldSlot + 1) % 4;

            if (oldSlot == 3 && newSlot == 0)
            {
                SnapToSlot(i, 0);
            }

            slotIndex[i] = newSlot;
        }
    }

    private (float x, float scale, float alpha) SlotTarget(int slot)
    {
        switch (slot)
        {
            case 3:
                return (leftX, smallScale, 1f);

            case 2:
                return (centerX, bigScale, 1f);

            case 1:
                return (rightX, smallScale, 1f);

            default:
                return (hiddenX, smallScale, 0f);
        }
    }

    private void ActualizarTextoLocalizado(string nuevoTexto)
    {
        textoLocalizado = nuevoTexto;

        // Si el idioma cambia, reiniciamos la animación
        if (typewriterCoroutine != null)
        {
            StopCoroutine(typewriterCoroutine);
        }

        if (typewriterText != null)
        {
            typewriterCoroutine = StartCoroutine(TypewriterLoop());
        }
    }

    private System.Collections.IEnumerator TypewriterLoop()
    {
        while (true)
        {
            if (string.IsNullOrEmpty(textoLocalizado))
            {
                yield return null;
                continue;
            }

            // Limpiar el texto
            typewriterText.text = "";

            // Máquina de escribir
            for (int i = 0; i < textoLocalizado.Length; i++)
            {
                typewriterText.text += textoLocalizado[i];

                yield return new WaitForSeconds(typingSpeed);
            }

            // Esperar después de terminar
            yield return new WaitForSeconds(pauseBeforeRepeat);

            // Borrar texto
            if (pauseBeforeErase > 0f)
            {
                for (int i = textoLocalizado.Length; i > 0; i--)
                {
                    typewriterText.text =
                        textoLocalizado.Substring(0, i - 1);

                    yield return new WaitForSeconds(
                        typingSpeed * 0.5f
                    );
                }

                yield return new WaitForSeconds(pauseBeforeErase);
            }
        }
    }

    private void SnapToSlot(int iconIndex, int slot)
    {
        var t = SlotTarget(slot);

        RectTransform rt = icons[iconIndex].rectTransform;

        rt.anchoredPosition = new Vector2(
            t.x,
            rt.anchoredPosition.y
        );

        rt.localScale = Vector3.one * t.scale;

        Color c = icons[iconIndex].color;
        c.a = t.alpha;

        icons[iconIndex].color = c;
    }
}