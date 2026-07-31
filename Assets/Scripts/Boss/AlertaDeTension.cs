using System.Collections;
using UnityEngine;

/// <summary>
/// Texto de alerta ("¡El Boss está a punto de ganar!" o el que le pongas)
/// que aparece deslizándose desde un lado, se queda un momento quieto en el
/// centro, y se va deslizando hacia afuera de nuevo - con un sonido de
/// tensión apenas empieza a aparecer.
///
/// No sabe nada de reglas ni de red: BossManager solo llama a Mostrar() vía
/// ClientRpc cuando BossStrategy detecta que el boss quedó a una carta de
/// ganar. Podés reusarlo para cualquier otro aviso con el mismo estilo.
///
/// Requisitos en la escena:
/// - Poner este componente en el mismo GameObject que tiene el texto
///   (Text o TextMeshProUGUI) y su RectTransform.
/// - El objeto puede arrancar activo o desactivado - Mostrar() se encarga
///   de dejarlo visible mientras dura la animación y oculto el resto del
///   tiempo.
/// - Arrastrar un asset de SoundData (Assets > Create > Music > Sound Data)
///   en el campo "Sonido Tension" del Inspector - no hace falta ningún
///   AudioSource propio, SoundData.Play() ya usa el pool de AudioManager.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(CanvasGroup))]
public class AlertaDeTension : MonoBehaviour
{
    [Header("Texto")]
    [Tooltip("Mensaje que se muestra en la alerta. Se aplica automáticamente al Text o TextMeshProUGUI que esté en este mismo objeto.")]
    [SerializeField] private string mensaje = "La leyenda está a punto de ganar, le hace falta una carta.";

    [Header("Movimiento")]
    [Tooltip("Cuanto se desplaza (en unidades de UI / pixeles) desde y hacia afuera de pantalla.")]
    [SerializeField] private float distanciaDeslizamiento = 500f;
    [Tooltip("Si entra desde la izquierda (true) o desde la derecha (false).")]
    [SerializeField] private bool entraDesdeLaIzquierda = true;
    [Tooltip("Duracion del deslizamiento de entrada, en segundos.")]
    [SerializeField] private float duracionEntrada = 0.35f;
    [Tooltip("Cuanto se queda quieto en el centro antes de empezar a salir, en segundos.")]
    [SerializeField] private float duracionEnPantalla = 1.5f;
    [Tooltip("Duracion del deslizamiento de salida, en segundos.")]
    [SerializeField] private float duracionSalida = 0.35f;

    [Header("Audio")]
    [Tooltip("SoundData del sonido de tensión (SFX puntual) - se reproduce vía SoundData.Play(), que internamente usa el pool de AudioManager. No hace falta un AudioSource propio acá.")]
    [SerializeField] private SoundData sonidoTension;

    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private Vector2 posicionCentro;
    private Coroutine corrutinaActual;
    private bool posicionCentroCapturada;
    private TMPro.TMP_Text textoTMP;
    private UnityEngine.UI.Text textoLegacy;

    private void Awake()
    {
        AsegurarReferencias();
        CapturarPosicionCentroSiHaceFalta();
        AplicarMensaje();
    }

    /// <summary>
    /// Busca (una sola vez) RectTransform, CanvasGroup y el componente de
    /// texto. Se llama tanto desde Awake() como desde Mostrar() - si el
    /// objeto (o algun padre, como el Canvas) arranca desactivado en la
    /// escena, Awake() no corre hasta la primera activacion, asi que
    /// Mostrar() no puede depender de que ya haya corrido. GetComponent
    /// funciona igual con el objeto inactivo, por eso alcanza con esto.
    /// </summary>
    private void AsegurarReferencias()
    {
        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
        }

        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();

            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        if (textoTMP == null && textoLegacy == null)
        {
            textoTMP = GetComponent<TMPro.TMP_Text>();
            textoLegacy = textoTMP == null ? GetComponent<UnityEngine.UI.Text>() : null;
        }
    }

    /// <summary>Escribe 'mensaje' en el Text o TextMeshProUGUI que haya en este objeto - lo que sea que exista, sin que haga falta configurarlo a mano en cada uno.</summary>
    private void AplicarMensaje()
    {
        if (textoTMP != null)
        {
            textoTMP.text = mensaje;
        }
        else if (textoLegacy != null)
        {
            textoLegacy.text = mensaje;
        }
        else
        {
            Debug.LogWarning("[AlertaDeTension] No encontré ni un TMP_Text ni un Text en este objeto - el mensaje no se pudo aplicar.");
        }
    }

    /// <summary>
    /// Guarda la posicion "de reposo" (el centro donde debe quedar el texto
    /// mientras esta visible) la primera vez que se necesita. Se hace de
    /// forma perezosa (no solo en Awake) por si el objeto arranca
    /// desactivado en la escena - Awake corre igual aunque este
    /// desactivado, asi que en la practica alcanza con Awake, pero esta
    /// verificacion evita problemas si en algun momento se instancia por
    /// codigo en vez de vivir ya en la escena.
    /// </summary>
    private void CapturarPosicionCentroSiHaceFalta()
    {
        if (posicionCentroCapturada)
        {
            return;
        }

        posicionCentro = rectTransform.anchoredPosition;
        posicionCentroCapturada = true;
    }

    /// <summary>Dispara la secuencia completa de aparicion. Si ya estaba animando, la reinicia desde el principio.</summary>
    public void Mostrar()
    {
        // Por si Awake() todavia no corrio (objeto o padre desactivado
        // desde el arranque de la escena) - sin esto, rectTransform y
        // canvasGroup pueden llegar nulos aca.
        AsegurarReferencias();

        // El objeto arranca DESACTIVADO en la escena - hay que activarlo
        // antes de arrancar la corutina: Unity no deja arrancar una
        // corutina en un GameObject inactivo.
        gameObject.SetActive(true);

        CapturarPosicionCentroSiHaceFalta();
        AplicarMensaje();

        if (corrutinaActual != null)
        {
            StopCoroutine(corrutinaActual);
        }

        corrutinaActual = StartCoroutine(SecuenciaDeAlerta());
    }

    private IEnumerator SecuenciaDeAlerta()
    {
        float signo = entraDesdeLaIzquierda ? -1f : 1f;
        Vector2 posicionAfuera = posicionCentro + new Vector2(signo * distanciaDeslizamiento, 0f);

        rectTransform.anchoredPosition = posicionAfuera;
        canvasGroup.alpha = 0f;

        if (sonidoTension != null)
        {
            sonidoTension.Play();
        }

        yield return AnimarEntradaOSalida(posicionAfuera, posicionCentro, 0f, 1f, duracionEntrada);

        yield return new WaitForSeconds(duracionEnPantalla);

        yield return AnimarEntradaOSalida(posicionCentro, posicionAfuera, 1f, 0f, duracionSalida);

        gameObject.SetActive(false);
        rectTransform.anchoredPosition = posicionCentro; // listo para la proxima vez
        canvasGroup.alpha = 1f;
        corrutinaActual = null;
    }

    /// <summary>Anima posicion y opacidad al mismo tiempo y con la misma curva - se usa tanto para la entrada como para la salida, solo cambian los valores de desde/hasta.</summary>
    private IEnumerator AnimarEntradaOSalida(Vector2 desdePos, Vector2 hastaPos, float desdeAlpha, float hastaAlpha, float duracion)
    {
        if (duracion <= 0f)
        {
            rectTransform.anchoredPosition = hastaPos;
            canvasGroup.alpha = hastaAlpha;
            yield break;
        }

        float tiempoTranscurrido = 0f;

        while (tiempoTranscurrido < duracion)
        {
            tiempoTranscurrido += Time.deltaTime;
            float t = Mathf.Clamp01(tiempoTranscurrido / duracion);
            float tSuavizado = t * t * (3f - 2f * t); // smoothstep - evita que se vea robotico/lineal
            rectTransform.anchoredPosition = Vector2.Lerp(desdePos, hastaPos, tSuavizado);
            canvasGroup.alpha = Mathf.Lerp(desdeAlpha, hastaAlpha, tSuavizado);
            yield return null;
        }

        rectTransform.anchoredPosition = hastaPos;
        canvasGroup.alpha = hastaAlpha;
    }
}