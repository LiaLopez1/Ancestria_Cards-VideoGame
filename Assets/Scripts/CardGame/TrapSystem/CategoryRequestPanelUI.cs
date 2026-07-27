using UnityEngine;

/// <summary>
/// Panel con un botón por cada categoría (4 en total). Este mismo panel se
/// reutiliza para dos trampas distintas - "Pedir" (necesito esa categoría) y
/// "Mostrar" (tengo esa categoría) - según cuál botón externo lo abrió.
/// Abrir()/AbrirParaMostrar() fijan el modo antes de mostrar el panel; al
/// elegir una categoría, Solicitar() usa ese modo para llamar al método
/// correspondiente de TrapManager, que se encarga de mandarlo al servidor.
///
/// Los 4 botones de categoría se conectan uno por uno desde el Inspector
/// (OnClick()), cada uno a su método correspondiente - no hay lógica
/// genérica de lista, porque las categorías son un enum fijo de 4 valores.
/// </summary>
public class CategoryRequestPanelUI : MonoBehaviour
{
    [SerializeField] private GameObject panelCategorias;
    [SerializeField] private TrapManager trapManager;

    // Modo actual: decide si al elegir una categoria se "pide" (necesita)
    // o se "muestra" (tiene). Lo fija el boton externo que abrio el panel.
    private bool modoMostrar;

    private void Awake()
    {
        panelCategorias.SetActive(false);
    }

    /// <summary>Conectar al botón "Pedir por categoría" dentro del panel de trampas.</summary>
    public void Abrir()
    {
        modoMostrar = false;
        panelCategorias.SetActive(true);
    }

    /// <summary>Conectar al botón "Mostrar" dentro del panel de trampas.</summary>
    public void AbrirParaMostrar()
    {
        modoMostrar = true;
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
        if (modoMostrar)
        {
            trapManager.MostrarCategoria(categoria);
        }
        else
        {
            trapManager.SolicitarCategoria(categoria);
        }

        Cerrar();
    }
}