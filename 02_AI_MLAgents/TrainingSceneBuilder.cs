using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// SoccerFlaw_Ai — TrainingSceneBuilder (versione "prefab GameField")
/// Istanzia il prefab GameField del gioco vero, disabilita ciò che non serve
/// in training (Players, Goalkeeper.cs pingpong, BallController.cs) e configura
/// il Goalkeeper come ML-Agents Agent.
///
/// NON tocca la Main Camera né la Directional Light: la camera la posizioni tu
/// manualmente come ti serve (visibile dietro il portiere, frontale, ecc).
///
/// SETUP:
///   1. Crea/apri _TrainGoalie.unity
///   2. (Manuale) Main Camera + Directional Light come preferisci
///   3. GameObject vuoto "TrainingSceneBuilder" + questo script
///   4. Trascina Assets/Prefabs/GameField.prefab nel campo gameFieldPrefab
///   5. Click BUILD TRAINING SCENE
///   6. Ctrl+S
///   7. mlagents-learn config/ppo/goalkeeper.yaml --run-id=goalkeeper_v5 --force
///   8. Play in Unity
/// </summary>
public class TrainingSceneBuilder : MonoBehaviour
{
    [Header("─── Prefab del gioco ──────────────────────────")]
    [Tooltip("Drag-drop di Assets/Prefabs/GameField.prefab — sarà istanziato N volte")]
    public GameObject gameFieldPrefab;

    [Header("─── Arene parallele ───────────────────────────")]
    [Range(1, 8)]
    public int arenaCount = 1;
    public float arenaSpacing = 14f;

    [Header("─── Cosa disabilitare nelle istanze ───────────")]
    [Tooltip("Disabilita il gruppo Players (le 4 capsule). In training non servono e bloccherebbero le traiettorie.")]
    public bool disablePlayers = true;
    [Tooltip("Disabilita Goalkeeper.cs (pingpong mover). L'Agent lo controllerà.")]
    public bool disableInGameGoalkeeperScript = true;
    [Tooltip("Disabilita BallController.cs (animazione kinematic). L'Agent userà fisica reale.")]
    public bool disableInGameBallController = true;

    [Header("─── Agent settings ────────────────────────────")]
    public float moveSpeed          = 4f;
    public int   maxStepsPerEpisode = 500;

    [Header("─── Decision Requester ────────────────────────")]
    public int decisionPeriod = 5;

    [Header("─── Camera (opzionale) ────────────────────────")]
    [Tooltip("Posiziona la Main Camera dietro lo spawn della palla guardando verso i pannelli. Disattiva se vuoi controllare la camera manualmente.")]
    public bool setupCamera = true;
    public float cameraFov = 55f;
    public Color skyColor = new Color(0.55f, 0.65f, 0.75f);

    // ──────────────────────────────────────────────────────

    public void Build()
    {
        if (gameFieldPrefab == null)
        {
            Debug.LogError("[TrainingSceneBuilder] gameFieldPrefab non assegnato! Trascinaci GameField.prefab dall'inspector.");
            return;
        }

        // Cleanup figli precedenti
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

#if UNITY_EDITOR
        EnsureBallTag();
#endif

        for (int i = 0; i < arenaCount; i++)
        {
            float offsetX = (i - (arenaCount - 1) * 0.5f) * arenaSpacing;
            BuildArena(offsetX, $"Arena_{i}");
        }

        if (setupCamera) SetupCamera();

        Debug.Log($"[TrainingSceneBuilder] Build OK: {arenaCount} arena/e instanziate dal prefab. " +
                  "Salva (Ctrl+S) e lancia mlagents-learn.");
    }

    void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;

        cam.orthographic = false;
        cam.fieldOfView  = cameraFov;

        // Posizione: dietro lo spawn della palla (z negativo), in alto, leggermente angolata in giù.
        // Scala con il numero di arene per inquadrarle tutte.
        float camY = 3.5f + arenaCount * 0.4f;
        float camZ = -7f - arenaCount * 1.0f;
        cam.transform.position = new Vector3(0f, camY, camZ);
        cam.transform.rotation = Quaternion.Euler(18f, 0f, 0f);

        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = skyColor;
    }

    void BuildArena(float offsetX, string arenaName)
    {
        // Istanzia il prefab GameField come figlio di questo GO
        GameObject arena;
#if UNITY_EDITOR
        arena = (GameObject)PrefabUtility.InstantiatePrefab(gameFieldPrefab, transform);
#else
        arena = Instantiate(gameFieldPrefab, transform);
#endif
        arena.name = arenaName;
        arena.transform.localPosition = new Vector3(offsetX, 0f, 0f);
        arena.transform.localRotation = Quaternion.identity;

        // ── Disabilita gruppi e script in-game ──
        if (disablePlayers)
        {
            var players = arena.transform.Find("Players");
            if (players != null) players.gameObject.SetActive(false);
        }

        Transform goalieT = arena.transform.Find("Goalkeeper");
        Transform ballT   = arena.transform.Find("Ball");
        Transform gridT   = arena.transform.Find("AnswerGrid");

        if (goalieT == null) { Debug.LogError("[TSB] 'Goalkeeper' non trovato nel prefab"); return; }
        if (ballT   == null) { Debug.LogError("[TSB] 'Ball' non trovato nel prefab");        return; }
        if (gridT   == null) { Debug.LogError("[TSB] 'AnswerGrid' non trovato nel prefab");  return; }

        // CRUCIALE: il prefab salva il Goalkeeper con SetActive(false)
        // (perché in game lo attiva FieldLoader on-demand). In training va attivo.
        goalieT.gameObject.SetActive(true);

        // Disable pingpong Goalkeeper.cs
        if (disableInGameGoalkeeperScript)
        {
            var inGameGK = goalieT.GetComponent<Goalkeeper>();
            if (inGameGK != null) inGameGK.enabled = false;
        }

        // Disable BallController
        if (disableInGameBallController)
        {
            var bc = ballT.GetComponent<BallController>();
            if (bc != null) bc.enabled = false;
        }

        // Riconfigura Goalkeeper come Agent
        ConfigureGoalkeeperAsAgent(goalieT, ballT, gridT);
    }

    void ConfigureGoalkeeperAsAgent(Transform goalieT, Transform ballT, Transform gridT)
    {
        GameObject go = goalieT.gameObject;

        // ── FASE 1: cleanup nell'ORDINE CORRETTO ──
        // Agent ha [RequireComponent(typeof(BehaviorParameters))], quindi prima va
        // distrutto Agent (e DecisionRequester), POI BehaviorParameters.
        var oldDR = go.GetComponent<DecisionRequester>();
        if (oldDR != null) DestroyImmediate(oldDR);

        var oldAgent = go.GetComponent<GoalieAgent>();
        if (oldAgent != null) DestroyImmediate(oldAgent);

        var oldBP = go.GetComponent<BehaviorParameters>();
        if (oldBP != null) DestroyImmediate(oldBP);

        // ── FASE 2: physics setup ──
        // Goalkeeper Kinematic (mosso via transform/MovePosition)
        var rb = go.GetComponent<Rigidbody>();
        if (rb == null) rb = go.AddComponent<Rigidbody>();
        rb.isKinematic   = true;
        rb.useGravity    = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // Ball Dynamic, no gravity, Continuous collision
        var ballRb = ballT.GetComponent<Rigidbody>();
        if (ballRb == null) ballRb = ballT.gameObject.AddComponent<Rigidbody>();
        ballRb.isKinematic            = false;
        ballRb.useGravity             = false;
        ballRb.linearDamping          = 0f;
        ballRb.angularDamping         = 0f;
        ballRb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        ballRb.interpolation          = RigidbodyInterpolation.Interpolate;

        // ── FASE 3: add components nell'ORDINE CORRETTO ──
        // Prima BehaviorParameters (dipendenza di Agent)
        var bp = go.AddComponent<BehaviorParameters>();
        bp.BehaviorName = "Goalkeeper";
        bp.BrainParameters.VectorObservationSize        = 7;
        bp.BrainParameters.NumStackedVectorObservations = 1;
        bp.BrainParameters.ActionSpec                   = ActionSpec.MakeDiscrete(3);
        bp.BehaviorType = BehaviorType.Default;

#if UNITY_EDITOR
        // Forza serializzazione del nome
        var soBP = new SerializedObject(bp);
        var nameProp = soBP.FindProperty("m_BehaviorName");
        if (nameProp != null)
        {
            nameProp.stringValue = "Goalkeeper";
            soBP.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(bp);
#endif

        // Poi GoalieAgent (BP esiste già, nessun RequireComponent auto-aggiunto)
        var agent = go.AddComponent<GoalieAgent>();
        agent.isTrainingMode     = true;  // ← CRUCIALE: abilita spawn palle + reward + EndEpisode
        agent.ball               = ballT;
        agent.ballRb             = ballRb;
        agent.goalieRb           = rb;
        agent.moveSpeed          = moveSpeed;
        agent.maxStepsPerEpisode = maxStepsPerEpisode;

        // Wire dei 9 pannelli (figli di AnswerGrid, ordinati per panelIndex se hanno AnswerPanel)
        var panels = new System.Collections.Generic.List<Transform>();
        var withIndex = new System.Collections.Generic.List<(int idx, Transform t)>();
        foreach (Transform child in gridT)
        {
            var ap = child.GetComponent<AnswerPanel>();
            if (ap != null)
                withIndex.Add((ap.panelIndex, child));
            else if (child.name.StartsWith("Panel"))
                panels.Add(child);
        }
        // Ordina per panelIndex e aggiungi
        withIndex.Sort((a, b) => a.idx.CompareTo(b.idx));
        foreach (var (_, t) in withIndex) panels.Add(t);
        agent.answerPanels = panels.ToArray();

        // Infine Decision Requester
        var dr = go.AddComponent<DecisionRequester>();
        dr.DecisionPeriod              = decisionPeriod;
        dr.TakeActionsBetweenDecisions = true;

#if UNITY_EDITOR
        EditorUtility.SetDirty(go);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }

    // ── Tag setup ────────────────────────────────────────

#if UNITY_EDITOR
    void EnsureBallTag()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return;

        var so       = new SerializedObject(assets[0]);
        var tagsProp = so.FindProperty("tags");

        for (int i = 0; i < tagsProp.arraySize; i++)
            if (tagsProp.GetArrayElementAtIndex(i).stringValue == "Ball") return;

        tagsProp.arraySize++;
        tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = "Ball";
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        Debug.Log("[TrainingSceneBuilder] Tag 'Ball' aggiunto al progetto.");
    }
#endif
}

#if UNITY_EDITOR
[CustomEditor(typeof(TrainingSceneBuilder))]
public class TrainingSceneBuilderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var b = (TrainingSceneBuilder)target;

        EditorGUILayout.Space(5);
        GUILayout.Label("Training Scene Builder — GameField prefab", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "1. Assegna gameFieldPrefab (Assets/Prefabs/GameField.prefab)\n" +
            "2. arenaCount: 1 per single test, 4-8 per training parallelo\n" +
            "3. Click BUILD — istanzia il prefab, disabilita Players + script in-game, configura Agent\n" +
            "4. Ctrl+S per salvare la scena\n" +
            "5. mlagents-learn config/ppo/goalkeeper.yaml --run-id=v5 --force\n\n" +
            "La Main Camera non viene toccata (impostala tu come preferisci).",
            MessageType.Info);

        DrawDefaultInspector();
        EditorGUILayout.Space(8);

        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
        if (GUILayout.Button("BUILD TRAINING SCENE", GUILayout.Height(40)))
            b.Build();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(4);

        if (GUILayout.Button("Clear All Children"))
        {
            for (int i = b.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(b.transform.GetChild(i).gameObject);
        }
    }
}
#endif
