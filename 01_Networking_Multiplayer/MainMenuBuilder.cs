using UnityEngine;
using UnityEngine.UI;
using TMPro;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// SoccerFlaw_Ai — MainMenuBuilder
/// Costruisce la UI della MainMenu. Due modalità:
///  - RUNTIME (default storico): se non trova una UI bakata, la costruisce in Awake.
///  - EDIT-TIME (nuovo): bottone "Build Menu UI" → bake la UI in scena per editing visuale.
///    Una volta bakata, Awake la rileva e NON ricostruisce → puoi abbellirla nell'editor.
///
/// SETUP:
/// 1. Apri/crea scena MainMenu con un GameObject "MainMenu" + MainMenuBuilder + NetworkManager
/// 2. Click "Build Menu UI" → la UI appare in scena (sotto al GameObject)
/// 3. Ctrl+S → abbellisci visualmente quando vuoi
/// </summary>
public class MainMenuBuilder : MonoBehaviour
{
    [Header("─── Colori ────────────────────────────────────")]
    [SerializeField] private Color bgColor      = new Color(0.08f, 0.08f, 0.12f);
    [SerializeField] private Color panelColor   = new Color(0.14f, 0.14f, 0.20f);
    [SerializeField] private Color accentColor  = new Color(0.15f, 0.45f, 0.90f);
    [SerializeField] private Color dangerColor  = new Color(0.85f, 0.20f, 0.20f);
    [SerializeField] private Color textColor    = Color.white;

    MainMenuUI ui;

    void Awake()
    {
        SetupCamera();

        // Se la UI è già bakata in scena (Canvas figlio esiste), non ricostruire:
        // MainMenuUI ha già i riferimenti serializzati.
        if (HasBakedUI())
        {
            ui = GetComponent<MainMenuUI>();
            return;
        }

        // Fallback runtime: costruisci da codice come prima
        ui = GetComponent<MainMenuUI>();
        if (ui == null) ui = gameObject.AddComponent<MainMenuUI>();
        BuildUI();
    }

    /// <summary>
    /// Ricostruisce la UI a runtime. Usato tornando al MainMenu dopo una partita:
    /// il GameObject è persistente (DontDestroyOnLoad) e la UI era stata distrutta
    /// entrando in gioco, quindi va rigenerata.
    /// </summary>
    public void RebuildUI()
    {
        var oldCanvas = GetComponentInChildren<Canvas>(true);
        if (oldCanvas != null) Destroy(oldCanvas.gameObject);

        ui = GetComponent<MainMenuUI>();
        if (ui == null) ui = gameObject.AddComponent<MainMenuUI>();
        BuildUI();
    }

    bool HasBakedUI()
    {
        // UI bakata = esiste un Canvas figlio di questo GameObject
        return GetComponentInChildren<Canvas>(true) != null
            && GetComponent<MainMenuUI>() != null
            && GetComponent<MainMenuUI>().panelConnecting != null;
    }

    void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = bgColor;
    }

    void BuildUI()
    {
        var canvas = MakeCanvas();
        var root   = canvas.GetComponent<RectTransform>();

        ui.panelConnecting = BuildPanelConnecting(root);
        ui.panelLobby      = BuildPanelLobby(root);
        ui.panelRoom       = BuildPanelRoom(root);

        // Tutti nascosti tranne Connecting (MainMenuUI li mostra al momento giusto)
        ui.panelConnecting.SetActive(true);
        ui.panelLobby.SetActive(false);
        ui.panelRoom.SetActive(false);
    }

    // ─── CANVAS ──────────────────────────────────────────────────────────────

    Canvas MakeCanvas()
    {
        var go     = new GameObject("Canvas");
        go.transform.SetParent(transform, false);   // figlio di MainMenuBuilder → detection bake
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight  = 0.5f;

        go.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();
        return canvas;
    }

    // ─── PANEL: CONNECTING ───────────────────────────────────────────────────

    GameObject BuildPanelConnecting(RectTransform root)
    {
        var panel = MakeFullPanel("PanelConnecting", root, bgColor);

        var logo = MakeTMP("Logo", panel);
        SetAnchors(logo.rectTransform, new Vector2(0.1f, 0.55f), new Vector2(0.9f, 0.75f));
        logo.text      = "SoccerFlaw AI";
        logo.fontSize  = 72;
        logo.fontStyle = FontStyles.Bold;
        logo.color     = accentColor;
        logo.alignment = TextAlignmentOptions.Center;

        var status = MakeTMP("ConnectingText", panel);
        SetAnchors(status.rectTransform, new Vector2(0.1f, 0.42f), new Vector2(0.9f, 0.55f));
        status.text      = "Connessione a Photon…";
        status.fontSize  = 32;
        status.color     = new Color(0.7f, 0.7f, 0.7f);
        status.alignment = TextAlignmentOptions.Center;
        ui.connectingText = status;

        return panel.gameObject;
    }

    // ─── PANEL: LOBBY ────────────────────────────────────────────────────────

    GameObject BuildPanelLobby(RectTransform root)
    {
        var panel = MakeFullPanel("PanelLobby", root, bgColor);

        // Titolo
        var title = MakeTMP("Title", panel);
        SetAnchors(title.rectTransform, new Vector2(0.05f, 0.88f), new Vector2(0.95f, 0.97f));
        title.text      = "SoccerFlaw AI";
        title.fontSize  = 56;
        title.fontStyle = FontStyles.Bold;
        title.color     = accentColor;
        title.alignment = TextAlignmentOptions.Center;

        // Card centrale
        var card = MakeRect("Card", panel);
        SetAnchors(card, new Vector2(0.06f, 0.30f), new Vector2(0.94f, 0.87f));
        AddImage(card, panelColor);

        // Nome room (i nomi giocatore sono assegnati automaticamente al join: Server/Player1/...)
        MakeLabel("RoomLabel", card, new Vector2(0.05f, 0.78f), new Vector2(0.95f, 0.90f), "Nome partita");
        ui.inputRoomName = MakeInputField("InputRoom", card,
            new Vector2(0.05f, 0.64f), new Vector2(0.95f, 0.77f), "Es: flo");
        ui.inputPlayerName = null; // non più usato

        // Bottoni
        ui.btnCreate = MakeButton("BtnCrea", card,
            new Vector2(0.05f, 0.28f), new Vector2(0.47f, 0.40f),
            "CREA ROOM", accentColor);

        ui.btnJoin = MakeButton("BtnUnisciti", card,
            new Vector2(0.53f, 0.28f), new Vector2(0.95f, 0.40f),
            "UNISCITI", new Color(0.20f, 0.55f, 0.30f));

        // Lista room
        MakeLabel("RoomListLabel", card,
            new Vector2(0.05f, 0.18f), new Vector2(0.95f, 0.26f), "Room disponibili:");

        var scrollArea = MakeRect("RoomScroll", card);
        SetAnchors(scrollArea, new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.17f));
        AddImage(scrollArea, new Color(0.10f, 0.10f, 0.14f));

        var scrollContent = MakeRect("RoomListContent", scrollArea);
        SetAnchors(scrollContent, Vector2.zero, Vector2.one);
        var vlg = scrollContent.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing            = 6;
        vlg.childControlHeight = true;
        vlg.childControlWidth  = true;
        vlg.padding            = new RectOffset(6, 6, 6, 6);
        ui.roomListContainer   = scrollContent;

        // Entry prefab per le room (non è un vero prefab — viene clonato da codice)
        ui.roomEntryPrefab = MakeRoomEntry();

        // Errore
        var err = MakeTMP("ErrorText", panel);
        SetAnchors(err.rectTransform, new Vector2(0.05f, 0.22f), new Vector2(0.95f, 0.29f));
        err.text      = "";
        err.fontSize  = 26;
        err.color     = dangerColor;
        err.alignment = TextAlignmentOptions.Center;
        ui.errorText  = err;

        return panel.gameObject;
    }

    // ─── PANEL: ROOM ─────────────────────────────────────────────────────────

    GameObject BuildPanelRoom(RectTransform root)
    {
        var panel = MakeFullPanel("PanelRoom", root, bgColor);

        // Nome room
        var roomName = MakeTMP("RoomNameText", panel);
        SetAnchors(roomName.rectTransform, new Vector2(0.05f, 0.88f), new Vector2(0.95f, 0.97f));
        roomName.text      = "Room: —";
        roomName.fontSize  = 44;
        roomName.color     = accentColor;
        roomName.alignment = TextAlignmentOptions.Center;
        ui.roomNameText    = roomName;

        // Lista giocatori
        var card = MakeRect("PlayerCard", panel);
        SetAnchors(card, new Vector2(0.06f, 0.42f), new Vector2(0.94f, 0.87f));
        AddImage(card, panelColor);

        MakeLabel("PlayersLabel", card,
            new Vector2(0.05f, 0.90f), new Vector2(0.95f, 0.99f), "Giocatori connessi:");

        var listArea = MakeRect("PlayerListArea", card);
        SetAnchors(listArea, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.89f));
        var vlg = listArea.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing            = 10;
        vlg.childControlHeight = true;
        vlg.childControlWidth  = true;
        vlg.padding            = new RectOffset(8, 8, 8, 8);
        ui.playerListContainer = listArea;

        ui.playerEntryPrefab = MakePlayerEntry();

        // Waiting text (per i telefoni)
        var waiting = MakeTMP("WaitingText", panel);
        SetAnchors(waiting.rectTransform, new Vector2(0.05f, 0.34f), new Vector2(0.95f, 0.41f));
        waiting.text      = "In attesa di giocatori…";
        waiting.fontSize  = 28;
        waiting.color     = new Color(0.6f, 0.6f, 0.6f);
        waiting.alignment = TextAlignmentOptions.Center;
        ui.waitingText    = waiting;

        // Toggle Portiere (interagibile solo dal MasterClient)
        ui.toggleGoalkeeper = MakeToggle("ToggleGoalkeeper", panel,
            new Vector2(0.05f, 0.26f), new Vector2(0.49f, 0.33f),
            "Portiere");

        // Toggle Mischia (asincrono, tutti i giocatori rispondono insieme)
        ui.toggleMischia = MakeToggle("ToggleMischia", panel,
            new Vector2(0.51f, 0.26f), new Vector2(0.95f, 0.33f),
            "Mischia");

        // Bottone START (solo MasterClient)
        ui.btnStart = MakeButton("BtnStart", panel,
            new Vector2(0.15f, 0.14f), new Vector2(0.85f, 0.24f),
            "INIZIA PARTITA", new Color(0.10f, 0.72f, 0.25f));

        // Bottone ESCI
        ui.btnLeave = MakeButton("BtnLeave", panel,
            new Vector2(0.30f, 0.04f), new Vector2(0.70f, 0.12f),
            "ESCI", dangerColor);

        return panel.gameObject;
    }

    // ─── FACTORY METHODS ─────────────────────────────────────────────────────

    RectTransform MakeFullPanel(string name, RectTransform parent, Color bg)
    {
        var rt = MakeRect(name, parent);
        SetAnchors(rt, Vector2.zero, Vector2.one);
        AddImage(rt, bg);
        return rt;
    }

    GameObject MakeRoomEntry()
    {
        var go  = new GameObject("RoomEntry");
        var img = go.AddComponent<Image>();
        img.color = new Color(0.20f, 0.20f, 0.28f);
        var btn = go.AddComponent<Button>();
        var lfe = go.AddComponent<LayoutElement>();
        lfe.minHeight = 60;

        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(go.transform, false);
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.fontSize  = 26;
        tmp.color     = textColor;
        tmp.alignment = TextAlignmentOptions.Center;
        var rt = labelGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(8, 4); rt.offsetMax = new Vector2(-8, -4);

        go.SetActive(false);
        return go;
    }

    GameObject MakePlayerEntry()
    {
        var go  = new GameObject("PlayerEntry");
        var img = go.AddComponent<Image>();
        img.color = new Color(0.18f, 0.18f, 0.24f);
        var lfe = go.AddComponent<LayoutElement>();
        lfe.minHeight = 70;

        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(go.transform, false);
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.fontSize  = 28;
        tmp.color     = textColor;
        tmp.alignment = TextAlignmentOptions.Center;
        var rt = labelGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(8, 4); rt.offsetMax = new Vector2(-8, -4);

        go.SetActive(false);
        return go;
    }

    TextMeshProUGUI MakeLabel(string name, RectTransform parent, Vector2 aMin, Vector2 aMax, string text)
    {
        var tmp = MakeTMP(name, parent);
        SetAnchors(tmp.rectTransform, aMin, aMax);
        tmp.text      = text;
        tmp.fontSize  = 26;
        tmp.color     = new Color(0.65f, 0.65f, 0.70f);
        tmp.alignment = TextAlignmentOptions.Left;
        return tmp;
    }

    TMP_InputField MakeInputField(string name, RectTransform parent, Vector2 aMin, Vector2 aMax, string placeholder)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        SetAnchors(rt, aMin, aMax);

        var bg  = go.AddComponent<Image>();
        bg.color = new Color(0.22f, 0.22f, 0.30f);

        var field = go.AddComponent<TMP_InputField>();

        // Testo
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(go.transform, false);
        var textTMP = textGO.AddComponent<TextMeshProUGUI>();
        textTMP.fontSize = 30;
        textTMP.color    = textColor;
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(12, 4); textRT.offsetMax = new Vector2(-12, -4);

        // Placeholder
        var phGO  = new GameObject("Placeholder");
        phGO.transform.SetParent(go.transform, false);
        var phTMP = phGO.AddComponent<TextMeshProUGUI>();
        phTMP.text     = placeholder;
        phTMP.fontSize = 30;
        phTMP.color    = new Color(0.45f, 0.45f, 0.50f);
        phTMP.fontStyle = FontStyles.Italic;
        var phRT = phGO.GetComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
        phRT.offsetMin = new Vector2(12, 4); phRT.offsetMax = new Vector2(-12, -4);

        field.textComponent  = textTMP;
        field.placeholder    = phTMP;
        field.targetGraphic  = bg;

        return field;
    }

    Toggle MakeToggle(string name, RectTransform parent, Vector2 aMin, Vector2 aMax, string label)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        SetAnchors(rt, aMin, aMax);

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.18f, 0.18f, 0.24f);

        var toggle = go.AddComponent<Toggle>();
        toggle.targetGraphic = bg;

        // Box checkbox a sinistra
        var boxGO = new GameObject("Box");
        boxGO.transform.SetParent(go.transform, false);
        var boxImg = boxGO.AddComponent<Image>();
        boxImg.color = new Color(0.30f, 0.30f, 0.36f);
        var boxRT = boxGO.GetComponent<RectTransform>();
        boxRT.anchorMin       = new Vector2(0f, 0.5f);
        boxRT.anchorMax       = new Vector2(0f, 0.5f);
        boxRT.pivot           = new Vector2(0f, 0.5f);
        boxRT.anchoredPosition = new Vector2(20f, 0f);
        boxRT.sizeDelta       = new Vector2(50f, 50f);

        // Checkmark dentro il box
        var checkGO = new GameObject("Checkmark");
        checkGO.transform.SetParent(boxGO.transform, false);
        var checkImg = checkGO.AddComponent<Image>();
        checkImg.color = accentColor;
        var checkRT = checkGO.GetComponent<RectTransform>();
        checkRT.anchorMin = new Vector2(0.15f, 0.15f);
        checkRT.anchorMax = new Vector2(0.85f, 0.85f);
        checkRT.offsetMin = Vector2.zero;
        checkRT.offsetMax = Vector2.zero;

        toggle.graphic = checkImg;

        // Label a destra del box
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(go.transform, false);
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = 28;
        tmp.color     = textColor;
        tmp.alignment = TextAlignmentOptions.Left;
        var lrt = labelGO.GetComponent<RectTransform>();
        lrt.anchorMin       = new Vector2(0f, 0f);
        lrt.anchorMax       = new Vector2(1f, 1f);
        lrt.offsetMin       = new Vector2(85f, 4f);
        lrt.offsetMax       = new Vector2(-10f, -4f);

        toggle.isOn = false;
        return toggle;
    }

    Button MakeButton(string name, RectTransform parent, Vector2 aMin, Vector2 aMax, string label, Color color)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        SetAnchors(rt, aMin, aMax);

        var img    = go.AddComponent<Image>();
        img.color  = color;
        var btn    = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor      = color;
        colors.highlightedColor = color * 1.2f;
        colors.pressedColor     = color * 0.8f;
        btn.colors = colors;

        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(go.transform, false);
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = 32;
        tmp.color     = Color.white;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        var lrt = labelGO.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;

        return btn;
    }

    // ─── UTILITY ─────────────────────────────────────────────────────────────

    static RectTransform MakeRect(string name, RectTransform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    static TextMeshProUGUI MakeTMP(string name, RectTransform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go.AddComponent<TextMeshProUGUI>();
    }

    static void SetAnchors(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = min; rt.anchorMax = max;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static void AddImage(RectTransform rt, Color color)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
    }

    static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
    }

#if UNITY_EDITOR
    // ─── BAKE EDITOR ─────────────────────────────────────────────────────────

    /// <summary>Costruisce la UI a edit-time per editing visuale. Wira MainMenuUI.</summary>
    public void BuildMenuInEditor()
    {
        ClearBakedUI();

        ui = GetComponent<MainMenuUI>();
        if (ui == null) ui = gameObject.AddComponent<MainMenuUI>();

        SetupCamera();
        BuildUI();

        EditorUtility.SetDirty(this);
        EditorUtility.SetDirty(ui);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        Debug.Log("[MainMenuBuilder] UI bakata in scena. Salva (Ctrl+S) e abbelliscila pure nell'editor.");
    }

    /// <summary>Rimuove la UI bakata (Canvas figlio).</summary>
    public void ClearBakedUI()
    {
        var canvas = GetComponentInChildren<Canvas>(true);
        if (canvas != null) DestroyImmediate(canvas.gameObject);
    }
#endif
}

#if UNITY_EDITOR
[CustomEditor(typeof(MainMenuBuilder))]
public class MainMenuBuilderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var b = (MainMenuBuilder)target;

        EditorGUILayout.Space(5);
        GUILayout.Label("Main Menu Builder", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Build Menu UI → bake la UI in scena per editing visuale.\n" +
            "Una volta bakata, il Play NON la ricostruisce: puoi abbellirla nell'editor.\n" +
            "Le liste room/giocatori restano dinamiche (popolate da Photon a runtime).",
            MessageType.Info);

        DrawDefaultInspector();
        EditorGUILayout.Space(8);

        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
        if (GUILayout.Button("Build Menu UI", GUILayout.Height(36)))
            b.BuildMenuInEditor();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(4);

        if (GUILayout.Button("Clear Baked UI"))
            b.ClearBakedUI();
    }
}
#endif
