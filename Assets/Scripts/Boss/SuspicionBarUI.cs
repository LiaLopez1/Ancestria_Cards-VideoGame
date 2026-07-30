using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Muestra visualmente el nivel de sospecha compartido (SuspicionManager) -
/// un Slider (0 a sospechaMaxima) + texto opcional ("3/10"). También puede
/// deshabilitar el botón de "Aceptar intercambio" mientras no haya ningún
/// intercambio en curso, para que no se pueda apretar algo que no empezó.
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

    [Header("Botón de aceptar intercambio (opcional)")]
   // [Tooltip("Se deshabilita automáticamente si no hay un intercambio en curso.")]
    [SerializeField] private Button botonAceptarIntercambio;

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
        }

        suspicionManager.OnSospechaCambio += ActualizarBarra;
        suspicionManager.OnIntercambioCambio += ActualizarBotonAceptar;

        // Estado inicial - por si ya había un valor sincronizado antes de
        // que este script llegara a suscribirse (por ejemplo, si alguien se
        // conecta a mitad de partida).
        ActualizarBarra(suspicionManager.NivelSospecha);
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
        if (sliderSospecha != null)
        {
            sliderSospecha.value = nuevoValor;
        }

        /*if (textoSospecha != null)
        {
            textoSospecha.text = $"{Mathf.RoundToInt(nuevoValor)}/{Mathf.RoundToInt(suspicionManager.SospechaMaxima)}";
        }*/
    }

    private void ActualizarBotonAceptar(bool intercambioEnCurso)
    {
        if (botonAceptarIntercambio != null)
        {
            botonAceptarIntercambio.interactable = intercambioEnCurso;
        }
    }
}