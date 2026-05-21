using UnityEngine;
using UnityEngine.UI;
using TMPro;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// SoccerFlaw_Ai — HUDManager
/// Gestisce l'HUD (domanda, turno, timer, punteggi) + lo costruisce.
/// Due modalità:
///  - BAKED (consigliato): click "Build HUD" → Canvas + testi in scena, editabili.
///  - RUNTIME fallback: se i riferimenti sono null in Awake, costruisce a runtime.
/// </summary>
public class HUDManager : MonoBehaviour
{
    [Header("─── DOMANDA ─────────────────────────────────")]
    public TextMeshProUGUI questionText;

    [Header("─── TURNO & TIMER ──────────────────────────")]
    public TextMeshProUGUI turnText;
    public TextMeshProUGUI timerText;

    [Header("─── PUNTEGGI ────────────────────────────────")]
    public TextMeshProUGUI scoreP1;
    public TextMeshProUGUI scoreP2;
    public TextMeshProUGUI scoreP3;
    public TextMeshProUGUI scoreP4;

    [Header("─── COLORI GIOCATORI ────────────────────────")]
    public Color player1Color = Color.red;
    public Color player2Color = new Color(0.15f, 0.37f, 0.78f);
    public Color player3Color = new Color(0.18f, 0.60f, 0.25f);
    public Color player4Color = new Color(0.95f, 0.85f, 0.10f);

    // Singleton — accessibile da GameManager senza FindObjectOfType
    public static HUDManager Instance { get; private set; }

    // ─── Barra timer ─────────────────────────────────────────────────────────
    RectTransform timerBar;
    const float   timerBarMaxWidth = 960f;   // metà schermo (ref 1920)
    int           timerMax         = 15;     // aggiornato all'inizio di ogni round
    int           lastTimerSeconds = 0;

    void Awake()
    {
        Instance = this;

        // GARANZIA UN SOLO HUD: nella scena Game l'unica UI è l'HUD.
        // Distruggi qualsiasi canvas pre-esistente (vecchio manuale o bakato duplicato),
        // poi costruisci un singolo HUD pulito. Risolve definitivamente i testi sovrapposti.
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c == null) continue;
            if (transform.IsChildOf(c.transform)) continue; // non distruggere un canvas che mi contiene
            Destroy(c.gameObject);
        }

        questionText = turnText = timerText = scoreP1 = scoreP2 = scoreP3 = scoreP4 = null;
        BuildHUD();
    }

    // ─── COSTRUZIONE HUD ─────────────────────────────────────────────────────

    void BuildHUD()
    {
        // Canvas
        var canvasGO = new GameObject("HUD_Canvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();
        var root = canvasGO.GetComponent<RectTransform>();

        // Domanda: in basso, poco sopra la barra timer e i punteggi
        questionText = MakeText("QuestionText", root, new Vector2(0.5f, 0f), new Vector2(0f, 185f), new Vector2(1300f, 80f), 34f, TextAlignmentOptions.Center);
        // Turno: in alto a destra
        turnText     = MakeText("TurnText",     root, new Vector2(1f, 1f),   new Vector2(-20f, -30f), new Vector2(240f, 50f), 22f, TextAlignmentOptions.Right);
        // Timer numero: in alto a sinistra, più grande
        timerText    = MakeText("TimerText",    root, new Vector2(0f, 1f),   new Vector2(30f, -45f),  new Vector2(180f, 80f), 48f, TextAlignmentOptions.Left);

        // ── Barra timer: pannello nero semi-trasparente, centrato, sotto la domanda.
        //    Larghezza proporzionale al tempo rimanente, si restringe da entrambi i lati. ──
        var barGO = new GameObject("TimerBar");
        barGO.transform.SetParent(root, false);
        timerBar = barGO.AddComponent<RectTransform>();
        timerBar.anchorMin        = new Vector2(0.5f, 0f);
        timerBar.anchorMax        = new Vector2(0.5f, 0f);
        timerBar.pivot            = new Vector2(0.5f, 0f);
        timerBar.anchoredPosition = new Vector2(0f, 135f);
        timerBar.sizeDelta        = new Vector2(timerBarMaxWidth, 26f);
        var barImg = barGO.AddComponent<Image>();
        barImg.color = new Color(0f, 0f, 0f, 0.55f);

        // ── Punteggi: riga centrata in basso, font grande, auto-centratura ──
        var scoresRow = new GameObject("ScoresRow");
        scoresRow.transform.SetParent(root, false);
        var rowRT = scoresRow.AddComponent<RectTransform>();
        rowRT.anchorMin        = new Vector2(0.5f, 0f);
        rowRT.anchorMax        = new Vector2(0.5f, 0f);
        rowRT.pivot            = new Vector2(0.5f, 0f);
        rowRT.anchoredPosition = new Vector2(0f, 40f);
        rowRT.sizeDelta        = new Vector2(1400f, 90f);

        var hlg = scoresRow.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment        = TextAnchor.MiddleCenter;
        hlg.spacing               = 50f;
        hlg.childControlWidth      = false;
        hlg.childControlHeight     = false;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;

        scoreP1 = MakeScoreLabel("ScoreP1", scoresRow.transform, player1Color);
        scoreP2 = MakeScoreLabel("ScoreP2", scoresRow.transform, player2Color);
        scoreP3 = MakeScoreLabel("ScoreP3", scoresRow.transform, player3Color);
        scoreP4 = MakeScoreLabel("ScoreP4", scoresRow.transform, player4Color);
    }

    static TextMeshProUGUI MakeScoreLabel(string name, Transform parent, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize  = 42;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = color;

        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth  = 260f;
        le.preferredHeight = 80f;
        return tmp;
    }

    static TextMeshProUGUI MakeText(string name, RectTransform parent, Vector2 anchor, Vector2 pos, Vector2 size, float fontSize, TextAlignmentOptions align)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        var rt  = tmp.rectTransform;
        rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        tmp.fontSize  = fontSize;
        tmp.alignment = align;
        tmp.color     = Color.white;
        return tmp;
    }

    void Start()
    {
        // Stato iniziale
        SetQuestion("In attesa della domanda…");
        SetTurn(1);
        SetTimer(15);
        SetScore(1, 0);
        SetScore(2, 0);
        SetScore(3, 0);
        SetScore(4, 0);

        // Colori fissi punteggi
        if (scoreP1) scoreP1.color = player1Color;
        if (scoreP2) scoreP2.color = player2Color;
        if (scoreP3) scoreP3.color = player3Color;
        if (scoreP4) scoreP4.color = player4Color;
    }

    // ─── API PUBBLICA ────────────────────────────────────────────────────────

    /// <summary>Mostra la domanda corrente.</summary>
    public void SetQuestion(string question)
    {
        if (questionText == null) return;
        questionText.text  = question;
        questionText.color = Color.white;
    }

    /// <summary>Aggiorna il testo del turno con il colore del giocatore (1-4).</summary>
    public void SetTurn(int playerIndex)
    {
        if (turnText == null) return;
        turnText.text  = $"Turno: P{playerIndex}";
        turnText.color = PlayerColor(playerIndex);
    }

    /// <summary>Aggiorna il timer (numero + barra). Sotto 5 secondi diventa rosso.</summary>
    public void SetTimer(int seconds)
    {
        // Rileva inizio nuovo round: se il valore sale, è il max di quel round
        if (seconds > lastTimerSeconds) timerMax = seconds;
        lastTimerSeconds = seconds;

        if (timerText != null)
        {
            timerText.text  = seconds.ToString();
            timerText.color = seconds <= 5 ? Color.red : Color.white;
        }

        // Barra: larghezza proporzionale al tempo rimanente, centrata (pivot 0.5 → restringe da entrambi i lati)
        if (timerBar != null && timerMax > 0)
        {
            float frac = Mathf.Clamp01((float)seconds / timerMax);
            var sd = timerBar.sizeDelta;
            sd.x = timerBarMaxWidth * frac;
            timerBar.sizeDelta = sd;
        }
    }

    /// <summary>Aggiorna il punteggio di un giocatore (playerIndex 1-4).</summary>
    public void SetScore(int playerIndex, int score)
    {
        var label = ScoreLabel(playerIndex);
        if (label) label.text = $"P{playerIndex}: {score}";
    }

    /// <summary>Mostra/nasconde la label punteggio di un giocatore (slot vuoto = nascosto).</summary>
    public void SetPlayerActive(int playerIndex, bool active)
    {
        var label = ScoreLabel(playerIndex);
        if (label) label.gameObject.SetActive(active);
    }

    /// <summary>
    /// Feedback visivo sulla domanda dopo il tiro.
    ///   saved=true → giallo "PARATA!" (anche se la risposta era giusta, punto annullato)
    ///   correct=true (e !saved) → verde "Corretto!"
    ///   altrimenti → rosso "Sbagliato"
    /// </summary>
    public void ShowAnswerFeedback(bool correct, bool saved = false)
    {
        if (questionText == null) return;

        string originalText = questionText.text;

        if (saved)
        {
            questionText.color = new Color(0.95f, 0.75f, 0.10f); // giallo
            questionText.text  = correct
                ? "PARATA! Risposta giusta ma punto annullato"
                : "PARATA!";
        }
        else if (correct)
        {
            questionText.color = Color.green;
            questionText.text  = "Corretto!";
        }
        else
        {
            questionText.color = Color.red;
            questionText.text  = "Sbagliato";
        }

        // Ripristina dopo 2s
        CancelInvoke(nameof(ResetQuestionColor));
        Invoke(nameof(ResetQuestionColor), 2f);
        // Salva il testo originale così ResetQuestionColor lo ripristina
        pendingOriginalText = originalText;
    }

    string pendingOriginalText;

    // ─── INTERNAL ────────────────────────────────────────────────────────────

    void ResetQuestionColor()
    {
        if (questionText == null) return;
        questionText.color = Color.white;
        if (!string.IsNullOrEmpty(pendingOriginalText))
            questionText.text = pendingOriginalText;
    }

    Color PlayerColor(int idx) => idx switch
    {
        1 => player1Color,
        2 => player2Color,
        3 => player3Color,
        4 => player4Color,
        _ => Color.white
    };

    TextMeshProUGUI ScoreLabel(int idx) => idx switch
    {
        1 => scoreP1,
        2 => scoreP2,
        3 => scoreP3,
        4 => scoreP4,
        _ => null
    };

#if UNITY_EDITOR
    // ─── BAKE EDITOR ─────────────────────────────────────────────────────────

    public void BuildHUDInEditor()
    {
        ClearHUD();
        BuildHUD();
        EditorUtility.SetDirty(this);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        Debug.Log("[HUDManager] HUD bakato in scena. Salva (Ctrl+S) e abbelliscilo nell'editor.");
    }

    public void ClearHUD()
    {
        // Distrugge TUTTI i canvas HUD nella scena (vecchio manuale + eventuali bakati),
        // così non restano duplicati sovrapposti.
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c == null) continue;
            bool isHud = c.name == "HUD_Canvas"
                      || c.transform.Find("QuestionText") != null
                      || c.transform.Find("ScoresRow")    != null
                      || c.transform.Find("ScoreP1")      != null;
            if (isHud) DestroyImmediate(c.gameObject);
        }
        questionText = turnText = timerText = scoreP1 = scoreP2 = scoreP3 = scoreP4 = null;
    }
#endif
}

#if UNITY_EDITOR
[CustomEditor(typeof(HUDManager))]
public class HUDManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var h = (HUDManager)target;

        EditorGUILayout.Space(5);
        GUILayout.Label("HUD Manager", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Build HUD → bake il Canvas HUD in scena (domanda/turno/timer/punteggi).\n" +
            "Una volta bakato puoi abbellirlo visualmente; il Play non lo ricostruisce.",
            MessageType.Info);

        DrawDefaultInspector();
        EditorGUILayout.Space(8);

        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
        if (GUILayout.Button("Build HUD", GUILayout.Height(34)))
            h.BuildHUDInEditor();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(4);
        if (GUILayout.Button("Clear HUD"))
            h.ClearHUD();
    }
}
#endif