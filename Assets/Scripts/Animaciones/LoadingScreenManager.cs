using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LoadingScreenManager : MonoBehaviour
{
    public static LoadingScreenManager Instance;

    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float fadeDuration = 0.4f;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        canvasGroup.alpha = 0;
        canvasGroup.blocksRaycasts = false;
    }

    public void LoadScene(string sceneName, System.Func<IEnumerator> extraWaitRoutine = null)
    {
        StartCoroutine(LoadRoutine(sceneName, extraWaitRoutine));
    }

    private IEnumerator LoadRoutine(string sceneName, System.Func<IEnumerator> extraWaitRoutine)
    {
        // 1. Fade a negro (mostrar pantalla de carga)
        yield return Fade(0f, 1f);

        // 2. Cargar la escena en background, sin activarla todavía
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        op.allowSceneActivation = false;

        while (op.progress < 0.9f)
            yield return null;

        // 3. Esperar algo extra si hace falta (ej. respuesta de PlayFab)
        if (extraWaitRoutine != null)
            yield return StartCoroutine(extraWaitRoutine());

        // 4. Activar la escena ya cargada
        op.allowSceneActivation = true;
        while (!op.isDone)
            yield return null;

        // 5. Fade de vuelta (ocultar pantalla de carga)
        yield return Fade(1f, 0f);
    }

    private IEnumerator Fade(float from, float to)
    {
        canvasGroup.blocksRaycasts = to > 0;
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(from, to, t / fadeDuration);
            yield return null;
        }
        canvasGroup.alpha = to;
    }
}
