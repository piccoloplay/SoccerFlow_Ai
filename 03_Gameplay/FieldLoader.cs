using UnityEngine;
using Photon.Pun;

/// <summary>
/// SoccerFlaw_Ai — FieldLoader
/// Sostituto di SceneSpawner: invece di costruire il campo proceduralmente,
/// istanzia un prefab pre-costruito (GameField, generato da FieldFactory)
/// e fa direttamente il wiring di GameManager con i componenti del prefab.
///
/// SETUP nella scena Game:
/// 1. Aggiungi un GameObject vuoto "FieldLoader" e questo script
/// 2. Trascina GameField.prefab nel campo gameFieldPrefab
/// 3. Disabilita SceneSpawner e GameSceneAutoWire (uncheck) sui rispettivi GameObject
///    — FieldLoader gestisce sia lo spawn sia il wiring.
/// 4. Rimuovi i Player1-4, Ball, Panel(0..8) manuali dalla Hierarchy: vivono nel prefab.
///
/// IMPORTANTE: [DefaultExecutionOrder(-100)] garantisce che questo Awake()
/// giri PRIMA di GameSceneAutoWire / GameManager, così quando il wiring
/// degli altri script parte trovino già tutto pronto.
/// </summary>
[DefaultExecutionOrder(-100)]
public class FieldLoader : MonoBehaviour
{
    [Header("─── Prefab del campo ─────────────────────────")]
    [Tooltip("Prefab base (portiere ping-pong, niente ML-Agents). Usato sempre su WebGL.")]
    public GameObject gameFieldPrefab;
    [Tooltip("Prefab con portiere AI (ML-Agents/ONNX). Usato SOLO su desktop quando il portiere è richiesto. Su WebGL l'ONNX crasha, quindi viene ignorato.")]
    public GameObject gameFieldPrefabAI;

    [Header("─── Camera Setup ─────────────────────────────")]
    [Tooltip("Configura la Main Camera con la visuale overview del campo")]
    public bool   setupCamera     = true;
    public Vector3 cameraPosition = new Vector3(0f, 6f, -8f);
    public Vector3 cameraEuler    = new Vector3(28f, 0f, 0f);
    public Color   skyColor       = new Color(0.53f, 0.81f, 0.98f);

    [Header("─── Goalkeeper ───────────────────────────────")]
    [Tooltip("Forza l'attivazione del portiere ignorando la Room Property (test offline)")]
    public bool forceGoalkeeper = false;

    [Header("─── Wiring ──────────────────────────────────")]
    [Tooltip("Disabilita GameSceneAutoWire (se presente) per evitare doppio wiring conflittuale")]
    public bool disableAutoWire = true;

    GameObject spawnedField;

    void Awake()
    {
        spawnedField = SpawnField();
        if (setupCamera) SetupCamera();
        ConfigureGoalkeeper();
        WireGameManager();
    }

    GameObject SpawnField()
    {
        bool gkRequested = forceGoalkeeper || ReadPhotonGoalkeeperFlag();

        // Scelta del prefab:
        //  - WebGL: SEMPRE il base (ping-pong). L'ONNX di ML-Agents crasha in WebGL.
        //  - Desktop: se portiere richiesto e c'è il variant AI → usa quello, altrimenti base.
        GameObject prefab = gameFieldPrefab;
#if !UNITY_WEBGL
        if (gkRequested && gameFieldPrefabAI != null)
            prefab = gameFieldPrefabAI;
#endif

        if (prefab == null)
        {
            Debug.LogError("[FieldLoader] Nessun prefab assegnato! Trascina GameField.prefab nell'Inspector.");
            return null;
        }

        var instance = Instantiate(prefab, Vector3.zero, Quaternion.identity);
        instance.name = "GameField"; // rimuove "(Clone)"
        instance.SetActive(true);

        Debug.Log($"[FieldLoader] Spawn '{prefab.name}' (gkRequested={gkRequested}, " +
#if UNITY_WEBGL
                  "platform=WebGL → ping-pong forzato)");
#else
                  "platform=Desktop)");
#endif
        return instance;
    }

    void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        cam.transform.position = cameraPosition;
        cam.transform.rotation = Quaternion.Euler(cameraEuler);
        cam.backgroundColor    = skyColor;
        cam.clearFlags         = CameraClearFlags.SolidColor;
    }

    void ConfigureGoalkeeper()
    {
        if (spawnedField == null) return;
        bool enable = forceGoalkeeper || ReadPhotonGoalkeeperFlag();
        // Cerca il Goalkeeper SOLO dentro il prefab spawnato (anche se inattivo)
        var gk = spawnedField.GetComponentInChildren<Goalkeeper>(true);
        if (gk != null) gk.gameObject.SetActive(enable);
    }

    bool ReadPhotonGoalkeeperFlag()
    {
        if (!PhotonNetwork.InRoom) return false;
        var props = PhotonNetwork.CurrentRoom.CustomProperties;
        return props.TryGetValue(GameManager.K_Goalkeeper, out object v) && v is bool b && b;
    }

    void WireGameManager()
    {
        if (spawnedField == null) return;
        var gm = FindFirstObjectByType<GameManager>();
        if (gm == null)
        {
            Debug.LogWarning("[FieldLoader] GameManager non trovato nella scena — wiring saltato.");
            return;
        }

        // Disabilita GameSceneAutoWire per evitare conflitti (cerca via tag, può prendere il manual Ball sbagliato)
        if (disableAutoWire)
        {
            var autoWire = gm.GetComponent<GameSceneAutoWire>();
            if (autoWire != null) autoWire.enabled = false;
        }

        // Cerca i componenti dentro il prefab spawnato (anche inactive, per il portiere)
        var ball   = spawnedField.GetComponentInChildren<BallController>(true);
        var panels = spawnedField.GetComponentsInChildren<AnswerPanel>(true);

        // Ordina i panel per panelIndex (0..8) — fondamentale per il match con le risposte
        System.Array.Sort(panels, (a, b) => a.panelIndex.CompareTo(b.panelIndex));

        // Assegna a GameManager
        gm.answerPanels   = panels;
        gm.ballController = ball;

        // Cross-wire: BallController ha bisogno della lista pannelli per shootare
        if (ball != null) ball.answerPanels = panels;

        // QuestionManager: solo banco offline. Se manca in scena, lo crea al volo.
        if (gm.questionManagerObject == null)
        {
            var qmLocal = FindFirstObjectByType<QuestionManager_Local>();
            if (qmLocal == null)
            {
                var qmGO = new GameObject("QuestionManager_Local");
                qmLocal = qmGO.AddComponent<QuestionManager_Local>();
                Debug.Log("[FieldLoader] QuestionManager_Local creato automaticamente (banco offline).");
            }
            gm.questionManagerObject = qmLocal;
        }

        // CameraController sul GameManager (come faceva GameSceneAutoWire)
        if (gm.GetComponent<CameraController>() == null)
            gm.gameObject.AddComponent<CameraController>();

        // Sicurezza extra: forza Ball attivo
        if (ball != null && !ball.gameObject.activeSelf)
            ball.gameObject.SetActive(true);

        Debug.Log($"[FieldLoader] Wired GameManager — panels: {panels.Length}, " +
                  $"ball: {ball != null} (active: {ball?.gameObject.activeInHierarchy}), " +
                  $"qm: {gm.questionManagerObject != null}");
    }
}
