using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// SoccerFlaw_Ai — FieldFactory
/// Costruisce in editor TUTTI gli elementi del campo di gioco
/// (pavimento, porta, 9 pannelli risposta, 4 player, palla, portiere)
/// come GameObject permanenti, poi li salva come singolo Prefab "GameField".
///
/// Vive SOLO nella scena _PrefabFactory.unity. Non viene mai caricato
/// dalla scena Game e non interferisce con la logica di gioco.
///
/// WORKFLOW:
///   1. Apri _PrefabFactory.unity
///   2. Seleziona il GameObject FieldFactory
///   3. Click "Build Field"  → costruisce alla scene root
///   4. Click "Save as GameField Prefab" → salva in Assets/Prefabs/GameField.prefab
///   5. Apri Game.unity, trascina il prefab nel campo gameFieldPrefab del FieldLoader
/// </summary>
public class FieldFactory : MonoBehaviour
{
    [Header("─── COLORI ────────────────────────────────────")]
    [SerializeField] private Color fieldColor       = new Color(0.18f, 0.48f, 0.18f);
    [SerializeField] private Color lineColor        = Color.white;
    [SerializeField] private Color goalColor        = Color.white;
    [SerializeField] private Color panelColor       = new Color(0.15f, 0.37f, 0.78f);
    [SerializeField] private Color player1Color     = Color.red;
    [SerializeField] private Color player2Color     = new Color(0.15f, 0.37f, 0.78f);
    [SerializeField] private Color player3Color     = new Color(0.18f, 0.60f, 0.25f);
    [SerializeField] private Color player4Color     = new Color(0.95f, 0.85f, 0.10f);
    [SerializeField] private Color goalkeeperColor  = new Color(0.95f, 0.85f, 0.10f);
    [SerializeField] private Color ballColor        = Color.white;

    [Header("─── OUTPUT ───────────────────────────────────")]
    [Tooltip("Path completo del prefab da salvare. La cartella viene creata se non esiste.")]
    [SerializeField] private string prefabSavePath  = "Assets/Prefabs/GameField.prefab";

    [Tooltip("Cartella dei materiali persistenti generati. Devono essere asset reali (non runtime) per non diventare magenta nel prefab.")]
    [SerializeField] private string materialsFolder = "Assets/Prefabs/Materials";

    [Tooltip("Goalkeeper GameObject viene creato ma SetActive(false) di default. FieldLoader lo abilita a runtime se Room Property gk=true.")]
    [SerializeField] private bool goalkeeperDisabledByDefault = true;

    [Header("─── Goalkeeper Sprite ─────────────────────────")]
    [Tooltip("Se TRUE, disabilita il MeshRenderer della capsule → vedi solo lo sprite (il CapsuleCollider resta attivo per la fisica)")]
    [SerializeField] private bool hideCapsuleMesh = true;

    [Tooltip("Offset locale dello sprite rispetto al centro della capsule. (0,0,0) = co-locato perfettamente.")]
    [SerializeField] private Vector3 spriteLocalPosition = new Vector3(0f, 0f, 0f);

    [Tooltip("Scala locale dello sprite. Compensa la scala della capsule (0.7, 1.3, 0.7) per avere sprite ~0.6×1.2 in world.")]
    [SerializeField] private Vector3 spriteLocalScale    = new Vector3(0.884f, 0.917f, 0.884f);

    // Cache materiali condivisi (azzerati ad ogni Build)
    Material matField, matLine, matGoal, matPanel;
    Material matP1, matP2, matP3, matP4;
    Material matGK, matBall, matNet;

    // ──────────────────────────────────────────────────────

    public void BuildField()
    {
        // Cleanup eventuale build precedente al root della scena
        var existing = FindRootGameField();
        if (existing != null) DestroyImmediate(existing);

        EnsureMaterials();

        var root = new GameObject("GameField");
        root.transform.position = Vector3.zero;
        root.transform.rotation = Quaternion.identity;

        BuildFieldGroup(root.transform);
        BuildGoalGroup(root.transform);
        BuildAnswerGrid(root.transform);
        BuildPlayers(root.transform);
        BuildBall(root.transform);
        BuildGoalkeeper(root.transform);

        Debug.Log("[FieldFactory] GameField costruito alla scene root. Pronto per Save.");
    }

    GameObject FindRootGameField()
    {
        // Cerca solo a scene root (parent == null) per evitare di toccare altri "GameField" nel progetto
        var roots = gameObject.scene.GetRootGameObjects();
        foreach (var go in roots)
            if (go.name == "GameField") return go;
        return null;
    }

    void EnsureMaterials()
    {
#if UNITY_EDITOR
        // Crea/aggiorna materiali come ASSET persistenti (Assets/Prefabs/Materials/Mat_*.mat).
        // Materiali runtime non sopravvivono al salvataggio prefab → diventano magenta.
        EnsureFolderExists(materialsFolder + "/dummy");

        matField = GetOrCreateMatAsset("Mat_Field",      fieldColor);
        matLine  = GetOrCreateMatAsset("Mat_Line",       lineColor);
        matGoal  = GetOrCreateMatAsset("Mat_Goal",       goalColor);
        matPanel = GetOrCreateMatAsset("Mat_Panel",      panelColor);
        matP1    = GetOrCreateMatAsset("Mat_Player1",    player1Color);
        matP2    = GetOrCreateMatAsset("Mat_Player2",    player2Color);
        matP3    = GetOrCreateMatAsset("Mat_Player3",    player3Color);
        matP4    = GetOrCreateMatAsset("Mat_Player4",    player4Color);
        matGK    = GetOrCreateMatAsset("Mat_Goalkeeper", goalkeeperColor);
        matBall  = GetOrCreateMatAsset("Mat_Ball",       ballColor);
        matNet   = GetOrCreateMatAsset("Mat_Net",        new Color(1f, 1f, 1f, 0.2f), transparent: true);

        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.AssetDatabase.Refresh();
#else
        // Runtime fallback (non dovrebbe mai capitare: FieldFactory è un tool editor-only)
        matField = MakeOpaqueMat(fieldColor);
        matLine  = MakeOpaqueMat(lineColor);
        matGoal  = MakeOpaqueMat(goalColor);
        matPanel = MakeOpaqueMat(panelColor);
        matP1    = MakeOpaqueMat(player1Color);
        matP2    = MakeOpaqueMat(player2Color);
        matP3    = MakeOpaqueMat(player3Color);
        matP4    = MakeOpaqueMat(player4Color);
        matGK    = MakeOpaqueMat(goalkeeperColor);
        matBall  = MakeOpaqueMat(ballColor);
        matNet   = MakeTransparentMat(new Color(1f, 1f, 1f, 0.2f));
#endif
    }

#if UNITY_EDITOR
    // Materiale template clonato dal RenderPipeline del progetto. Garantisce shader corretto
    // qualunque sia la pipeline attiva (URP / HDRP / Built-in).
    Material pipelineTemplate;

    Material GetPipelineTemplate()
    {
        if (pipelineTemplate != null) return pipelineTemplate;

        // 1. Default material della pipeline corrente (più affidabile)
        var pipeline = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline
                    ?? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        if (pipeline != null && pipeline.defaultMaterial != null)
        {
            pipelineTemplate = pipeline.defaultMaterial;
            Debug.Log($"[FieldFactory] Pipeline default material trovato: '{pipelineTemplate.name}' (shader: {pipelineTemplate.shader.name})");
            return pipelineTemplate;
        }

        // 2. Fallback: prova a trovare URP Lit / Simple Lit per nome
        string[] candidates = {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "URP/Lit",
            "URP/Simple Lit",
            "Standard"
        };
        foreach (var name in candidates)
        {
            var s = Shader.Find(name);
            if (s != null && s.name != "Hidden/InternalErrorShader")
            {
                pipelineTemplate = new Material(s);
                Debug.Log($"[FieldFactory] Shader trovato per nome: '{s.name}'");
                return pipelineTemplate;
            }
        }

        Debug.LogError("[FieldFactory] Nessun render pipeline o shader URP trovato. " +
                       "Verifica Edit → Project Settings → Graphics → 'Scriptable Render Pipeline Settings' (deve puntare a un URP Asset).");
        return null;
    }

    Material GetOrCreateMatAsset(string fileName, Color color, bool transparent = false)
    {
        string path = $"{materialsFolder}/{fileName}.mat";
        Material mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);

        if (mat == null)
        {
            // Crea NUOVO clonando il template della pipeline (shader sicuro)
            var template = GetPipelineTemplate();
            if (template == null)
            {
                Debug.LogError($"[FieldFactory] Impossibile creare {fileName}: nessun template valido.");
                return new Material(Shader.Find("Sprites/Default"));
            }

            mat = new Material(template);
            mat.name = fileName;
            ConfigureMatProperties(mat, color, transparent);
            UnityEditor.AssetDatabase.CreateAsset(mat, path);
            Debug.Log($"[FieldFactory] Creato '{fileName}' con shader '{mat.shader.name}'.");
        }
        else
        {
            // Aggiorna asset esistente con il colore corrente
            ConfigureMatProperties(mat, color, transparent);
            UnityEditor.EditorUtility.SetDirty(mat);
        }
        return mat;
    }

    static void ConfigureMatProperties(Material mat, Color color, bool transparent)
    {
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        mat.color = color;

        if (transparent)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend",   0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        else
        {
            mat.SetFloat("_Surface", 0f);
            mat.SetInt("_ZWrite", 1);
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = -1;
        }
    }
#endif

    // ── FIELD ────────────────────────────────────────────

    void BuildFieldGroup(Transform parent)
    {
        var fieldRoot = new GameObject("Field");
        fieldRoot.transform.SetParent(parent, false);

        var grass = MakeCube("Grass", matField, fieldRoot.transform);
        grass.transform.localPosition = new Vector3(0, -0.05f, 0);
        grass.transform.localScale    = new Vector3(8f, 0.1f, 12f);

        AddLine(fieldRoot.transform, "LineLeft",   new Vector3(-3.8f, 0.01f,    0), new Vector3(0.05f, 0.01f, 12f));
        AddLine(fieldRoot.transform, "LineRight",  new Vector3( 3.8f, 0.01f,    0), new Vector3(0.05f, 0.01f, 12f));
        AddLine(fieldRoot.transform, "LineMid",    new Vector3(    0, 0.01f,    0), new Vector3(8f,    0.01f, 0.05f));
        AddLine(fieldRoot.transform, "LineTop",    new Vector3(    0, 0.01f,  5.9f), new Vector3(8f,    0.01f, 0.05f));
        AddLine(fieldRoot.transform, "LineBottom", new Vector3(    0, 0.01f, -5.9f), new Vector3(8f,    0.01f, 0.05f));
    }

    void AddLine(Transform parent, string name, Vector3 pos, Vector3 scale)
    {
        var go = MakeCube(name, matLine, parent);
        go.transform.localPosition = pos;
        go.transform.localScale    = scale;
    }

    // ── GOAL ─────────────────────────────────────────────

    void BuildGoalGroup(Transform parent)
    {
        var goalRoot = new GameObject("Goal");
        goalRoot.transform.SetParent(parent, false);

        // Porta più larga + più alta (era 2.4x2, ora 3.2x2.5) per migliore visibilità
        AddGoalPart(goalRoot.transform, "PostLeft",  new Vector3(-1.6f, 1.25f, 5.8f), new Vector3(0.1f, 2.5f, 0.1f));
        AddGoalPart(goalRoot.transform, "PostRight", new Vector3( 1.6f, 1.25f, 5.8f), new Vector3(0.1f, 2.5f, 0.1f));
        AddGoalPart(goalRoot.transform, "Crossbar",  new Vector3(    0, 2.5f, 5.8f), new Vector3(3.3f, 0.1f, 0.1f));

        var net = GameObject.CreatePrimitive(PrimitiveType.Quad);
        net.name = "Net";
        net.transform.SetParent(goalRoot.transform, false);
        net.transform.localPosition = new Vector3(0, 1.25f, 5.88f);
        net.transform.localScale    = new Vector3(3.1f, 2.5f, 1f);
        var col = net.GetComponent<Collider>();
        if (col != null) DestroyImmediate(col);
        var mr = net.GetComponent<MeshRenderer>();
        if (mr != null) mr.sharedMaterial = matNet;
    }

    void AddGoalPart(Transform parent, string name, Vector3 pos, Vector3 scale)
    {
        var go = MakeCube(name, matGoal, parent);
        go.transform.localPosition = pos;
        go.transform.localScale    = scale;
    }

    // ── ANSWER GRID 3x3 ──────────────────────────────────

    void BuildAnswerGrid(Transform parent)
    {
        var gridRoot = new GameObject("AnswerGrid");
        gridRoot.transform.SetParent(parent, false);
        gridRoot.transform.localPosition = new Vector3(0, 1.25f, 5.5f);

        // Spacing aumentato (era 0.85/0.55) per pannelli più grandi e più separati
        const float sx = 1.05f, sy = 0.65f;

        for (int i = 0; i < 9; i++)
        {
            int col = i % 3;
            int row = i / 3;

            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = $"Panel_{i}";
            panel.transform.SetParent(gridRoot.transform, false);
            panel.transform.localPosition = new Vector3((col - 1) * sx, (1 - row) * sy, 0);
            // Pannello più grande (era 0.75x0.45) per migliore visibilità
            panel.transform.localScale    = new Vector3(0.9f, 0.55f, 0.1f);

            var mr = panel.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = matPanel;

            var ap = panel.AddComponent<AnswerPanel>();
            ap.panelIndex = i;
        }
    }

    // ── PLAYERS ──────────────────────────────────────────

    void BuildPlayers(Transform parent)
    {
        var playersRoot = new GameObject("Players");
        playersRoot.transform.SetParent(parent, false);

        AddPlayer(playersRoot.transform, "Player1", new Vector3(-2.5f, 0.9f, -2f), matP1);
        AddPlayer(playersRoot.transform, "Player2", new Vector3(-0.8f, 0.9f, -2f), matP2);
        AddPlayer(playersRoot.transform, "Player3", new Vector3( 0.8f, 0.9f, -2f), matP3);
        AddPlayer(playersRoot.transform, "Player4", new Vector3( 2.5f, 0.9f, -2f), matP4);
    }

    void AddPlayer(Transform parent, string name, Vector3 pos, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        TrySetTag(go, "Player");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale    = new Vector3(0.5f, 0.9f, 0.5f);

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sharedMaterial = mat;
    }

    // ── BALL ─────────────────────────────────────────────

    void BuildBall(Transform parent)
    {
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Ball";
        TrySetTag(ball, "Ball");
        ball.transform.SetParent(parent, false);
        ball.transform.localPosition = new Vector3(0, 0.2f, -1f);
        ball.transform.localScale    = new Vector3(0.3f, 0.3f, 0.3f);

        var mr = ball.GetComponent<MeshRenderer>();
        if (mr != null) mr.sharedMaterial = matBall;

        var rb = ball.AddComponent<Rigidbody>();
        rb.mass            = 1f;
        rb.linearDamping   = 0.5f;
        rb.angularDamping  = 0.5f;

        // BallController è un componente esistente del progetto
        ball.AddComponent<BallController>();
    }

    // ── GOALKEEPER ───────────────────────────────────────

    void BuildGoalkeeper(Transform parent)
    {
        var gk = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        gk.name = "Goalkeeper";
        gk.transform.SetParent(parent, false);
        // Posizione iniziale più in avanti (z=3.5) rispetto ai pannelli (z=5.5):
        // dà ~2 unità di "respiro" davanti alla porta, visivamente realistico
        // e fornisce all'agente AI più tempo di reazione.
        // Scala aumentata per coprire la porta più grande (top panel y≈2.2).
        gk.transform.localPosition = new Vector3(0f, 1.3f, 3.5f);
        gk.transform.localScale    = new Vector3(0.7f, 1.3f, 0.7f);

        var mr = gk.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.sharedMaterial = matGK;
            // Nascondi il mesh della capsule se richiesto (solo lo sprite resta visibile,
            // il CapsuleCollider continua a fare il suo lavoro per fisica/collisioni)
            mr.enabled = !hideCapsuleMesh;
        }

        var goalkeeperScript = gk.AddComponent<Goalkeeper>();

        // ── Child Sprite per visual ──
        var spriteGO = new GameObject("Sprite");
        spriteGO.transform.SetParent(gk.transform, false);
        // CO-LOCATO con la capsule (default 0,0,0) — billboard si occupa di girarlo verso camera.
        // Quando la capsule si muove, lo sprite la segue automaticamente (parent-child).
        spriteGO.transform.localPosition = spriteLocalPosition;
        spriteGO.transform.localScale    = spriteLocalScale;

        var sr = spriteGO.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 1;
        sr.color        = Color.white;
        sr.drawMode     = SpriteDrawMode.Simple;

#if UNITY_EDITOR
        // Material URP 2D "Sprite-Unlit-Default" → resa pulita, niente luci
        TryAssignUrpSpriteUnlit(sr);
#endif

        var sprite = spriteGO.AddComponent<GoalkeeperSprite>();
        sprite.saveStateDuration = 0.6f;
        sprite.billboardToCamera = true;

#if UNITY_EDITOR
        // Auto-assegnazione sprite del calciatore (cerca SpriteSheet_0..3 nel progetto)
        sprite.idleSprite      = FindSpriteByName("SpriteSheet_0");
        sprite.moveRightSprite = FindSpriteByName("SpriteSheet_1");
        sprite.moveLeftSprite  = FindSpriteByName("SpriteSheet_2");
        sprite.saveSprite      = FindSpriteByName("SpriteSheet_3");

        // Sprite iniziale visibile
        if (sprite.idleSprite != null) sr.sprite = sprite.idleSprite;
        else Debug.LogWarning("[FieldFactory] SpriteSheet_0..3 non trovati nel progetto. Assegnali manualmente nel prefab dopo Save.");
#endif

        goalkeeperScript.sprite = sprite;

        gk.SetActive(!goalkeeperDisabledByDefault);
    }

#if UNITY_EDITOR
    static void TryAssignUrpSpriteUnlit(SpriteRenderer sr)
    {
        // 1. Prova a caricare il material asset built-in di URP (path stabile)
        string[] matPaths = {
            "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat",
            "Packages/com.unity.render-pipelines.universal/Runtime/2D/Sprite-Unlit-Default.mat",
        };
        foreach (var path in matPaths)
        {
            var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) { sr.sharedMaterial = mat; return; }
        }

        // 2. Fallback: crea materiale runtime con lo shader URP 2D Unlit
        var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (shader != null)
        {
            var runtime = new Material(shader) { name = "Sprite-Unlit-Default (runtime)" };
            sr.sharedMaterial = runtime;
            return;
        }

        // 3. Se nemmeno lo shader URP è disponibile, lascia il default che Unity assegna a SpriteRenderer
        Debug.LogWarning("[FieldFactory] URP 2D Sprite-Unlit-Default non trovato. Material rimane default.");
    }

    static Sprite FindSpriteByName(string name)
    {
        // Cerca in TUTTO il progetto qualsiasi Sprite (incluso sub-sprite di uno sliced sheet)
        // con quel nome esatto.
        var guids = UnityEditor.AssetDatabase.FindAssets("t:Sprite");
        foreach (var guid in guids)
        {
            string path   = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var    assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (var asset in assets)
            {
                if (asset is Sprite s && s.name == name) return s;
            }
        }
        return null;
    }
#endif

    // ── UTILITY ──────────────────────────────────────────

    GameObject MakeCube(string name, Material mat, Transform parent)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sharedMaterial = mat;
        return go;
    }

    static Material MakeOpaqueMat(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null || shader.name == "Hidden/InternalErrorShader")
            shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null || shader.name == "Hidden/InternalErrorShader")
            return new Material(Shader.Find("Sprites/Default"));

        var mat = new Material(shader);
        mat.SetColor("_BaseColor", color);
        mat.color = color;
        return mat;
    }

    static Material MakeTransparentMat(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null || shader.name == "Hidden/InternalErrorShader")
            shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null || shader.name == "Hidden/InternalErrorShader")
            return new Material(Shader.Find("Sprites/Default"));

        var mat = new Material(shader);
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend",   0f);
        mat.SetInt("_SrcBlend",  (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend",  (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return mat;
    }

    static void TrySetTag(GameObject go, string tag)
    {
#if UNITY_EDITOR
        bool exists = false;
        foreach (var t in UnityEditorInternal.InternalEditorUtility.tags)
            if (t == tag) { exists = true; break; }
        if (!exists)
        {
            Debug.LogWarning($"[FieldFactory] Tag '{tag}' non esiste nel progetto. " +
                             $"Aggiungilo via Edit → Project Settings → Tags and Layers, poi ribuilda.");
            return;
        }
#endif
        go.tag = tag;
    }

#if UNITY_EDITOR
    public void SaveAsPrefab()
    {
        var fieldRoot = FindRootGameField();
        if (fieldRoot == null)
        {
            EditorUtility.DisplayDialog("Niente da salvare",
                "Click prima su 'Build Field' per generare la gerarchia.", "OK");
            return;
        }

        // Crea cartelle nidificate se mancanti (es. Assets/Prefabs)
        EnsureFolderExists(prefabSavePath);

        var prefab = PrefabUtility.SaveAsPrefabAsset(fieldRoot, prefabSavePath, out bool success);

        if (success && prefab != null)
        {
            Debug.Log($"[FieldFactory] Prefab salvato: {prefabSavePath}");
            EditorGUIUtility.PingObject(prefab);
            // Ricarica per pulizia: rimuove l'istanza in scena e ribuilda da prefab
            DestroyImmediate(fieldRoot);
            // Lascia la scena vuota — l'utente vedrà solo il prefab nel Project window
        }
        else
        {
            Debug.LogError($"[FieldFactory] Salvataggio fallito: {prefabSavePath}");
        }
    }

    public void ClearScene()
    {
        var fieldRoot = FindRootGameField();
        if (fieldRoot != null) DestroyImmediate(fieldRoot);
    }

    static void EnsureFolderExists(string assetPath)
    {
        var folder = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;

        var parts   = folder.Split('/');
        string current = parts[0]; // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
#endif
}

#if UNITY_EDITOR
[CustomEditor(typeof(FieldFactory))]
public class FieldFactoryEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var ff = (FieldFactory)target;

        EditorGUILayout.Space(5);
        GUILayout.Label("Field Factory — SoccerFlaw_Ai", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "1. Configura colori e path\n" +
            "2. Click 'Build Field' (crea GameField alla scene root)\n" +
            "3. Click 'Save as Prefab' (salva e pulisce la scena)\n" +
            "4. In Game.unity assegna il prefab al FieldLoader",
            MessageType.Info);

        DrawDefaultInspector();

        EditorGUILayout.Space(8);

        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
        if (GUILayout.Button("Build Field", GUILayout.Height(35)))
            ff.BuildField();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(4);

        GUI.backgroundColor = new Color(0.4f, 0.6f, 0.95f);
        if (GUILayout.Button("Save as GameField Prefab", GUILayout.Height(30)))
            ff.SaveAsPrefab();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(4);

        if (GUILayout.Button("Clear Scene (rimuove GameField)"))
            ff.ClearScene();
    }
}
#endif
