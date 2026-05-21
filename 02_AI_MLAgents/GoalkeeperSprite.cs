using UnityEngine;

/// <summary>
/// SoccerFlaw_Ai — GoalkeeperSprite
/// Da mettere su un GameObject FIGLIO della capsule Goalkeeper.
/// Gestisce uno SpriteRenderer con 4 stati: Idle / MoveLeft / MoveRight / Save.
/// Goalkeeper.cs chiama SetState() in base al movimento e TriggerSave() su collisione.
///
/// SETUP:
/// 1. La FieldFactory crea già un GameObject "Sprite" figlio della capsule
///    con SpriteRenderer e questo script attaccati.
/// 2. Trascina i tuoi sprite (dal sprite sheet) nei 4 campi qui sotto.
/// 3. Tweak: scala del child, billboard, durata save.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class GoalkeeperSprite : MonoBehaviour
{
    public enum State { Idle, MoveLeft, MoveRight, Save }

    [Header("─── Sprites per stato ──────────────────────")]
    public Sprite idleSprite;
    public Sprite moveLeftSprite;
    public Sprite moveRightSprite;
    public Sprite saveSprite;

    [Header("─── Comportamento ──────────────────────────")]
    [Tooltip("Durata in secondi dello stato Save prima di tornare automaticamente a Idle/movimento")]
    public float saveStateDuration = 0.6f;

    [Tooltip("Cooldown tra due trigger Save consecutivi — evita flicker se più palle colpiscono in sequenza (Mischia)")]
    public float saveCooldown = 0.1f;

    [Tooltip("Se true, lo sprite ruota per guardare sempre la camera (billboard)")]
    public bool billboardToCamera = true;

    // ──────────────────────────────────────────────────────

    SpriteRenderer sr;
    State          currentState = State.Idle;
    float          saveTimer;
    Camera         cam;

    public State CurrentState => currentState;

    void Awake()
    {
        sr  = GetComponent<SpriteRenderer>();
        cam = Camera.main;
        SetState(State.Idle, force: true);
    }

    void LateUpdate()
    {
        // Billboard verso la camera
        if (billboardToCamera)
        {
            if (cam == null) cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }

        // Auto-uscita dallo state Save dopo saveStateDuration
        if (currentState == State.Save)
        {
            saveTimer -= Time.deltaTime;
            if (saveTimer <= 0f) SetState(State.Idle, force: true);
        }
    }

    /// <summary>
    /// Imposta lo state. In Save normale i tentativi di sovrascrivere sono ignorati
    /// finché il timer non scade (passa force=true per forzare).
    /// </summary>
    public void SetState(State state, bool force = false)
    {
        if (currentState == State.Save && state != State.Save && !force) return;
        if (currentState == state) return;

        currentState = state;

        switch (state)
        {
            case State.Idle:      SetSprite(idleSprite);      break;
            case State.MoveLeft:  SetSprite(moveLeftSprite);  break;
            case State.MoveRight: SetSprite(moveRightSprite); break;
            case State.Save:
                SetSprite(saveSprite);
                saveTimer = saveStateDuration;
                break;
        }
    }

    float lastSaveTriggerTime = -999f;

    /// <summary>Scorciatoia: forza lo state Save (chiamata da Goalkeeper su collisione).
    /// Ha un cooldown per evitare flicker se più palle colpiscono in successione.</summary>
    public void TriggerSave()
    {
        if (Time.time - lastSaveTriggerTime < saveCooldown) return;
        lastSaveTriggerTime = Time.time;
        SetState(State.Save, force: true);
    }

    void SetSprite(Sprite s)
    {
        if (sr == null) return;
        if (s == null) return; // se non assegnato, mantiene quello corrente (no flash vuoto)
        sr.sprite = s;
    }
}
