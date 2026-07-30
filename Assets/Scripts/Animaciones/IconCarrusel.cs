using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class IconCarousel : MonoBehaviour
{
    [Header("Íconos (en orden de ciclo)")]
    public Image[] icons;

    [Header("Posiciones X (relativas al centro del contenedor)")]
    public float leftX = -180f;
    public float centerX = 0f;
    public float rightX = 180f;
    public float hiddenX = 400f; // fuera de pantalla, a la derecha

    [Header("Escalas")]
    public float smallScale = 0.6f;
    public float bigScale = 1.0f;

    [Header("Animación")]
    public float transitionSpeed = 8f;

    [Header("Auto-avance (opcional)")]
    public bool autoAdvance = true;
    public float autoAdvanceInterval = 1.5f;

    [Header("Texto animado (máquina de escribir)")]
    public TMP_Text typewriterText;
    [TextArea] public string fullText = "Elige tu elemento...";
    public float typingSpeed = 0.05f;      
    public float pauseBeforeRepeat = 1.2f; 
    public float pauseBeforeErase = 0f;   

    private int[] slotIndex;
    private float timer;

    void Start()
    {
        slotIndex = new int[icons.Length];
        // Estado inicial: los primeros 3 en izquierda/centro/derecha,
        // el resto (si hay más de 4) empiezan ocultos.
        for (int i = 0; i < icons.Length; i++)
        {
            slotIndex[i] = i < 3 ? (3 - i) : 0; // 3=izq,2=centro,1=der,0=oculto
            SnapToSlot(i, slotIndex[i]);
        }

        if (typewriterText != null)
            StartCoroutine(TypewriterLoop());
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
            (float x, float scale, float alpha) target = SlotTarget(slotIndex[i]);

            Vector2 pos = rt.anchoredPosition;
            pos.x = Mathf.Lerp(pos.x, target.x, Time.deltaTime * transitionSpeed);
            rt.anchoredPosition = pos;

            rt.localScale = Vector3.Lerp(rt.localScale, Vector3.one * target.scale, Time.deltaTime * transitionSpeed);

            Color c = icons[i].color;
            c.a = Mathf.Lerp(c.a, target.alpha, Time.deltaTime * transitionSpeed);
            icons[i].color = c;
        }
    }

    /// <summary>Avanza el carrusel una posición (el de la izquierda sale, entra uno nuevo por la derecha).</summary>
    public void NextIcon()
    {
        for (int i = 0; i < icons.Length; i++)
        {
            int oldSlot = slotIndex[i];
            int newSlot = (oldSlot + 1) % 4; // izquierda(3) -> oculto(0), derecha(1)->centro(2), etc.

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
            case 3: return (leftX, smallScale, 1f);   // izquierda, visible chico
            case 2: return (centerX, bigScale, 1f);   // centro, visible grande
            case 1: return (rightX, smallScale, 1f);  // derecha, visible chico
            default: return (hiddenX, smallScale, 0f); // oculto, invisible
        }
    }

    private System.Collections.IEnumerator TypewriterLoop()
    {
        while (true)
        {
            // Escribiendo letra por letra
            typewriterText.text = "";
            for (int i = 0; i < fullText.Length; i++)
            {
                typewriterText.text += fullText[i];
                yield return new WaitForSeconds(typingSpeed);
            }

            // Pausa con el texto completo visible
            yield return new WaitForSeconds(pauseBeforeRepeat);

            // Borrado opcional (efecto "backspace") antes de repetir
            if (pauseBeforeErase > 0f)
            {
                for (int i = fullText.Length; i > 0; i--)
                {
                    typewriterText.text = fullText.Substring(0, i - 1);
                    yield return new WaitForSeconds(typingSpeed * 0.5f);
                }
                yield return new WaitForSeconds(pauseBeforeErase);
            }
        }
    }

    private void SnapToSlot(int iconIndex, int slot)
    {
        var t = SlotTarget(slot);
        RectTransform rt = icons[iconIndex].rectTransform;
        rt.anchoredPosition = new Vector2(t.x, rt.anchoredPosition.y);
        rt.localScale = Vector3.one * t.scale;
        Color c = icons[iconIndex].color;
        c.a = t.alpha;
        icons[iconIndex].color = c;
    }
}