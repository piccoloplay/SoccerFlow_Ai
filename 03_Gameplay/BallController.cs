using System.Collections;
using UnityEngine;
using Photon.Pun;

/// <summary>
/// SoccerFlaw_Ai — BallController (versione fisica)
/// La palla è un Rigidbody DINAMICO. Al tiro calcoliamo la velocità iniziale
/// per traiettoria parabolica (formula proiettile) e lasciamo che la gravità
/// faccia il suo lavoro. Le collisioni con portiere o pannelli scattano
/// naturalmente via OnCollisionEnter — niente più WouldSave manuale.
///
/// Funziona sia in Turni (1 palla) che in Mischia (N palle in parallelo via spawner).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class BallController : MonoBehaviour
{
    [Header("─── Riferimenti ───────────────────────────────")]
    public AnswerPanel[] answerPanels;

    [Header("─── Fisica ────────────────────────────────────")]
    [Tooltip("Tempo previsto di volo (sec). Determina la velocità iniziale.")]
    public float flightDuration = 0.7f;

    [Tooltip("Tempo massimo prima del reset di sicurezza (anti-loop)")]
    public float lifetime = 4f;

    [Tooltip("Se TRUE la palla va DRITTA senza arco (no gravità). Più facile per il portiere parare. Se FALSE usa gravità → arco parabolico (pallonetto).")]
    public bool flatTrajectory = true;

    [Header("─── Bouncy collision ──────────────────────────")]
    [Tooltip("Crea automaticamente un PhysicMaterial bouncy sulla palla. Serve in Mischia per ball-ball collision visibile (player 3 vince ironicamente perché 1 e 2 si scontrano).")]
    public bool useBouncyMaterial = true;
    [Range(0f, 1f)] public float bounciness = 0.8f;

    [Header("─── Reset ─────────────────────────────────────")]
    public float resetDelay = 0.6f;

    [Tooltip("Se TRUE la palla viene nascosta (SetActive false) immediatamente su collisione, poi riappare alla home dopo resetDelay. Più chiaro visivamente.")]
    public bool hideOnHit = true;

    // ─── Per Mischia (usati dal GameManager via Reflection o cast) ───
    [HideInInspector] public int playerIndex      = -1;  // chi ha tirato (0-3), -1 in Turni
    [HideInInspector] public int targetPanelIndex = -1;  // pannello target (-1 = vuoto)
    [HideInInspector] public int tapOrder         = 0;   // ordine di tap nel round per buzzer scoring

    // ────────────────────────────────────────────────────────

    Rigidbody rb;
    Vector3   homePosition;       // posizione di "riposo" della palla
    bool      inFlight;
    bool      resolved;
    Coroutine lifetimeRoutine;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        homePosition = transform.position;
        ConfigurePhysics();
    }

    void ConfigurePhysics()
    {
        rb.useGravity              = !flatTrajectory;   // se flatTrajectory niente gravità
        rb.collisionDetectionMode  = CollisionDetectionMode.Continuous;
        rb.interpolation           = RigidbodyInterpolation.Interpolate;
        rb.isKinematic             = true;   // riposo: kinematic, niente caduta dalla gravità
        rb.linearDamping           = 0f;
        rb.angularDamping          = 0.2f;

        // Bouncy material per il caos ball-ball in Mischia
        if (useBouncyMaterial)
        {
            var col = GetComponent<SphereCollider>();
            if (col != null && col.sharedMaterial == null)
            {
                var pm = new PhysicsMaterial("BallBouncy_RT")
                {
                    bounciness       = bounciness,
                    dynamicFriction  = 0.1f,
                    staticFriction   = 0.1f,
                    frictionCombine  = PhysicsMaterialCombine.Minimum,
                    bounceCombine    = PhysicsMaterialCombine.Maximum
                };
                col.sharedMaterial = pm;
            }
        }
    }

    /// <summary>Tiro classico (Turni): dalla posizione home verso il pannello.</summary>
    public void ShootToPanel(int panelIndex)
    {
        ShootFromTo(homePosition, panelIndex, playerIdx: -1, order: 0);
    }

    /// <summary>Tiro completo (Mischia o Turni custom): origine + target + tracking giocatore.</summary>
    public void ShootFromTo(Vector3 origin, int panelIndex, int playerIdx, int order)
    {
        if (inFlight) return;

        Vector3 target = ResolveTarget(panelIndex, origin);

        this.targetPanelIndex = panelIndex;
        this.playerIndex      = playerIdx;
        this.tapOrder         = order;

        resolved = false;
        inFlight = true;

        // Posiziona alla origine e attiva la fisica dinamica
        transform.position  = origin;
        transform.rotation  = Quaternion.identity;
        rb.isKinematic      = false;
        rb.linearVelocity   = Vector3.zero;
        rb.angularVelocity  = Vector3.zero;

        if (flatTrajectory)
        {
            // Tiro DRITTO: niente gravità, velocità costante origin→target
            rb.useGravity     = false;
            rb.linearVelocity = (target - origin) / flightDuration;
        }
        else
        {
            // Pallonetto parabolico con gravità Unity
            rb.useGravity     = true;
            rb.linearVelocity = ComputeProjectileVelocity(origin, target, flightDuration);
        }

        if (lifetimeRoutine != null) StopCoroutine(lifetimeRoutine);
        lifetimeRoutine = StartCoroutine(LifetimeWatch());
    }

    Vector3 ResolveTarget(int panelIndex, Vector3 origin)
    {
        if (panelIndex >= 0 && answerPanels != null
            && panelIndex < answerPanels.Length && answerPanels[panelIndex] != null)
            return answerPanels[panelIndex].transform.position;
        // Tiro nel vuoto: avanti e leggermente verso l'alto
        return origin + Vector3.forward * 6f + Vector3.up * 0.5f;
    }

    /// <summary>
    /// Formula del proiettile: dato origin, target e flightTime, calcola v0
    /// tale che la palla atterri sul target con la gravità di Unity.
    ///   vx = (tx - ox) / T
    ///   vz = (tz - oz) / T
    ///   vy = (ty - oy) / T + 0.5 * g * T   (compensa la caduta gravitazionale)
    /// </summary>
    public static Vector3 ComputeProjectileVelocity(Vector3 origin, Vector3 target, float flightTime)
    {
        float g = Mathf.Abs(Physics.gravity.y);
        Vector3 d = target - origin;
        return new Vector3(
            d.x / flightTime,
            d.y / flightTime + 0.5f * g * flightTime,
            d.z / flightTime);
    }

    IEnumerator LifetimeWatch()
    {
        yield return new WaitForSeconds(lifetime);
        if (!resolved)
        {
            resolved = true;
            if (PhotonNetwork.IsMasterClient)
                GameManager.Instance?.OnBallReachedPanel(targetPanelIndex, saved: false, playerIdx: playerIndex);
            ResetBall();
        }
    }

    void OnCollisionEnter(Collision col)
    {
        if (resolved) return;

        // PARATA: collisione col portiere
        var gk = col.gameObject.GetComponent<Goalkeeper>();
        if (gk != null)
        {
            resolved = true;
            if (PhotonNetwork.IsMasterClient)
                GameManager.Instance?.OnBallReachedPanel(targetPanelIndex, saved: true, playerIdx: playerIndex);
            StartCoroutine(HideAndReset());
            return;
        }

        // GOL: collisione col pannello risposta
        var panel = col.gameObject.GetComponent<AnswerPanel>();
        if (panel != null)
        {
            resolved = true;
            if (PhotonNetwork.IsMasterClient)
                GameManager.Instance?.OnBallReachedPanel(panel.panelIndex, saved: false, playerIdx: playerIndex);
            StartCoroutine(HideAndReset());
            return;
        }
        // Altre collisioni (terreno, mura) → ignora, lascia rimbalzare fino a timeout
    }

    /// <summary>
    /// "Sparisce" visivamente subito su collisione (Renderer+Collider off), poi
    /// riposiziona la palla alla home position e la rimostra dopo resetDelay.
    /// Più chiaro per l'utente: niente rimbalzi confusi dopo il contatto.
    /// Usa Renderer.enabled invece di SetActive(false) per non interrompere la coroutine.
    /// </summary>
    IEnumerator HideAndReset()
    {
        if (hideOnHit)
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic     = true;

            // Nascondi mesh + disattiva collider per niente collisioni residue
            var mr  = GetComponent<MeshRenderer>();
            var col = GetComponent<Collider>();
            if (mr  != null) mr.enabled  = false;
            if (col != null) col.enabled = false;
        }

        yield return new WaitForSeconds(resetDelay);

        ResetBall();

        if (hideOnHit)
        {
            var mr  = GetComponent<MeshRenderer>();
            var col = GetComponent<Collider>();
            if (mr  != null) mr.enabled  = true;
            if (col != null) col.enabled = true;
        }
    }

    void ResetBall()
    {
        inFlight = false;
        if (lifetimeRoutine != null) { StopCoroutine(lifetimeRoutine); lifetimeRoutine = null; }
        rb.linearVelocity  = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic     = true;            // torna in riposo: niente caduta
        transform.position = homePosition;
        transform.rotation = Quaternion.identity;
    }

    /// <summary>Cambia la "home position" (usato in Mischia per posizionare la palla ai piedi del giocatore N)</summary>
    public void SetHomePosition(Vector3 pos)
    {
        homePosition = pos;
        if (!inFlight) transform.position = pos;
    }

    public bool IsInFlight => inFlight;
}
