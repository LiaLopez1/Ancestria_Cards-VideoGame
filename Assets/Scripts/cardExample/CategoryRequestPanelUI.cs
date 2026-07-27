using UnityEngine;

/// <summary>
/// Panel con un botón por cada categoría (4 en total). Al elegir una, le
/// avisa a TrapManager, que se encarga de mandarlo al servidor.
///
/// Los 4 botones se conectan uno por uno desde el Inspector (OnClick()),
/// cada uno a su método correspondiente - no hay lógica genérica de lista,
/// porque las categorías son un enum fijo de 4 valores.
/// </summary>
public class CategoryRequestPanelUI : MonoBehaviour
{
    [SerializeField] private GameObject panelCategorias;
    [SerializeField] private TrapManager trapManager;

    private void Awake()
    {
        panelCategorias.SetActive(false);
    }

    /// <summary>Conectar al botón "Pedir por categoría" dentro del panel de trampas.</summary>
    public void Abrir()
    {
        panelCategorias.SetActive(true);
    }

    public void Cerrar()
    {
        panelCategorias.SetActive(false);
    }

    public void OnPedirProtectoresPressed()
    {
        Solicitar(CardCategory.Protectores);
    }

    public void OnPedirCastigadoresPressed()
    {
        Solicitar(CardCategory.Castigadores);
    }

    public void OnPedirTransformadosPressed()
    {
        Solicitar(CardCategory.Transformados);
    }

    public void OnPedirAparicionesPressed()
    {
        Solicitar(CardCategory.Apariciones);
    }

    private void Solicitar(CardCategory categoria)
    {
        trapManager.SolicitarCategoria(categoria);
        Cerrar();
    }
}