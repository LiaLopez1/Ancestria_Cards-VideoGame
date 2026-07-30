using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Muestra visualmente el nivel de sospecha compartido (SuspicionManager) -
/// un Slider (0 a sospechaMaxima) + texto opcional ("3/10"). También puede
/// deshabilitar el botón de "Aceptar intercambio" mientras no haya ningún
/// intercambio en curso, para que no se pueda apretar algo que no empezó.
///
/// El relleno del slider cambia de color gradualmente segun aumenta el
/// nivel de sospecha (verde -> amarillo -> rojo), y el valor se anima
/// suavemente en vez de saltar de golpe.
///
/// Puramente de UI - no toma ninguna decisión, solo refleja lo que
/// SuspicionManager (server-autoritativo) ya decidió.
/// </summary>
public class SuspicionBarUI : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private SuspicionManager suspicionManager;
    [SerializeField] private Slider sliderSospecha;

    //[SerializeField] private TMP_Text textoSospecha;

    [Header("Colores del relleno (bajo -> alto)")]
    [SerializeField] private Color colorBajo = Color.green;
    [SerializeField] private Color colorAlto = Color.red;

    [Header("Animacion del slider")]
    [SerializeField] private float velocidadAnimacion = 2f; // unidades por segundo

    [Header("Botón de aceptar intercambio (opcional)")]
    [SerializeField] private Button botonAceptarIntercambio;

    private Coroutine animacionCoroutine;
    private Image fillImage;

    private void Start()
    {
        if (suspicionManager == null)
        {
            Debug.LogError("[SuspicionBarUI] No se asignó SuspicionManager.");
            return;
        }

        if (sliderSospecha != null)
        {
            sliderSospecha.minValue = 0f;
            sliderSospecha.maxValue = suspicionManager.SospechaMaxima;

            if (sliderSospecha.fillRect != null)
            {
                fillImage = sliderSospecha.fillRect.GetComponent<Image>();
            }
        }

        suspicionManager.OnSospechaCambio += ActualizarBarra;
        suspicionManager.OnIntercambioCambio += ActualizarBotonAceptar;

        // Estado inicial - por si ya había un valor sincronizado antes de
        // que este script llegara a suscribirse (por ejemplo, si alguien se
        // conecta a mitad de partida).
        ActualizarBarra(suspicionManager.NivelSospecha, instantaneo: true);
        ActualizarBotonAceptar(suspicionManager.IntercambioEnCurso);
    }

    private void OnDestroy()
    {
        if (suspicionManager == null)
        {
            return;
        }

        suspicionManager.OnSospechaCambio -= ActualizarBarra;
        suspicionManager.OnIntercambioCambio -= ActualizarBotonAceptar;
    }

    private void ActualizarBarra(float nuevoValor)
    {
        ActualizarBarra(nuevoValor, instantaneo: false);
    }

    private void ActualizarBarra(float nuevoValor, bool instantaneo)
    {
        if (sliderSospecha == null) return;

        if (animacionCoroutine != null)
        {
            StopCoroutine(animacionCoroutine);
        }

        if (instantaneo)
        {
            sliderSospecha.value = nuevoValor;
            ActualizarColorRelleno(nuevoValor);
        }
        else
        {
            animacionCoroutine = StartCoroutine(AnimarSlider(nuevoValor));
        }

        /*if (textoSospecha != null)
        {
            textoSospecha.text = $"{Mathf.RoundToInt(nuevoValor)}/{Mathf.RoundToInt(suspicionManager.SospechaMaxima)}";
        }*/
    }

    private IEnumerator AnimarSlider(float objetivo)
    {
        while (!Mathf.Approximately(sliderSospecha.value, objetivo))
        {
            sliderSospecha.value = Mathf.MoveTowards(sliderSospecha.value, objetivo, velocidadAnimacion * Time.deltaTime);
            ActualizarColorRelleno(sliderSospecha.value);
            yield return null;
        }

        sliderSospecha.value = objetivo;
        ActualizarColorRelleno(objetivo);
    }

    private void ActualizarColorRelleno(float valorActual)
    {
        if (fillImage == null || suspicionManager == null) return;

        float progreso = valorActual / suspicionManager.SospechaMaxima;
        fillImage.color = Color.Lerp(colorBajo, colorAlto, progreso);
    }

    private void ActualizarBotonAceptar(bool intercambioEnCurso)
    {
        if (botonAceptarIntercambio != null)
        {
            botonAceptarIntercambio.interactable = intercambioEnCurso;
        }
    }
}