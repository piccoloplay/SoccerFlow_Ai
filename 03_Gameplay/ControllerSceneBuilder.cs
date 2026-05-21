using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;

/// <summary>
/// SoccerFlaw_Ai — ControllerSceneBuilder
/// Crea l'intera UI della scena Controller in modo programmatico.
/// Simile a SceneSpawner per la scena Game: zero setup manuale nell'Editor.
///
/// SETUP:
/// 1. Crea una nuova scena Unity chiamata "Controller".
/// 2. Aggiungi un GameObject vuoto "ControllerSceneBuilder".
/// 3. Aggiungi questo script.
/// 4. Premi Play — tutto viene creato automaticamente.
/// </summary>
public class ControllerSceneBuilder : MonoBehaviour
{
    [Header("─── Colori ────────────────────────────────────")]
    [SerializeField] private Color bgColor        = new Color(0.10f, 0.10f, 0.14f);
    [SerializeField] private Color panelActive    = new Color(0.15f, 0.37f, 0.78f);
    [SerializeField] private Color panelDisabled  = new Color(0.22f, 0.22f, 0.28f);
    [SerializeField] private Color panelEmpty     = new Color(0.14f, 0.14f, 0.18f);
    [SerializeField] private Color accentColor    = new Color(0.95f, 0.85f, 0.10f);

    [Header("─── Colori Giocatori ───────────────────────────")]
    [SerializeField] private Color p1Color = Color.red;
    [SerializeField] private Color p2Color = new Color(0.15f, 0.37f, 0.78f);
    [SerializeField] private Color p3Color = new Color(0.18f, 0.60f, 0.25f);
    [SerializeField] private Color p4Color = new Color(0.95f, 0.85f, 0.10f);

    void Start()
    {
        SetupCamera();
        var (canvas, root) = BuildCanvas();
        BuildHUD(root);
        var panels = BuildGrid(root);
        BuildScores(root);
        WireControllerManager(canvas, panels);
    }

    // ─── CAMERA ──────────────────────────────────────────────────────────────

    void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        cam.clearFlags       = CameraClearFlags.SolidColor;
        cam.backgroundColor  = bgColor;
    }

    // ─── CANVAS ──────────────────────────────────────────────────────────────

    (Canvas canvas, RectTransform root) BuildCanvas()
    {
        var go     = new GameObject("Canvas");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight  = 0.5f;

        go.AddComponent<GraphicRaycaster>();

        // EventSystem
        if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
        }

        return (canvas, go.GetComponent<RectTransform>());
    }

    // ─── HUD (domanda + turno + timer) ───────────────────────────────────────

    (TextMeshProUGUI question, TextMeshProUGUI turn, TextMeshProUGUI timer) BuildHUD(RectTransform parent)
    {
        // Sfondo header
        var header = MakeRect("Header", parent);
        SetAnchors(header, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -220));
        SetBg(header, new Color(0, 0, 0, 0.45f));

        // Domanda
        var qText = MakeTMP("QuestionText", header);
        SetAnchors(qText.rectTransform, new Vector2(0.05f, 0.5f), new Vector2(0.95f, 1f), Vector2.zero);
        qText.text      = "In attesa della domanda…";
        qText.fontSize  = 36;
        qText.alignment = TextAlignmentOptions.Center;
        qText.color     = Color.white;

        // Turno
        var turnText = MakeTMP("TurnText", header);
        SetAnchors(turnText.rectTransform, new Vector2(0.05f, 0f), new Vector2(0.55f, 0.5f), Vector2.zero);
        turnText.text      = "Turno: —";
        turnText.fontSize  = 30;
        turnText.alignment = TextAlignmentOptions.Left;
        turnText.color     = Color.white;

        // Timer
        var timerText = MakeTMP("TimerText", header);
        SetAnchors(timerText.rectTransform, new Vector2(0.6f, 0f), new Vector2(0.95f, 0.5f), Vector2.zero);
        timerText.text      = "15";
        timerText.fontSize  = 44;
        timerText.alignment = TextAlignmentOptions.Right;
        timerText.color     = accentColor;
        timerText.fontStyle = FontStyles.Bold;

        return (qText, turnText, timerText);
    }

    // ─── GRIGLIA 3×3 ─────────────────────────────────────────────────────────

    ControllerPanel[] BuildGrid(RectTransform parent)
    {
        var gridGO  = MakeRect("PanelGrid", parent);
        // Centra la griglia verticalmente nel mezzo dello schermo
        SetAnchors(gridGO, new Vector2(0.04f, 0.18f), new Vector2(0.96f, 0.82f), Vector2.zero);

        var grid             = gridGO.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize        = new Vector2(300, 240);
        grid.spacing         = new Vector2(20, 20);
        grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment  = TextAnchor.MiddleCenter;

        var panels = new ControllerPanel[9];
        for (int i = 0; i < 9; i++)
        {
            var btnGO  = new GameObject($"Panel_{i}");
            btnGO.transform.SetParent(gridGO, false);

            // Sfondo
            var img   = btnGO.AddComponent<Image>();
            img.color = panelDisabled;

            // Bottone
            var btn   = btnGO.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor      = panelDisabled;
            colors.highlightedColor = panelActive;
            colors.pressedColor     = Color.white;
            colors.disabledColor    = panelEmpty;
            btn.colors = colors;

            // Label
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(btnGO.transform, false);
            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text              = "";
            tmp.fontSize          = 26;
            tmp.alignment         = TextAlignmentOptions.Center;
            tmp.color             = Color.white;
            tmp.enableWordWrapping = true;
            var rt = labelGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(10, 10);
            rt.offsetMax = new Vector2(-10, -10);

            // ControllerPanel
            var cp = btnGO.AddComponent<ControllerPanel>();
            cp.panelIndex   = i;
            cp.button       = btn;
            cp.labelText    = tmp;

            panels[i] = cp;
        }
        return panels;
    }

    // ─── PUNTEGGI ─────────────────────────────────────────────────────────────

    (TextMeshProUGUI p1, TextMeshProUGUI p2, TextMeshProUGUI p3, TextMeshProUGUI p4) BuildScores(RectTransform parent)
    {
        var bar = MakeRect("ScoreBar", parent);
        SetAnchors(bar, new Vector2(0, 0), new Vector2(1, 0.15f), Vector2.zero);
        SetBg(bar, new Color(0, 0, 0, 0.45f));

        // Riga centrata con HorizontalLayoutGroup → auto-centratura quando alcuni player sono nascosti
        var rowGO = new GameObject("ScoresRow");
        rowGO.transform.SetParent(bar, false);
        var rowRT = rowGO.AddComponent<RectTransform>();
        rowRT.anchorMin = Vector2.zero; rowRT.anchorMax = Vector2.one;
        rowRT.offsetMin = Vector2.zero; rowRT.offsetMax = Vector2.zero;
        var hlg = rowGO.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        hlg.childAlignment        = TextAnchor.MiddleCenter;
        hlg.spacing               = 60f;
        hlg.childControlWidth      = false;
        hlg.childControlHeight     = false;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;

        var s1 = MakeScoreLabel("Score1", rowGO.transform, p1Color, "P1: 0");
        var s2 = MakeScoreLabel("Score2", rowGO.transform, p2Color, "P2: 0");
        var s3 = MakeScoreLabel("Score3", rowGO.transform, p3Color, "P3: 0");
        var s4 = MakeScoreLabel("Score4", rowGO.transform, p4Color, "P4: 0");

        return (s1, s2, s3, s4);
    }

    TextMeshProUGUI MakeScoreLabel(string name, Transform parent, Color color, string text)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = 40;
        tmp.color     = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;

        var le = go.AddComponent<UnityEngine.UI.LayoutElement>();
        le.preferredWidth  = 200f;
        le.preferredHeight = 70f;
        return tmp;
    }

    // ─── WIRE CONTROLLERMANAGER ──────────────────────────────────────────────

    void WireControllerManager(Canvas canvas, ControllerPanel[] panels)
    {
        var canvasGO = canvas.gameObject;

        // ControllerManager + PhotonView sullo stesso GameObject del Canvas
        var cm = canvasGO.AddComponent<ControllerManager>();
        canvasGO.AddComponent<PhotonView>();

        cm.panels = panels;
        cm.p1Color = p1Color;
        cm.p2Color = p2Color;
        cm.p3Color = p3Color;
        cm.p4Color = p4Color;

        // Trova i TMP creati per nome e li assegna
        cm.questionText = canvasGO.GetComponentInChildren<TextMeshProUGUI>(true);

        // Assegna per nome
        foreach (var tmp in canvasGO.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            switch (tmp.gameObject.name)
            {
                case "QuestionText": cm.questionText = tmp; break;
                case "TurnText":     cm.turnText     = tmp; break;
                case "TimerText":    cm.timerText    = tmp; break;
                case "Score1":       cm.scoreP1      = tmp; break;
                case "Score2":       cm.scoreP2      = tmp; break;
                case "Score3":       cm.scoreP3      = tmp; break;
                case "Score4":       cm.scoreP4      = tmp; break;
            }
        }
    }

    // ─── UTILITY UI ──────────────────────────────────────────────────────────

    static RectTransform MakeRect(string name, RectTransform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    static TextMeshProUGUI MakeTMP(string name, RectTransform parent)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go.AddComponent<TextMeshProUGUI>();
    }

    static void SetAnchors(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 sizeDelta)
    {
        rt.anchorMin  = anchorMin;
        rt.anchorMax  = anchorMax;
        rt.offsetMin  = Vector2.zero;
        rt.offsetMax  = Vector2.zero;
        if (sizeDelta != Vector2.zero) rt.sizeDelta = sizeDelta;
    }

    static void SetBg(RectTransform rt, Color color)
    {
        var img   = rt.gameObject.AddComponent<Image>();
        img.color = color;
    }
}
