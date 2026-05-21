using System.Collections;
using UnityEngine;

/// <summary>
/// SoccerFlaw_Ai — CameraController
/// Gestisce le animazioni della camera nella scena Game:
///   - Aiming: centra il giocatore di turno, gli altri sullo sfondo
///   - InFlight: inquadra il pannello target mentre la palla vola
///   - Result/Overview: torna alla visione di campo completa
///
/// SETUP: aggiungi questo script al GameObject "GameManager"
/// (oppure a qualsiasi GameObject persistente nella scena Game).
/// </summary>
public class CameraController : MonoBehaviour
{
    public static CameraController Instance { get; private set; }

    [Header("─── Visuale Campo (Overview) ──────────────────")]
    [SerializeField] private Vector3 overviewPosition = new Vector3(0f, 6f, -8f);
    [SerializeField] private Vector3 overviewEuler    = new Vector3(28f, 0f, 0f);

    [Header("─── Visuale Aiming (giocatore di turno) ───────")]
    [Tooltip("Altezza camera durante la fase Aiming")]
    [SerializeField] private float aimHeight = 4.5f;
    [Tooltip("Profondità camera (z) durante la fase Aiming")]
    [SerializeField] private float aimZ      = -5.5f;
    [Tooltip("Quanto la camera segue orizzontalmente il giocatore (0=niente, 1=tutto)")]
    [SerializeField] private float aimPlayerInfluence = 0.5f;

    [Header("─── Visuale InFlight (pannello target) ────────")]
    [Tooltip("Z della camera per inquadrare il pannello")]
    [SerializeField] private float panelCamZ      = 0.5f;
    [Tooltip("Altezza camera per inquadrare il pannello")]
    [SerializeField] private float panelCamHeight = 3.5f;
    [Tooltip("Influenza orizzontale del pannello sulla posizione camera")]
    [SerializeField] private float panelCamInfluence = 0.35f;

    [Header("─── Velocità Transizione ───────────────────────")]
    [Tooltip("Velocità smoothstep delle transizioni")]
    [SerializeField] private float smoothSpeed = 2.5f;

    // ─── POSIZIONI GIOCATORI (specchio di SceneSpawner.ConfigurePlayers) ────
    static readonly Vector3[] PlayerPositions =
    {
        new Vector3(-2.5f, 0.9f, -2f),
        new Vector3(-0.8f, 0.9f, -2f),
        new Vector3( 0.8f, 0.9f, -2f),
        new Vector3( 2.5f, 0.9f, -2f),
    };

    // ─────────────────────────────────────────────────────────────────────────

    Transform cam;
    Vector3    targetPos;
    Quaternion targetRot;
    Coroutine  transitionRoutine;

    void Awake()
    {
        Instance = this;
        cam = Camera.main?.transform;
        if (cam != null)
        {
            targetPos = cam.position;
            targetRot = cam.rotation;
        }
    }

    void LateUpdate()
    {
        if (cam == null) return;
        cam.position = Vector3.Lerp(cam.position, targetPos, Time.deltaTime * smoothSpeed);
        cam.rotation = Quaternion.Slerp(cam.rotation, targetRot, Time.deltaTime * smoothSpeed);
    }

    // ─── API PUBBLICA ────────────────────────────────────────────────────────

    /// <summary>Visuale di campo completa (post-result, reset).</summary>
    public void SetOverview()
    {
        targetPos = overviewPosition;
        targetRot = Quaternion.Euler(overviewEuler);
    }

    /// <summary>
    /// Fase Aiming: centra la camera sul giocatore <paramref name="playerIndex"/> (0-3).
    /// Gli altri giocatori appaiono ai lati sullo sfondo.
    /// </summary>
    public void FocusPlayer(int playerIndex)
    {
        float px = (playerIndex >= 0 && playerIndex < PlayerPositions.Length)
            ? PlayerPositions[playerIndex].x
            : 0f;

        targetPos = new Vector3(px * aimPlayerInfluence, aimHeight, aimZ);

        // Guarda verso il giocatore attivo (leggermente sopra i piedi)
        Vector3 lookAt = new Vector3(px, 1.2f, -2f);
        targetRot = Quaternion.LookRotation(lookAt - targetPos);
    }

    /// <summary>
    /// Fase InFlight: inquadra il pannello target mentre la palla vola.
    /// </summary>
    /// <param name="panelWorldPos">Posizione world del pannello (da AnswerPanel.transform.position).</param>
    public void FocusPanel(Vector3 panelWorldPos)
    {
        float px = panelWorldPos.x;
        float py = panelWorldPos.y;

        targetPos = new Vector3(px * panelCamInfluence, panelCamHeight, panelCamZ);

        // Guarda verso il pannello
        Vector3 lookAt = new Vector3(px, py, panelWorldPos.z);
        targetRot = Quaternion.LookRotation(lookAt - targetPos);
    }

    /// <summary>Tiro nel vuoto (timeout): panoramica verso la porta.</summary>
    public void FocusMiss()
    {
        // Zoom in verso la porta vuota
        targetPos = new Vector3(0f, 3f, 2f);
        targetRot = Quaternion.Euler(10f, 0f, 0f);
    }
}
