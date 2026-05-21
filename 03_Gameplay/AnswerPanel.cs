using UnityEngine;
using TMPro;

/// <summary>
/// SoccerFlaw_Ai — AnswerPanel
/// Componente da aggiungere a ognuno dei 9 Cube pannelli nella scena Game.
/// Si occupa di: label testuale, feedback colore, rilevamento collisione con il pallone.
///
/// SETUP nell'editor:
/// 1. Seleziona Panel_0 … Panel_8 e aggiungi questo script.
/// 2. Imposta panelIndex (0-8) nell'Inspector di ciascun pannello.
/// 3. Lo script crea automaticamente un TextMeshPro figlio per la label.
///    In alternativa puoi trascinare un TMP esistente nel campo labelText.
/// 4. Trascina i 9 pannelli nell'array answerPanels[] del GameManager.
/// </summary>
[RequireComponent(typeof(Collider))]
public class AnswerPanel : MonoBehaviour
{
    [Header("─── Identità ──────────────────────────────────")]
    public int panelIndex;

    [Header("─── Label TMP (lascia vuoto: creata in automatico) ──")]
    public TextMeshPro labelText;

    [Header("─── Colori ─────────────────────────────────────")]
    public Color defaultColor  = new Color(0.15f, 0.37f, 0.78f);
    public Color correctColor  = new Color(0.10f, 0.72f, 0.25f);
    public Color wrongColor    = new Color(0.85f, 0.15f, 0.15f);
    public Color savedColor    = new Color(0.95f, 0.75f, 0.10f);  // giallo = pannello scelto ma parato
    public Color emptyColor    = new Color(0.25f, 0.25f, 0.25f);

    // ─────────────────────────────────────────────────────────────────────────

    MeshRenderer mr;
    bool hitProcessed;

    void Awake()
    {
        mr = GetComponent<MeshRenderer>();
        EnsureLabel();
    }

    void EnsureLabel()
    {
        if (labelText != null) return;

        var child = new GameObject("Label");
        child.transform.SetParent(transform, false);
        child.transform.localPosition = new Vector3(0, 0, -0.6f); // davanti al pannello
        child.transform.localRotation = Quaternion.identity;
        child.transform.localScale    = new Vector3(0.6f, 0.6f, 0.6f);

        labelText                  = child.AddComponent<TextMeshPro>();
        labelText.alignment        = TextAlignmentOptions.Center;
        labelText.fontSize         = 4f;
        labelText.color            = Color.white;
        labelText.text             = "";
        labelText.enableWordWrapping = true;
        // Imposta il RectTransform abbastanza grande
        var rt     = child.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(1.4f, 0.9f);
    }

    // ─── API PUBBLICA ────────────────────────────────────────────────────────

    /// <summary>Imposta il testo della risposta. Stringa vuota = pannello grigio.</summary>
    public void SetLabel(string text)
    {
        if (labelText) labelText.text = text ?? "";
        bool empty = string.IsNullOrEmpty(text);
        SetColor(empty ? emptyColor : defaultColor);
    }

    /// <summary>Mostra feedback verde/rosso dopo il tiro.</summary>
    public void SetFeedback(bool correct)
        => SetColor(correct ? correctColor : wrongColor);

    /// <summary>Pannello scelto dal giocatore ma intercettato dal portiere → giallo.</summary>
    public void SetFeedbackSaved()
        => SetColor(savedColor);

    /// <summary>Torna al colore default blu.</summary>
    public void ResetColor()
        => SetColor(string.IsNullOrEmpty(labelText?.text) ? emptyColor : defaultColor);

    // ─── COLLISIONE CON IL PALLONE ───────────────────────────────────────────

    void OnCollisionEnter(Collision col)
    {
        if (hitProcessed) return;
        if (!col.gameObject.CompareTag("Ball")) return;

        hitProcessed = true;
        // NOTA: la notifica a GameManager è ora gestita da BallController.OnCollisionEnter,
        // che ha accesso a playerIndex (necessario per Mischia). Questo OnCollisionEnter
        // resta solo per il flag hitProcessed (reset per turno via ResetHit()).
    }

    // Reset hit flag all'inizio di ogni turno (chiamato da GameManager)
    public void ResetHit() { hitProcessed = false; }

    // ─── UTILITY ─────────────────────────────────────────────────────────────

    void SetColor(Color c)
    {
        if (mr == null) return;
        if (mr.material.HasProperty("_BaseColor"))
            mr.material.SetColor("_BaseColor", c);
        mr.material.color = c;
    }
}
