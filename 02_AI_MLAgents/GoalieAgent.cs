using System.Collections;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

/// <summary>
/// SoccerFlaw_Ai — GoalieAgent (versione "9 pannelli")
/// Portiere 3D (Capsule del prefab GameField) che si muove solo su X
/// a velocità costante per intercettare la palla diretta a uno dei 9
/// pannelli risposta della griglia AnswerGrid.
///
/// Action space: DISCRETO 1 branch, 3 azioni → 0=fermo, 1=sinistra, 2=destra.
/// Observations: 7 float normalizzati e ARENA-RELATIVE:
///   0: self.x        (relativa all'arena)
///   1: ball.x        (relativa all'arena)
///   2: ball.y        (la palla può puntare a panel a diverse altezze)
///   3: ball.z        (relativa all'arena)
///   4: ball.velocity.x
///   5: ball.velocity.y
///   6: ball.velocity.z
/// Reward:
///   +1 se collision palla↔portiere (parata)
///   -1 se la palla supera la goal line dei pannelli (gol)
///   -0.0005/step (incentivo a reagire veloce)
/// </summary>
public class GoalieAgent : Agent
{
    [Header("─── Riferimenti scena ─────────────────────────")]
    public Transform   ball;
    public Rigidbody   ballRb;
    public Rigidbody   goalieRb;
    [Tooltip("I 9 pannelli risposta — usati come target di spawn della palla")]
    public Transform[] answerPanels;

    [Header("─── Movimento portiere ────────────────────────")]
    public float moveSpeed      = 5f;
    [Tooltip("Limite X di movimento del portiere (relativo all'arena). Default 1.6 = arriva fino ai pali della porta più grande.")]
    public float fieldHalfWidth = 1.6f;

    [Header("─── Spawn palla (relativo arena center) ──────")]
    [Tooltip("Z di partenza della palla (davanti al portiere, lato 'tiratore')")]
    public float ballSpawnZ      = -1.5f;
    public float ballSpawnXRange = 2.5f;
    public float ballSpawnYMin   = 0.3f;
    public float ballSpawnYMax   = 1.5f;
    [Tooltip("Velocità della palla — costante; con AddRandomness una piccola variazione")]
    public float ballSpeed       = 8f;
    [Range(0f, 0.5f)]
    public float ballSpeedJitter = 0.15f;

    [Header("─── Goal detection ────────────────────────────")]
    [Tooltip("Z assoluta (world) oltre cui la palla è considerata gol — meglio leggermente PRIMA del piano dei pannelli")]
    public float goalZWorld = 5.4f;

    [Header("─── Reward ────────────────────────────────────")]
    public float saveReward  =  1f;
    public float goalPenalty = -1f;
    public float stepPenalty = -0.0005f;

    [Header("─── Modalità ──────────────────────────────────")]
    [Tooltip("TRUE = training (spawn palle, reward, episode ending). FALSE = produzione online (muove solo il portiere usando il modello). DEFAULT false per sicurezza.")]
    public bool isTrainingMode = false;

    [Header("─── Episode ───────────────────────────────────")]
    public int maxStepsPerEpisode = 500;

    [Header("─── Sequenza parata / gol ─────────────────────")]
    [Tooltip("Su parata: triggera lo sprite Save, nasconde la palla, attende, poi reset episodio")]
    public bool  playSaveAnimation     = true;
    public float saveAnimationDuration = 0.4f;
    [Tooltip("Su gol: piccola pausa per chiarezza visiva prima del reset (off di default per training rapido)")]
    public bool  pauseOnGoal          = false;
    public float goalPauseDuration    = 0.2f;

    [Header("─── Debug ─────────────────────────────────────")]
    [Tooltip("Logga le prime N azioni ricevute")]
    public int debugLogFirstActions = 0;

    Vector3 goalieStartPos;
    int     actionsReceived;
    int     currentTargetPanel = -1;
    bool    resolvingEpisode   = false;

    Vector3 ArenaCenter => transform.parent != null ? transform.parent.position : Vector3.zero;

    // ──────────────────────────────────────────────────────

    public override void Initialize()
    {
        goalieStartPos = transform.position;
        if (goalieRb == null) goalieRb = GetComponent<Rigidbody>();
        if (ballRb == null && ball != null) ballRb = ball.GetComponent<Rigidbody>();
        // In produzione: 0 = nessun limite (la partita la gestisce GameManager/Photon, non l'agent)
        MaxStep = isTrainingMode ? maxStepsPerEpisode : 0;

        // In TRAINING il BallController darebbe fastidio: nel suo Awake mette la palla
        // kinematic (→ la velocità che impostiamo verrebbe ignorata) e su collisione
        // la nasconde/resetta. Durante il training la palla la pilota l'agent, quindi
        // lo disabilitiamo. In produzione resta attivo (è lui a gestire i tiri reali).
        if (isTrainingMode && ball != null)
        {
            var bc = ball.GetComponent<BallController>();
            if (bc != null) bc.enabled = false;
        }
    }

    public override void OnEpisodeBegin()
    {
        // Reset portiere alla posizione iniziale (safe in entrambe le modalità)
        transform.position       = goalieStartPos;
        transform.rotation       = Quaternion.identity;
        if (goalieRb != null)
        {
            goalieRb.linearVelocity  = Vector3.zero;
            goalieRb.angularVelocity = Vector3.zero;
            goalieRb.position        = goalieStartPos;
        }

        // ── PRODUZIONE: stop qui. La palla la gestisce BallController via Photon. ──
        if (!isTrainingMode) return;

        // ── TRAINING: spawn palla random aimed at panel ──
        Vector3 ac = ArenaCenter;

        if (answerPanels == null || answerPanels.Length == 0)
        {
            Debug.LogError($"[GoalieAgent:{name}] answerPanels non assegnati — l'agente non può scegliere un target!");
            return;
        }
        currentTargetPanel = Random.Range(0, answerPanels.Length);
        Vector3 targetWorld = answerPanels[currentTargetPanel].position;

        float bx = ac.x + Random.Range(-ballSpawnXRange, ballSpawnXRange);
        float by = ac.y + Random.Range(ballSpawnYMin, ballSpawnYMax);
        float bz = ac.z + ballSpawnZ;

        // CRUCIALE: la palla deve essere dinamica, altrimenti linearVelocity è ignorata
        // e la palla resta ferma (→ ogni episodio scade a MaxStep, reward fisso -0.25).
        ballRb.isKinematic     = false;
        ballRb.useGravity      = false;   // tiro dritto verso il pannello
        ballRb.position        = new Vector3(bx, by, bz);
        ballRb.angularVelocity = Vector3.zero;
        ballRb.rotation        = Quaternion.identity;

        Vector3 dir = (targetWorld - ballRb.position).normalized;
        float speed = ballSpeed * (1f + Random.Range(-ballSpeedJitter, ballSpeedJitter));
        ballRb.linearVelocity = dir * speed;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 ac = ArenaCenter;
        // 7 float — tutti arena-relative e normalizzati
        sensor.AddObservation((goalieRb.position.x - ac.x) / fieldHalfWidth);
        sensor.AddObservation((ballRb.position.x   - ac.x) / fieldHalfWidth);
        sensor.AddObservation((ballRb.position.y   - ac.y) / 2f);
        sensor.AddObservation((ballRb.position.z   - ac.z) / Mathf.Abs(ballSpawnZ));
        sensor.AddObservation(ballRb.linearVelocity.x / ballSpeed);
        sensor.AddObservation(ballRb.linearVelocity.y / ballSpeed);
        sensor.AddObservation(ballRb.linearVelocity.z / ballSpeed);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (resolvingEpisode) return;

        int   act = actions.DiscreteActions[0];
        float dir = act == 1 ? -1f : act == 2 ? 1f : 0f;

        if (actionsReceived < debugLogFirstActions)
        {
            actionsReceived++;
            Debug.Log($"[GoalieAgent:{name}] action #{actionsReceived}: discrete={act} → dir={dir}, pos.x={transform.position.x:F2}");
        }

        Vector3 ac = ArenaCenter;

        // Movimento: SEMPRE attivo (è ciò che fa il portiere in qualsiasi modalità)
        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(
            pos.x + dir * moveSpeed * Time.fixedDeltaTime,
            ac.x - fieldHalfWidth,
            ac.x + fieldHalfWidth);
        transform.position = pos;
        if (goalieRb != null) goalieRb.position = pos;

        // ── PRODUZIONE: stop qui. Niente reward, niente goal check. ──
        // La logica di "gol" e "parata" è gestita da GameManager via Room Properties.
        if (!isTrainingMode) return;

        // ── TRAINING ──
        AddReward(stepPenalty);

        if (ballRb != null && ballRb.position.z > goalZWorld)
        {
            AddReward(goalPenalty);
            if (pauseOnGoal) StartCoroutine(GoalSequence());
            else             EndEpisode();
        }
    }

    void OnCollisionEnter(Collision col)
    {
        if (resolvingEpisode) return;
        if (!col.gameObject.CompareTag("Ball")) return;

        // ── PRODUZIONE: la palla è kinematic, questa funzione non dovrebbe scattare.
        // Se per qualche motivo scatta (es. palla fisica in modalità Mischia futura),
        // triggera solo l'animazione sprite — NIENTE EndEpisode né reset palla. ──
        if (!isTrainingMode)
        {
            var sprite = GetComponentInChildren<GoalkeeperSprite>();
            if (sprite != null) sprite.TriggerSave();
            return;
        }

        // ── TRAINING ──
        AddReward(saveReward);
        if (playSaveAnimation) StartCoroutine(SaveSequence());
        else                   EndEpisode();
    }

    IEnumerator SaveSequence()
    {
        resolvingEpisode = true;

        // 1. Triggera sprite parata (se presente come figlio del Goalkeeper)
        var sprite = GetComponentInChildren<GoalkeeperSprite>();
        if (sprite != null) sprite.TriggerSave();

        // 2. Disattiva la palla (sparisce, niente doppie collisioni)
        if (ball != null) ball.gameObject.SetActive(false);

        // 3. Attendi la durata dell'animazione
        yield return new WaitForSeconds(saveAnimationDuration);

        // 4. Riattiva la palla (OnEpisodeBegin la riposiziona)
        if (ball != null) ball.gameObject.SetActive(true);

        resolvingEpisode = false;
        EndEpisode();
    }

    IEnumerator GoalSequence()
    {
        resolvingEpisode = true;
        if (ball != null) ball.gameObject.SetActive(false);
        yield return new WaitForSeconds(goalPauseDuration);
        if (ball != null) ball.gameObject.SetActive(true);
        resolvingEpisode = false;
        EndEpisode();
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var disc = actionsOut.DiscreteActions;
        disc[0] = 0;

        // Compatibile sia con vecchio Input Manager che nuovo Input System.
        // Se entrambi i define sono attivi (Active Input Handling = Both) si attivano tutti e due i blocchi.
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            if (kb.leftArrowKey.isPressed  || kb.aKey.isPressed) disc[0] = 1;
            if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) disc[0] = 2;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKey(KeyCode.LeftArrow)  || Input.GetKey(KeyCode.A)) disc[0] = 1;
        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) disc[0] = 2;
#endif
    }

    void OnDrawGizmosSelected()
    {
        Vector3 ac = transform.parent != null ? transform.parent.position : Vector3.zero;

        // Linea di gol (piano dei pannelli)
        Gizmos.color = Color.red;
        Gizmos.DrawLine(new Vector3(ac.x - 2f, ac.y + 1.3f, goalZWorld),
                        new Vector3(ac.x + 2f, ac.y + 1.3f, goalZWorld));

        // Bounding box di movimento del portiere
        Gizmos.color = Color.yellow;
        Vector3 mvCenter = goalieStartPos == Vector3.zero ? transform.position : goalieStartPos;
        Gizmos.DrawLine(new Vector3(ac.x - fieldHalfWidth, mvCenter.y, mvCenter.z),
                        new Vector3(ac.x + fieldHalfWidth, mvCenter.y, mvCenter.z));

        // Pannello target attuale (verde)
        if (answerPanels != null && currentTargetPanel >= 0 && currentTargetPanel < answerPanels.Length
            && answerPanels[currentTargetPanel] != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(answerPanels[currentTargetPanel].position, 0.3f);
        }
    }
}
