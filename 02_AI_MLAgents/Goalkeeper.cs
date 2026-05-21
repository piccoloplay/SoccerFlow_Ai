using UnityEngine;

/// <summary>
/// SoccerFlaw_Ai — Goalkeeper
/// Capsula davanti alla porta che si muove a destra/sinistra in ping-pong.
/// Pilota un eventuale GoalkeeperSprite figlio (sprite 2D che cambia in base
/// allo stato: idle, movimento, parata).
///
/// La palla è cinematica nelle modalità Turni/Portiere: il "save" è un check
/// di prossimità chiamato da BallController. Con fisica reale (modalità Mischia
/// futura) il save scatta anche via OnCollisionEnter.
///
/// SETUP: spawnata da SceneSpawner / FieldFactory.BuildGoalkeeper().
/// Aggiungi un GameObject figlio "Sprite" con SpriteRenderer + GoalkeeperSprite
/// e assegna gli sprite nel suo inspector.
/// </summary>
public class Goalkeeper : MonoBehaviour
{
    [Header("─── Movimento ──────────────────────────────────")]
    [Tooltip("Se TRUE, il ping-pong è disabilitato: il portiere è mosso da un agent esterno (es. GoalieAgent ML). WouldSave e sprite update restano attivi.")]
    public bool externalMovementControl = false;

    [Tooltip("Velocità del movimento laterale ping-pong (unità/secondo)")]
    public float speed = 2.5f;

    [Tooltip("Distanza massima dalla posizione iniziale (in unità Unity)")]
    public float range = 1.8f;

    [Header("─── Parata ─────────────────────────────────────")]
    [Tooltip("Raggio di parata: se la palla entra in questa distanza viene parata (modalità kinematic)")]
    public float saveRadius = 0.55f;

    [Header("─── Sprite (figlio, opzionale) ─────────────────")]
    [Tooltip("Auto-rilevato come GetComponentInChildren se lasciato vuoto")]
    public GoalkeeperSprite sprite;

    [Tooltip("Soglia minima di movimento per cambiare stato sprite (evita flicker)")]
    public float moveEpsilon = 0.005f;

    // ──────────────────────────────────────────────────────

    Vector3 origin;
    float   lastX;

    void Start()
    {
        origin = transform.position;
        lastX  = transform.position.x;

        if (sprite == null)
            sprite = GetComponentInChildren<GoalkeeperSprite>(includeInactive: true);
    }

    void Update()
    {
        if (!externalMovementControl)
        {
            // Movimento ping-pong: [0, range*2] → sottraggo range per oscillare in [-range, +range]
            float offset = Mathf.PingPong(Time.time * speed, range * 2f) - range;
            transform.position = origin + Vector3.right * offset;
        }
        // Sprite update funziona indipendentemente da chi ha mosso la capsule
        // (legge solo dx tra transform.position e lastX). Compatibile con AI control.
        UpdateSpriteFromMotion();
    }

    void UpdateSpriteFromMotion()
    {
        if (sprite == null) return;

        // Mentre lo sprite è in Save, lascia che si auto-gestisca
        if (sprite.CurrentState == GoalkeeperSprite.State.Save)
        {
            lastX = transform.position.x;
            return;
        }

        float dx = transform.position.x - lastX;
        if      (dx >  moveEpsilon) sprite.SetState(GoalkeeperSprite.State.MoveRight);
        else if (dx < -moveEpsilon) sprite.SetState(GoalkeeperSprite.State.MoveLeft);
        else                        sprite.SetState(GoalkeeperSprite.State.Idle);

        lastX = transform.position.x;
    }

    /// <summary>
    /// LEGACY (deprecato): distance check usato dal vecchio BallController kinematic.
    /// Il nuovo BallController fisico rileva la parata via OnCollisionEnter automatica.
    /// Lasciato come fallback per eventuale codice esterno che ancora lo usa.
    /// </summary>
    public bool WouldSave(Vector3 ballPos, float ballRadius)
    {
        bool saved = Vector3.Distance(transform.position, ballPos) < saveRadius + ballRadius;
        if (saved && sprite != null) sprite.TriggerSave();
        return saved;
    }

    /// <summary>
    /// Collisione fisica con la palla (modalità Mischia futura con palle dinamiche).
    /// Innesca solo l'animazione del sprite — la deflessione è gestita dalla fisica Unity.
    /// </summary>
    void OnCollisionEnter(Collision col)
    {
        if (!col.gameObject.CompareTag("Ball")) return;
        if (sprite != null) sprite.TriggerSave();
    }
}
