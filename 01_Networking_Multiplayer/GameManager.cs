using System.Collections;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// SoccerFlaw_Ai — GameManager
/// State machine principale del gioco. Gira su TUTTI i client della scena Game
/// (solo il MasterClient guida la logica; gli altri leggono le Room Properties).
///
/// SETUP nell'editor:
/// 1. Crea un GameObject "GameManager" nella scena Game.
/// 2. Aggiungi questo script E un componente PhotonView allo stesso GameObject.
/// 3. Trascina i 9 AnswerPanel[] (gli stessi cube di SceneSpawner).
/// 4. Trascina BallController.
///
/// COMUNICAZIONE CROSS-SCENE (Game ↔ Controller):
/// Tutto lo stato è sincronizzato via Photon Room Custom Properties.
/// I client della scena Controller leggono le stesse Properties tramite ControllerManager.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class GameManager : MonoBehaviourPunCallbacks
{
    public static GameManager Instance { get; private set; }

    public enum TurnPhase { Waiting, Aiming, InFlight, Result, End }
    public TurnPhase Phase { get; private set; } = TurnPhase.Waiting;

    public enum GameMode { Turns, Free4All }
    public GameMode CurrentMode { get; private set; } = GameMode.Turns;

    [Header("─── Impostazioni Partita ─────────────────────")]
    [SerializeField] private int totalQuestions = 10;
    [SerializeField] private int timerSeconds   = 15;
    [Tooltip("Timer per modalità Mischia (più breve perché tutti rispondono insieme)")]
    [SerializeField] private int mischiaTimerSeconds = 8;

    [Header("─── Mischia: spawn palle per giocatore ──────")]
    [Tooltip("Posizione locale di spawn della palla per ogni player (X laterale, Z leggermente avanti ai giocatori che stanno a z=-2)")]
    [SerializeField] private Vector3[] playerBallSpawnPositions = new Vector3[]
    {
        new Vector3(-2.5f, 0.5f, -0.8f),
        new Vector3(-0.8f, 0.5f, -0.8f),
        new Vector3( 0.8f, 0.5f, -0.8f),
        new Vector3( 2.5f, 0.5f, -0.8f),
    };

    [Tooltip("Moltiplicatore di scala delle palle Mischia (1 = uguale al Turni, 2 = doppio). Tutte e 4 le palle (originale + 3 cloni) vengono scalate.")]
    [SerializeField] private float mischiaBallScale = 2f;

    [Tooltip("Scaling palla in Turni quando il portiere è attivo (1.25 = leggermente più grande per collisione migliore). Non applicato senza portiere né in Mischia. Tenuto basso per non sovrapporre pannelli adiacenti.")]
    [SerializeField] private float goalkeeperBallScale = 1.25f;

    [Header("─── Riferimenti (Inspector) ─────────────────")]
    public BallController  ballController;
    public AnswerPanel[]   answerPanels;     // 9 pannelli
    /// <summary>
    /// Trascina qui il GameObject con QuestionManager_Local (test)
    /// oppure QuestionManager (Cheshire Cat). Deve implementare IQuestionManager.
    /// </summary>
    public MonoBehaviour   questionManagerObject;

    // ─── STATO INTERNO ──────────────────────────────────────────────────────
    int       currentTurn   = 0;    // indice 0-3
    int       questionIndex = 0;
    int[]     scores        = new int[4];
    int       correctPanel  = -1;
    int[]     playerActors  = new int[4];   // ActorNumber Photon dei 4 controller
    Coroutine timerRoutine;

    // ─── MISCHIA: pool di palle e tracking shots ────────────────────────────
    BallController[] mischiaBalls       = new BallController[4];
    Vector3[]        runtimeBallSpawns  = new Vector3[4];   // calcolate dalle capsule (robuste vs serializzato)

    // Shot info per giocatore (Mischia)
    struct MischiaShot
    {
        public int  panelIndex;        // pannello scelto (-1 = non tappato)
        public int  tapOrder;          // ordine di tap (1..N)
        public bool resolved;          // ball ha colpito qualcosa o timeout
        public bool wasCorrect;        // panelIndex == correctPanel
        public bool wasSaved;          // intercettato dal portiere
    }
    MischiaShot[] mischiaShots = new MischiaShot[4];
    int           mischiaTapCounter = 0;   // incrementato ad ogni tap nuovo

    // ─── CHIAVI ROOM PROPERTIES ─────────────────────────────────────────────
    // Costanti condivise con ControllerManager (stesso nome)
    public const string K_Phase       = "phase";
    public const string K_Question    = "q";
    public const string K_Answers     = "ans";
    public const string K_CorrectIdx  = "ci";
    public const string K_CurrentTurn = "turn";
    public const string K_Timer       = "tmr";
    public const string K_Scores      = "sc";
    public const string K_SelPanel    = "sp";
    public const string K_SelActor    = "sa";
    public const string K_Goalkeeper  = "gk";
    public const string K_Saved       = "sv";   // bool: true se il portiere ha parato (annulla il punto anche se giusto)
    public const string K_GameMode    = "mode"; // "turns" | "free4all"
    public const string K_Sel0        = "s0";   // Mischia: pannello scelto dal player 0 (-1 = non tappato)
    public const string K_Sel1        = "s1";   // Mischia: player 1
    public const string K_Sel2        = "s2";   // Mischia: player 2
    public const string K_Sel3        = "s3";   // Mischia: player 3

    // ─────────────────────────────────────────────────────────────────────────

    IQuestionManager QuestionMgr =>
        (questionManagerObject as IQuestionManager)
        ?? FindFirstObjectByType<QuestionManager_Local>();

    void Awake() { Instance = this; }

    void Start()
    {
        // Leggi la modalità dalla room (tutti i client, non solo master)
        if (PhotonNetwork.InRoom)
        {
            var props = PhotonNetwork.CurrentRoom.CustomProperties;
            if (props.TryGetValue(K_GameMode, out object m) && m is string s && s == "free4all")
                CurrentMode = GameMode.Free4All;
        }

        if (!PhotonNetwork.IsMasterClient) return;
        BuildPlayerList();
        HidePlayerVisuals();

        if (CurrentMode == GameMode.Free4All)
            SetupMischiaBalls();
        else
            SetupTurniBall();

        StartCoroutine(StartWhenReady());
    }

    /// <summary>Nasconde capsule + label punteggio dei giocatori non connessi (slot vuoti).</summary>
    void HidePlayerVisuals()
    {
        var playersGroup = GameObject.Find("Players");
        for (int i = 0; i < 4; i++)
        {
            bool active = playerActors[i] != 0;

            if (playersGroup != null)
            {
                var capsule = playersGroup.transform.Find($"Player{i + 1}");
                if (capsule != null) capsule.gameObject.SetActive(active);
            }

            HUDManager.Instance?.SetPlayerActive(i + 1, active);
        }
    }

    /// <summary>Turni: se il portiere è attivo, scala leggermente la palla per collisione migliore.</summary>
    void SetupTurniBall()
    {
        bool gkActive = false;
        if (PhotonNetwork.InRoom)
        {
            var props = PhotonNetwork.CurrentRoom.CustomProperties;
            gkActive = props.TryGetValue(K_Goalkeeper, out object v) && v is bool b && b;
        }

        if (gkActive && ballController != null
            && goalkeeperBallScale > 0f && Mathf.Abs(goalkeeperBallScale - 1f) > 0.01f)
        {
            ballController.transform.localScale *= goalkeeperBallScale;
            Debug.Log($"[GameManager] Turni + Portiere: palla scalata x{goalkeeperBallScale}");
        }
    }

    void BuildPlayerList()
    {
        // MasterClient = PC (non conta come giocatore attivo)
        // I telefoni sono i giocatori 0-3 nell'ordine di join
        int idx = 0;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (p.IsMasterClient) continue;
            if (idx >= 4) break;
            playerActors[idx++] = p.ActorNumber;
        }
        // Se mancano giocatori (test con meno di 4), riempie con 0
        for (int i = idx; i < 4; i++) playerActors[i] = 0;
    }

    IEnumerator StartWhenReady()
    {
        yield return new WaitUntil(() => QuestionMgr != null);
        yield return new WaitForSeconds(0.5f);
        StartNextTurn();
    }

    // ─── STATE MACHINE ───────────────────────────────────────────────────────

    void StartNextTurn()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (questionIndex >= totalQuestions) { DoEndGame(); return; }

        // Round-robin (solo in Turni): seleziona il giocatore di turno
        int activePlayers = 0;
        foreach (var a in playerActors) if (a != 0) activePlayers++;
        currentTurn = activePlayers > 0 ? questionIndex % activePlayers : 0;

        // Reset stato Mischia (anche in Turni non fa danno)
        ResetMischiaShotsTracker();

        Phase = TurnPhase.Aiming;
        StartCoroutine(FetchAndBroadcast());
    }

    IEnumerator FetchAndBroadcast()
    {
        yield return StartCoroutine(
            QuestionMgr.FetchQuestion((question, answers, correctIdx) =>
            {
                correctPanel = correctIdx;

                var props = new Hashtable
                {
                    { K_Phase,       "Aiming"                        },
                    { K_Question,    question                         },
                    { K_Answers,     JsonHelper.ToJson(answers)       },
                    { K_CorrectIdx,  correctIdx                       },
                    { K_CurrentTurn, currentTurn                      },
                    { K_Timer,       CurrentMode == GameMode.Free4All ? mischiaTimerSeconds : timerSeconds },
                    { K_Scores,      JsonHelper.ToJson(scores)        },
                    { K_SelPanel,    -1                               },
                    { K_SelActor,    -1                               },
                    { K_Saved,       false                            },
                    // Mischia: reset selezioni per-player
                    { K_Sel0, -1 }, { K_Sel1, -1 }, { K_Sel2, -1 }, { K_Sel3, -1 }
                };
                PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            })
        );
    }

    // ─── RICEZIONE SELEZIONE PANNELLO (dal Controller via Room Properties) ──

    void HandlePanelSelected(int panelIndex, int actorNumber)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (Phase != TurnPhase.Aiming) return;

        // Verifica che sia il giocatore di turno
        bool validPlayer = (playerActors[currentTurn] == actorNumber)
                           || (actorNumber == 0); // fallback test senza telefoni

        if (!validPlayer) return;

        StopTimer();
        Phase = TurnPhase.InFlight;
        var props = new Hashtable
        {
            { K_Phase,    "InFlight" },
            { K_SelPanel, panelIndex }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    void HandleTimerExpired()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (Phase != TurnPhase.Aiming) return;

        StopTimer();

        if (CurrentMode == GameMode.Free4All)
        {
            // Mischia: aspetta le palle in volo + buffer, poi risolvi
            StartCoroutine(MischiaTimerExpiredFlow());
            return;
        }

        // Turni: timeout = palla nel vuoto
        Phase = TurnPhase.InFlight;
        var props = new Hashtable
        {
            { K_Phase,    "InFlight" },
            { K_SelPanel, -1 }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    /// <summary>
    /// Chiamato da AnswerPanel.OnCollisionEnter OPPURE da BallController
    /// alla fine dell'animazione (per tiri nel vuoto o parati).
    ///
    /// panelIndex: il pannello scelto dal giocatore (o -1 se timeout/tiro nel vuoto).
    /// saved: true se il portiere ha intercettato la palla prima del pannello.
    ///   Quando saved=true il punto è annullato anche se la risposta era corretta.
    /// </summary>
    public void OnBallReachedPanel(int panelIndex, bool saved = false, int playerIdx = -1)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // ── MISCHIA: route diretto al player se identificato dalla palla ──
        if (CurrentMode == GameMode.Free4All)
        {
            if (playerIdx >= 0 && playerIdx < 4)
            {
                HandleMischiaBallResolved(playerIdx, panelIndex, saved);
                return;
            }
            // Fallback (palla senza playerIndex): match per panelIndex non risolto
            for (int i = 0; i < 4; i++)
            {
                if (mischiaShots[i].panelIndex == panelIndex && !mischiaShots[i].resolved)
                {
                    HandleMischiaBallResolved(i, panelIndex, saved);
                    return;
                }
            }
            Debug.LogWarning($"[GameManager] Mischia: OnBallReachedPanel({panelIndex}, saved={saved}, playerIdx={playerIdx}) non matchato");
            return;
        }

        // ── TURNI: comportamento classico ──
        if (Phase != TurnPhase.InFlight) return;

        bool answeredCorrectly = panelIndex == correctPanel && panelIndex >= 0;
        bool scored = answeredCorrectly && !saved;
        if (scored) scores[currentTurn]++;

        Debug.Log($"[GameManager] Turn {currentTurn} result: panel={panelIndex}, correctPanel={correctPanel}, saved={saved}, answeredCorrectly={answeredCorrectly}, SCORED={scored}, scores=[{string.Join(",", scores)}]");

        Phase = TurnPhase.Result;
        var props = new Hashtable
        {
            { K_Phase,    "Result"                   },
            { K_Scores,   JsonHelper.ToJson(scores)  },
            { K_SelPanel, panelIndex                 },
            { K_Saved,    saved                      }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);

        StartCoroutine(ResultDelay());
    }

    IEnumerator ResultDelay()
    {
        yield return new WaitForSeconds(2.5f);
        questionIndex++;
        StartNextTurn();
    }

    [Tooltip("Secondi sulla schermata di fine partita prima del ritorno automatico al MainMenu")]
    [SerializeField] private int endGameReturnSeconds = 15;

    void DoEndGame()
    {
        Phase = TurnPhase.End;
        var props = new Hashtable
        {
            { K_Phase,  "End"                        },
            { K_Scores, JsonHelper.ToJson(scores)    }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    // ─── TIMER ───────────────────────────────────────────────────────────────

    void StartTimer()
    {
        StopTimer();
        timerRoutine = StartCoroutine(TimerCountdown());
    }

    void StopTimer()
    {
        if (timerRoutine != null) { StopCoroutine(timerRoutine); timerRoutine = null; }
    }

    // Usato solo quando BallController non è ancora wired nell'Inspector
    IEnumerator FallbackResolve(int panelIndex)
    {
        yield return new WaitForSeconds(1.5f);
        OnBallReachedPanel(panelIndex);
    }

    IEnumerator TimerCountdown()
    {
        for (int i = timerSeconds; i >= 0; i--)
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { K_Timer, i } });
            yield return new WaitForSeconds(1f);
        }
        HandleTimerExpired();
    }

    // ─── MISCHIA: setup e gestione shots ────────────────────────────────────

    void SetupMischiaBalls()
    {
        if (ballController == null)
        {
            Debug.LogError("[GameManager] Mischia: ballController non assegnato!");
            return;
        }
        // Calcola le posizioni di spawn dalle CAPSULE reali (forward offset),
        // così sono sempre davanti ai giocatori indipendentemente dai valori serializzati.
        var playersGroup = GameObject.Find("Players");
        for (int i = 0; i < 4; i++)
        {
            Vector3 spawn = playerBallSpawnPositions[i]; // fallback
            if (playersGroup != null)
            {
                var capsule = playersGroup.transform.Find($"Player{i + 1}");
                if (capsule != null)
                    spawn = new Vector3(capsule.position.x, 0.5f, capsule.position.z + 1.6f);
            }
            runtimeBallSpawns[i] = spawn;
        }

        // Slot 0 = la palla originale del prefab
        mischiaBalls[0] = ballController;
        ballController.SetHomePosition(runtimeBallSpawns[0]);

        // Slot 1-3 = cloni della palla originale
        for (int i = 1; i < 4; i++)
        {
            var clone = Instantiate(ballController.gameObject, ballController.transform.parent);
            clone.name = $"Ball_{i}";
            var bc = clone.GetComponent<BallController>();
            bc.SetHomePosition(runtimeBallSpawns[i]);
            mischiaBalls[i] = bc;
        }

        // Scaling delle palle Mischia (default 2x). Applicato a TUTTE e 4
        // perché in modalità Mischia anche l'originale fa parte del pool.
        // Sono scene instances, non modificano il prefab su disco.
        if (mischiaBallScale > 0f && Mathf.Abs(mischiaBallScale - 1f) > 0.01f)
        {
            for (int i = 0; i < 4; i++)
            {
                if (mischiaBalls[i] != null)
                    mischiaBalls[i].transform.localScale *= mischiaBallScale;
            }
        }

        // Nascondi i palloni dei giocatori NON connessi (slot vuoti) → niente palle fantasma
        for (int i = 0; i < 4; i++)
        {
            if (mischiaBalls[i] != null)
                mischiaBalls[i].gameObject.SetActive(playerActors[i] != 0);
        }

        Debug.Log($"[GameManager] Mischia: palle pronte per {CountActivePlayers()} giocatori, scale x{mischiaBallScale}");
    }

    int CountActivePlayers()
    {
        int n = 0;
        foreach (var a in playerActors) if (a != 0) n++;
        return n;
    }

    void ResetMischiaShotsTracker()
    {
        for (int i = 0; i < 4; i++)
            mischiaShots[i] = new MischiaShot { panelIndex = -1, tapOrder = 0, resolved = false };
        mischiaTapCounter = 0;
    }

    /// <summary>Mischia: un giocatore ha tappato. Spawn della sua palla immediato.</summary>
    void HandleMischiaTap(int playerIdx, int panelIndex)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (Phase != TurnPhase.Aiming) return;
        if (playerIdx < 0 || playerIdx >= 4) return;
        if (mischiaShots[playerIdx].panelIndex >= 0) return; // già tappato

        mischiaTapCounter++;
        mischiaShots[playerIdx] = new MischiaShot
        {
            panelIndex = panelIndex,
            tapOrder   = mischiaTapCounter,
            resolved   = false
        };

        // Spawn ball del giocatore N (origine calcolata dalla capsule)
        if (mischiaBalls[playerIdx] != null)
        {
            mischiaBalls[playerIdx].ShootFromTo(
                origin:     runtimeBallSpawns[playerIdx],
                panelIndex: panelIndex,
                playerIdx:  playerIdx,
                order:      mischiaTapCounter);
        }

        Debug.Log($"[GameManager] Mischia tap: player={playerIdx}, panel={panelIndex}, order={mischiaTapCounter}");
    }

    /// <summary>Mischia: una palla è stata risolta (collisione o timeout).</summary>
    void HandleMischiaBallResolved(int playerIdx, int hitPanelIndex, bool saved)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (playerIdx < 0 || playerIdx >= 4) return;

        var shot = mischiaShots[playerIdx];
        if (shot.resolved) return;
        shot.resolved   = true;
        shot.wasSaved   = saved;
        shot.wasCorrect = (hitPanelIndex == correctPanel) && hitPanelIndex >= 0 && !saved;
        mischiaShots[playerIdx] = shot;

        Debug.Log($"[GameManager] Mischia resolved: player={playerIdx}, hit={hitPanelIndex}, saved={saved}, correct={shot.wasCorrect}");

        // Tutti risolti (chi non ha tappato è considerato risolto-no-shot)?
        if (AllMischiaShotsResolved())
            ResolveMischiaRound();
    }

    bool AllMischiaShotsResolved()
    {
        for (int i = 0; i < 4; i++)
        {
            if (playerActors[i] == 0) continue;              // slot vuoto: skip
            if (mischiaShots[i].panelIndex < 0) return false; // non ha ANCORA tappato → aspetta (no risoluzione prematura)
            if (!mischiaShots[i].resolved)      return false; // palla in volo → aspetta
        }
        return true;
    }

    /// <summary>Mischia: calcola scoring buzzer-style 3/2/1/1 sui correct.</summary>
    void ResolveMischiaRound()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (Phase == TurnPhase.Result) return;

        // Raccogli i correct ordinati per tapOrder
        var correctShots = new System.Collections.Generic.List<(int player, int order)>();
        for (int i = 0; i < 4; i++)
            if (mischiaShots[i].wasCorrect)
                correctShots.Add((i, mischiaShots[i].tapOrder));
        correctShots.Sort((a, b) => a.order.CompareTo(b.order));

        // Buzzer scoring: 3, 2, 1, 1
        int[] buzzerPoints = { 3, 2, 1, 1 };
        for (int rank = 0; rank < correctShots.Count; rank++)
        {
            int player = correctShots[rank].player;
            int pts    = buzzerPoints[Mathf.Min(rank, 3)];
            scores[player] += pts;
        }

        Phase = TurnPhase.Result;
        var props = new Hashtable
        {
            { K_Phase,  "Result"                  },
            { K_Scores, JsonHelper.ToJson(scores) }
            // Nota: in Mischia il feedback è "tutti vs tutti", non c'è una singola K_SelPanel
            //       da mostrare. ControllerManager mostrerà i risultati per-player dai K_SelN.
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);

        Debug.Log($"[GameManager] Mischia round resolved. Correct: {correctShots.Count}, scores=[{string.Join(",", scores)}]");

        StartCoroutine(ResultDelay());
    }

    /// <summary>Mischia: scaduto il timer. Forza il flight delle palle in volo e poi resolve.</summary>
    IEnumerator MischiaTimerExpiredFlow()
    {
        // Aspetta che eventuali palle ancora in volo atterrino (max 5s buffer)
        float maxWait = 5f;
        float t = 0f;
        while (t < maxWait && !AllMischiaShotsResolved())
        {
            t += Time.deltaTime;
            yield return null;
        }
        ResolveMischiaRound();
    }

    // ─── PHOTON CALLBACKS ────────────────────────────────────────────────────

    public override void OnRoomPropertiesUpdate(Hashtable changedProps)
    {
        // ── Timer ──
        if (changedProps.TryGetValue(K_Timer, out object tmrObj))
            HUDManager.Instance?.SetTimer((int)tmrObj);

        // ── Scores ──
        if (changedProps.TryGetValue(K_Scores, out object scObj))
            ApplyScores(JsonHelper.IntArrayFromJson((string)scObj));

        // ── TURNI: selezione singola via K_SelPanel + K_SelActor ──
        if (PhotonNetwork.IsMasterClient && CurrentMode == GameMode.Turns
            && changedProps.TryGetValue(K_SelPanel, out object sp2)
            && changedProps.TryGetValue(K_SelActor, out object sa2))
        {
            int sp = (int)sp2;
            int sa = (int)sa2;
            if (sp >= 0 && sa > 0)
                HandlePanelSelected(sp, sa);
        }

        // ── MISCHIA: selezioni multiple via K_Sel0..K_Sel3 ──
        if (PhotonNetwork.IsMasterClient && CurrentMode == GameMode.Free4All)
        {
            string[] selKeys = { K_Sel0, K_Sel1, K_Sel2, K_Sel3 };
            for (int i = 0; i < 4; i++)
            {
                if (changedProps.TryGetValue(selKeys[i], out object selObj))
                {
                    int sel = (int)selObj;
                    if (sel >= 0) HandleMischiaTap(playerIdx: i, panelIndex: sel);
                }
            }
        }

        // ── Phase ──
        if (!changedProps.TryGetValue(K_Phase, out object phaseObj)) return;
        string phase = (string)phaseObj;

        if (phase == "Aiming")
        {
            ApplyAimingState(changedProps);
            if (PhotonNetwork.IsMasterClient) StartTimer();
        }
        else if (phase == "InFlight")
        {
            Phase = TurnPhase.InFlight;
            HUDManager.Instance?.SetTimer(0);
            int sel = changedProps.TryGetValue(K_SelPanel, out object spObj) ? (int)spObj : -1;

            // Camera: inquadra il pannello target (o la porta se timeout)
            if (sel >= 0 && answerPanels != null && sel < answerPanels.Length && answerPanels[sel] != null)
                CameraController.Instance?.FocusPanel(answerPanels[sel].transform.position);
            else
                CameraController.Instance?.FocusMiss();

            if (ballController != null)
                ballController.ShootToPanel(sel);
            else if (PhotonNetwork.IsMasterClient)
                StartCoroutine(FallbackResolve(sel));

            // Reset hit flag su tutti i pannelli
            if (answerPanels != null)
                foreach (var p in answerPanels) p?.ResetHit();
        }
        else if (phase == "Result")
        {
            Phase = TurnPhase.Result;
            ApplyResultState();
            // Torna alla visuale di campo dopo un breve delay
            StartCoroutine(DelayedOverview(1.5f));
        }
        else if (phase == "End")
        {
            Phase = TurnPhase.End;
            LoadEndGameSceneOrFallback();
        }

    }

    /// <summary>
    /// Tenta di caricare la scena "EndGame". Se non è in Build Settings,
    /// mostra un overlay in-scena con i punteggi finali (no crash).
    /// </summary>
    void LoadEndGameSceneOrFallback()
    {
        if (SceneExistsInBuild("EndGame"))
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("EndGame");
        }
        else
        {
            Debug.LogWarning("[GameManager] Scena 'EndGame' non in Build Settings. Mostro overlay in-scena con punteggi finali.");
            ShowEndGameOverlay();
        }
    }

    static bool SceneExistsInBuild(string sceneName)
    {
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name == sceneName) return true;
        }
        return false;
    }

    void ShowEndGameOverlay()
    {
        // Costruisce a runtime un Canvas con "GAME OVER" + punteggi finali ordinati
        var canvasGO = new GameObject("EndGameOverlay");

        // ORDINE CORRETTO: Canvas PRIMA, poi CanvasScaler + GraphicRaycaster
        // (CanvasScaler ha [RequireComponent(typeof(Canvas))])
        var c = canvasGO.AddComponent<Canvas>();
        c.renderMode   = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 100;

        var scaler = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode         = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // Sfondo semi-trasparente
        var bg   = new GameObject("Background");
        bg.transform.SetParent(canvasGO.transform, false);
        var bgRT = bg.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;
        var bgImg = bg.AddComponent<UnityEngine.UI.Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.85f);

        // Titolo GAME OVER
        var titleGO = new GameObject("Title");
        titleGO.transform.SetParent(canvasGO.transform, false);
        var titleRT = titleGO.AddComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0.5f, 1f);
        titleRT.anchorMax = new Vector2(0.5f, 1f);
        titleRT.pivot     = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0, -120);
        titleRT.sizeDelta = new Vector2(1200, 140);
        var titleTmp = titleGO.AddComponent<TMPro.TextMeshProUGUI>();
        titleTmp.text      = "GAME OVER";
        titleTmp.fontSize  = 96;
        titleTmp.alignment = TMPro.TextAlignmentOptions.Center;
        titleTmp.color     = new Color(0.95f, 0.85f, 0.1f);
        titleTmp.fontStyle = TMPro.FontStyles.Bold;

        // Lista punteggi ordinati per score desc
        var rankings = new System.Collections.Generic.List<(int playerIdx, int score)>();
        for (int i = 0; i < 4; i++) rankings.Add((i + 1, scores[i]));
        rankings.Sort((a, b) => b.score.CompareTo(a.score));

        var scoresGO = new GameObject("Scores");
        scoresGO.transform.SetParent(canvasGO.transform, false);
        var scoresRT = scoresGO.AddComponent<RectTransform>();
        scoresRT.anchorMin = new Vector2(0.5f, 0.5f);
        scoresRT.anchorMax = new Vector2(0.5f, 0.5f);
        scoresRT.pivot     = new Vector2(0.5f, 0.5f);
        scoresRT.anchoredPosition = new Vector2(0, 0);
        scoresRT.sizeDelta = new Vector2(800, 400);
        var scoresTmp = scoresGO.AddComponent<TMPro.TextMeshProUGUI>();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < rankings.Count; i++)
        {
            string medal = $"{i + 1}°";
            sb.AppendLine($"{medal}  P{rankings[i].playerIdx}: {rankings[i].score} punti");
        }
        scoresTmp.text      = sb.ToString();
        scoresTmp.fontSize  = 64;
        scoresTmp.alignment = TMPro.TextAlignmentOptions.Center;
        scoresTmp.color     = Color.white;

        // Hint per uscire
        var hintGO = new GameObject("Hint");
        hintGO.transform.SetParent(canvasGO.transform, false);
        var hintRT = hintGO.AddComponent<RectTransform>();
        hintRT.anchorMin = new Vector2(0.5f, 0f);
        hintRT.anchorMax = new Vector2(0.5f, 0f);
        hintRT.pivot     = new Vector2(0.5f, 0f);
        hintRT.anchoredPosition = new Vector2(0, 80);
        hintRT.sizeDelta = new Vector2(800, 60);
        var hintTmp = hintGO.AddComponent<TMPro.TextMeshProUGUI>();
        hintTmp.text      = $"Ritorno al menu tra {endGameReturnSeconds}...";
        hintTmp.fontSize  = 28;
        hintTmp.alignment = TMPro.TextAlignmentOptions.Center;
        hintTmp.color     = new Color(0.7f, 0.7f, 0.7f);

        // Ritorno automatico al MainMenu dopo endGameReturnSeconds (i punteggi restano visibili)
        StartCoroutine(ReturnToMenuCountdown(hintTmp, endGameReturnSeconds));
    }

    IEnumerator ReturnToMenuCountdown(TMPro.TextMeshProUGUI label, int seconds)
    {
        for (int i = seconds; i > 0; i--)
        {
            if (label != null) label.text = $"Ritorno al menu tra {i}...";
            yield return new WaitForSeconds(1f);
        }
        ReturnToMainMenu();
    }

    void ReturnToMainMenu()
    {
        // LeaveRoom → NetworkManager.OnLeftRoom carica automaticamente il MainMenu.
        if (PhotonNetwork.InRoom && NetworkManager.Instance != null)
            NetworkManager.Instance.LeaveRoom();
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }

    void ApplyAimingState(Hashtable props)
    {
        Phase = TurnPhase.Aiming;

        if (props.TryGetValue(K_Question, out object q))
            HUDManager.Instance?.SetQuestion((string)q);

        if (props.TryGetValue(K_CurrentTurn, out object ct))
        {
            currentTurn = (int)ct;
            HUDManager.Instance?.SetTurn(currentTurn + 1);
        }

        if (props.TryGetValue(K_CorrectIdx, out object ci))
            correctPanel = (int)ci;

        if (props.TryGetValue(K_Answers, out object ans))
        {
            string[] answers = JsonHelper.StringArrayFromJson((string)ans);
            if (answerPanels != null)
                for (int i = 0; i < answerPanels.Length && i < answers.Length; i++)
                    answerPanels[i]?.SetLabel(answers[i]);
        }

        HUDManager.Instance?.SetTimer(timerSeconds);

        if (answerPanels != null)
            foreach (var p in answerPanels) p?.ResetColor();

        // Camera: centra il giocatore di turno
        CameraController.Instance?.FocusPlayer(currentTurn);
    }

    IEnumerator DelayedOverview(float delay)
    {
        yield return new WaitForSeconds(delay);
        CameraController.Instance?.SetOverview();
    }

    void ApplyResultState()
    {
        var allProps = PhotonNetwork.CurrentRoom.CustomProperties;
        int corrIdx  = allProps.TryGetValue(K_CorrectIdx, out object ci) ? (int)ci : -1;

        // ── MISCHIA: niente singolo Corretto/Sbagliato (4 risultati diversi).
        // Evidenzia solo il pannello corretto in verde. Ogni telefono mostra il proprio esito. ──
        if (CurrentMode == GameMode.Free4All)
        {
            if (answerPanels != null)
                for (int i = 0; i < answerPanels.Length; i++)
                    if (i == corrIdx) answerPanels[i]?.SetFeedback(true);
            return;
        }

        // ── TURNI: feedback singolo ──
        int  selPanel = allProps.TryGetValue(K_SelPanel, out object sp) ? (int)sp : -1;
        bool saved    = allProps.TryGetValue(K_Saved,    out object sv) && (bool)sv;

        bool answeredCorrectly = selPanel == corrIdx && selPanel >= 0;
        HUDManager.Instance?.ShowAnswerFeedback(answeredCorrectly, saved);

        // Pannelli: colora SEMPRE il corretto in verde. Il pannello scelto:
        //  - se saved: giallo (intercettato)
        //  - se sbagliato: rosso
        //  - se corretto: già verde dal ramo sopra
        if (answerPanels != null)
        {
            for (int i = 0; i < answerPanels.Length; i++)
            {
                if (i == corrIdx)
                    answerPanels[i]?.SetFeedback(true);              // giusto → verde
                else if (i == selPanel && saved)
                    answerPanels[i]?.SetFeedbackSaved();             // scelto + parato → giallo
                else if (i == selPanel)
                    answerPanels[i]?.SetFeedback(false);             // scelto sbagliato → rosso
            }
        }
    }

    void ApplyScores(int[] s)
    {
        if (s == null) return;
        for (int i = 0; i < s.Length && i < 4; i++)
        {
            scores[i] = s[i];
            HUDManager.Instance?.SetScore(i + 1, s[i]);
        }
    }
}
