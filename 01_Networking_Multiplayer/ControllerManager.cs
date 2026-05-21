using System.Collections;
using UnityEngine;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// SoccerFlaw_Ai — ControllerManager
/// Gestisce la scena Controller (telefono): sincronizza la UI con le Room Properties
/// e coordina i 9 ControllerPanel.
///
/// SETUP nell'editor (scena Controller):
/// 1. Crea un Canvas (Screen Space - Overlay, Scale With Screen Size 1080×1920).
/// 2. Aggiungi un GameObject "ControllerManager" con questo script + PhotonView.
/// 3. Collega i TextMeshProUGUI per question, turn, timer, score1-4.
/// 4. Trascina i 9 ControllerPanel[] nell'Inspector.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class ControllerManager : MonoBehaviourPunCallbacks
{
    public static ControllerManager Instance { get; private set; }

    [Header("─── HUD Telefono ─────────────────────────────")]
    public TextMeshProUGUI questionText;
    public TextMeshProUGUI turnText;
    public TextMeshProUGUI timerText;
    public TextMeshProUGUI scoreP1;
    public TextMeshProUGUI scoreP2;
    public TextMeshProUGUI scoreP3;
    public TextMeshProUGUI scoreP4;

    [Header("─── Pannelli (9 ControllerPanel) ─────────────")]
    public ControllerPanel[] panels;    // 9 elementi, ordine 0-8

    [Header("─── Colori Giocatori ───────────────────────────")]
    public Color p1Color = Color.red;
    public Color p2Color = new Color(0.15f, 0.37f, 0.78f);
    public Color p3Color = new Color(0.18f, 0.60f, 0.25f);
    public Color p4Color = new Color(0.95f, 0.85f, 0.10f);

    int currentTurn = 0;

    void Awake() { Instance = this; }

    void Start()
    {
        UpdateScoreVisibility();
        // Leggi lo stato attuale della Room (se la partita è già in corso)
        if (PhotonNetwork.InRoom)
            ApplyAllProperties(PhotonNetwork.CurrentRoom.CustomProperties);
    }

    /// <summary>Nasconde le label punteggio dei giocatori non connessi (slot vuoti).</summary>
    void UpdateScoreVisibility()
    {
        int activePlayers = 0;
        if (PhotonNetwork.InRoom)
            foreach (var p in PhotonNetwork.PlayerList)
                if (!p.IsMasterClient) activePlayers++;

        if (scoreP1) scoreP1.gameObject.SetActive(activePlayers >= 1);
        if (scoreP2) scoreP2.gameObject.SetActive(activePlayers >= 2);
        if (scoreP3) scoreP3.gameObject.SetActive(activePlayers >= 3);
        if (scoreP4) scoreP4.gameObject.SetActive(activePlayers >= 4);
    }

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer) => UpdateScoreVisibility();
    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)  => UpdateScoreVisibility();

    // ─── PHOTON CALLBACKS ────────────────────────────────────────────────────

    public override void OnRoomPropertiesUpdate(Hashtable changedProps)
    {
        // Timer
        if (changedProps.TryGetValue(GameManager.K_Timer, out object tmr))
            SetTimer((int)tmr);

        // Scores
        if (changedProps.TryGetValue(GameManager.K_Scores, out object sc))
            ApplyScores(JsonHelper.IntArrayFromJson((string)sc));

        // Phase
        if (!changedProps.TryGetValue(GameManager.K_Phase, out object phObj)) return;
        string phase = (string)phObj;

        if (phase == "Aiming")
        {
            ApplyAimingState(changedProps);
        }
        else if (phase == "InFlight" || phase == "Result")
        {
            // Disabilita tutti i pannelli durante il volo e il risultato
            SetAllPanelsInteractable(false);

            if (phase == "Result")
                ApplyResultFeedback(changedProps);
        }
        else if (phase == "End")
        {
            SetAllPanelsInteractable(false);
            if (SceneExistsInBuild("EndGame"))
                UnityEngine.SceneManagement.SceneManager.LoadScene("EndGame");
            else
                ShowEndOnPhone();
        }
    }

    // ─── LOGICA FASI ─────────────────────────────────────────────────────────

    void ApplyAimingState(Hashtable props)
    {
        // Domanda
        if (props.TryGetValue(GameManager.K_Question, out object q) && questionText)
            questionText.text = (string)q;

        // Turno (in Mischia il currentTurn è irrilevante ma lo leggiamo comunque)
        if (props.TryGetValue(GameManager.K_CurrentTurn, out object ct))
            currentTurn = (int)ct;

        // Modalità
        bool isMischia = ReadIsMischia();

        if (turnText)
        {
            if (isMischia)
            {
                turnText.text  = "MISCHIA — Tappa veloce!";
                turnText.color = new Color(0.95f, 0.85f, 0.10f);
            }
            else
            {
                turnText.text  = $"Turno: P{currentTurn + 1}";
                turnText.color = PlayerColor(currentTurn + 1);
            }
        }

        // Risposte sui pannelli
        if (props.TryGetValue(GameManager.K_Answers, out object ans))
        {
            string[] answers = JsonHelper.StringArrayFromJson((string)ans);
            for (int i = 0; i < panels.Length && i < answers.Length; i++)
                panels[i]?.SetLabel(answers[i]);
        }

        // Abilitazione pannelli:
        //   - Mischia: TUTTI i giocatori sempre, finché non tappano (poi LockAllPanels)
        //   - Turni: solo per il giocatore di turno
        bool enable = isMischia || IsMyTurn();
        SetAllPanelsInteractable(enable);

        if (timerText) timerText.text = isMischia ? "8" : "15";
    }

    static bool ReadIsMischia()
    {
        if (!PhotonNetwork.InRoom) return false;
        var p = PhotonNetwork.CurrentRoom.CustomProperties;
        return p.TryGetValue(GameManager.K_GameMode, out object m) && m is string s && s == "free4all";
    }

    /// <summary>Indice 0-3 del giocatore locale (escludendo il MasterClient).</summary>
    static int ResolveMyPlayerIndex()
    {
        int idx = 0;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (p.IsMasterClient) continue;
            if (p.ActorNumber == PhotonNetwork.LocalPlayer.ActorNumber) return idx;
            idx++;
        }
        return -1;
    }

    /// <summary>Pannello scelto dal giocatore locale in Mischia (K_Sel{myIdx}).</summary>
    static int ReadMySelection()
    {
        int myIdx = ResolveMyPlayerIndex();
        if (myIdx < 0 || !PhotonNetwork.InRoom) return -1;
        string key = myIdx == 0 ? GameManager.K_Sel0
                   : myIdx == 1 ? GameManager.K_Sel1
                   : myIdx == 2 ? GameManager.K_Sel2
                   :              GameManager.K_Sel3;
        var props = PhotonNetwork.CurrentRoom.CustomProperties;
        return props.TryGetValue(key, out object v) ? (int)v : -1;
    }

    /// <summary>Mischia: chiamato da ControllerPanel.OnClick dopo il primo tap → disabilita tutti gli altri.</summary>
    public void LockAllPanels() => SetAllPanelsInteractable(false);

    void ApplyResultFeedback(Hashtable props)
    {
        var allProps = PhotonNetwork.CurrentRoom.CustomProperties;
        int corrIdx  = allProps.TryGetValue(GameManager.K_CorrectIdx, out object ci) ? (int)ci : -1;

        // ── MISCHIA: ogni telefono mostra il PROPRIO esito (la sua selezione vs corretta) ──
        if (ReadIsMischia())
        {
            int mySel = ReadMySelection();
            bool iWasCorrect = mySel == corrIdx && mySel >= 0;

            for (int i = 0; i < panels.Length; i++)
            {
                if (i == corrIdx)      panels[i]?.SetFeedback(true);
                else if (i == mySel)   panels[i]?.SetFeedback(false);
            }

            if (turnText != null)
            {
                turnText.text  = iWasCorrect ? "Corretto!" : "Sbagliato";
                turnText.color = iWasCorrect ? Color.green : Color.red;
            }
            return;
        }

        // ── TURNI: feedback singolo condiviso ──
        int  selPanel = allProps.TryGetValue(GameManager.K_SelPanel,   out object sp) ? (int)sp  : -1;
        bool saved    = allProps.TryGetValue(GameManager.K_Saved,      out object sv) && (bool)sv;

        // Pannelli: verde sul corretto SEMPRE. Pannello scelto:
        //   - saved → giallo (intercettato dal portiere)
        //   - sbagliato → rosso
        for (int i = 0; i < panels.Length; i++)
        {
            if (i == corrIdx)
                panels[i]?.SetFeedback(true);
            else if (i == selPanel && saved)
                panels[i]?.SetFeedbackSaved();
            else if (i == selPanel)
                panels[i]?.SetFeedback(false);
        }

        // Header del telefono: messaggio chiaro col colore corrispondente
        if (turnText != null)
        {
            if (saved)
            {
                turnText.text  = (selPanel == corrIdx)
                    ? "PARATA! Punto annullato"
                    : "PARATA!";
                turnText.color = new Color(0.95f, 0.75f, 0.10f);
            }
            else if (selPanel == corrIdx && selPanel >= 0)
            {
                turnText.text  = "Corretto!";
                turnText.color = Color.green;
            }
            else
            {
                turnText.text  = "Sbagliato";
                turnText.color = Color.red;
            }
        }
    }

    void ApplyAllProperties(Hashtable props)
    {
        ApplyScores(JsonHelper.IntArrayFromJson(
            props.TryGetValue(GameManager.K_Scores, out object sc) ? (string)sc : null));

        if (props.TryGetValue(GameManager.K_Phase, out object ph))
            OnRoomPropertiesUpdate(props);   // ri-usa la stessa logica
    }

    // ─── END GAME SAFE FALLBACK ──────────────────────────────────────────────

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

    void ShowEndOnPhone()
    {
        // Riusa questionText come banner di fine partita
        if (questionText != null)
        {
            var allProps = PhotonNetwork.CurrentRoom.CustomProperties;
            int[] s = JsonHelper.IntArrayFromJson(
                allProps.TryGetValue(GameManager.K_Scores, out object sc) ? (string)sc : null);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("GAME OVER\n");
            if (s != null && s.Length > 0)
                for (int i = 0; i < s.Length && i < 4; i++)
                    sb.AppendLine($"P{i + 1}: {s[i]}");

            questionText.text  = sb.ToString();
            questionText.color = new Color(0.95f, 0.85f, 0.1f);
        }
        if (turnText  != null) turnText.text  = "Partita finita";
        if (timerText != null) timerText.text = "—";

        // Ritorno automatico al MainMenu, sincronizzato col PC (i punteggi restano visibili)
        StartCoroutine(ReturnToMenuCountdown(15));
    }

    IEnumerator ReturnToMenuCountdown(int seconds)
    {
        for (int i = seconds; i > 0; i--)
        {
            if (turnText != null) turnText.text = $"Ritorno al menu tra {i}...";
            yield return new WaitForSeconds(1f);
        }
        // LeaveRoom → NetworkManager.OnLeftRoom carica automaticamente il MainMenu.
        if (PhotonNetwork.InRoom && NetworkManager.Instance != null)
            NetworkManager.Instance.LeaveRoom();
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────────

    bool IsMyTurn()
    {
        // Il giocatore locale è di turno se il suo ActorNumber coincide con quello
        // registrato da GameManager per il currentTurn.
        // Usiamo la Room Property K_SelActor come canale di confronto —
        // in alternativa leggiamo playerActors via Room Custom Properties.
        // Approccio semplice: confronta l'indice 0-3 con il NickName o ActorNumber.
        // Per ora usiamo una Room Property "actorTurn" che GameManager può impostare,
        // oppure confrontiamo tramite la lista giocatori.
        var props = PhotonNetwork.CurrentRoom.CustomProperties;
        if (!props.TryGetValue(GameManager.K_CurrentTurn, out object ct)) return false;

        int turnIdx = (int)ct;
        // Ordina i client NON MasterClient per ActorNumber (stesso ordine di GameManager.BuildPlayerList)
        var players = PhotonNetwork.PlayerList;
        int phoneIdx = 0;
        foreach (var p in players)
        {
            if (p.IsMasterClient) continue;
            if (phoneIdx == turnIdx)
                return p.ActorNumber == PhotonNetwork.LocalPlayer.ActorNumber;
            phoneIdx++;
        }
        return false;
    }

    void SetAllPanelsInteractable(bool value)
    {
        if (panels == null) return;
        foreach (var p in panels) p?.SetInteractable(value);
    }

    void SetTimer(int seconds)
    {
        if (timerText == null) return;
        timerText.text  = seconds.ToString();
        timerText.color = seconds <= 5 ? Color.red : Color.white;
    }

    void ApplyScores(int[] s)
    {
        if (s == null) return;
        SetScoreText(scoreP1, 1, s.Length > 0 ? s[0] : 0);
        SetScoreText(scoreP2, 2, s.Length > 1 ? s[1] : 0);
        SetScoreText(scoreP3, 3, s.Length > 2 ? s[2] : 0);
        SetScoreText(scoreP4, 4, s.Length > 3 ? s[3] : 0);
    }

    void SetScoreText(TextMeshProUGUI label, int playerIdx, int score)
    {
        if (label == null) return;
        label.text  = $"P{playerIdx}: {score}";
        label.color = PlayerColor(playerIdx);
    }

    Color PlayerColor(int idx) => idx switch
    {
        1 => p1Color, 2 => p2Color, 3 => p3Color, 4 => p4Color, _ => Color.white
    };
}
