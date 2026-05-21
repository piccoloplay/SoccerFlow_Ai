using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Realtime;

/// <summary>
/// SoccerFlaw_Ai — MainMenuUI
/// Logica della scena MainMenu: gestisce i tre pannelli (Connecting → Lobby → Room)
/// e aggancia i delegate di NetworkManager.
///
/// Viene popolato da MainMenuBuilder che crea la UI e assegna tutti i riferimenti.
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    // ─── PANNELLI ────────────────────────────────────────────────────────────
    [HideInInspector] public GameObject panelConnecting;
    [HideInInspector] public GameObject panelLobby;
    [HideInInspector] public GameObject panelRoom;

    // ─── CONNECTING ──────────────────────────────────────────────────────────
    [HideInInspector] public TextMeshProUGUI connectingText;

    // ─── LOBBY ───────────────────────────────────────────────────────────────
    [HideInInspector] public TMP_InputField  inputPlayerName;
    [HideInInspector] public TMP_InputField  inputRoomName;
    [HideInInspector] public Button          btnCreate;
    [HideInInspector] public Button          btnJoin;
    [HideInInspector] public TextMeshProUGUI errorText;
    [HideInInspector] public Transform       roomListContainer;
    [HideInInspector] public GameObject      roomEntryPrefab;   // creato da Builder

    // ─── ROOM ────────────────────────────────────────────────────────────────
    [HideInInspector] public TextMeshProUGUI roomNameText;
    [HideInInspector] public Transform       playerListContainer;
    [HideInInspector] public GameObject      playerEntryPrefab; // creato da Builder
    [HideInInspector] public Button          btnStart;
    [HideInInspector] public Button          btnLeave;
    [HideInInspector] public TextMeshProUGUI waitingText;
    [HideInInspector] public Toggle          toggleGoalkeeper;
    [HideInInspector] public Toggle          toggleMischia;

    // ─────────────────────────────────────────────────────────────────────────

    readonly List<GameObject> roomEntries   = new();
    readonly List<GameObject> playerEntries = new();

    bool subscribed;

    void Start()
    {
        EnsureSubscribed();
        InitUIState();
    }

    // Sottoscrizione ai delegate NetworkManager: UNA volta sola (l'oggetto è persistente).
    void EnsureSubscribed()
    {
        if (subscribed) return;
        var nm = NetworkManager.Instance;
        if (nm == null) { Debug.LogError("[MainMenuUI] NetworkManager non trovato!"); return; }

        nm.OnReady        += OnReady;
        nm.OnRoomsUpdated += OnRoomsUpdated;
        nm.OnRoomJoined   += OnRoomJoined;
        nm.OnPlayerJoined += OnPlayerJoined;
        nm.OnPlayerLeft   += OnPlayerLeft;
        nm.OnError        += OnError;
        subscribed = true;
    }

    // Aggancia i listener ai bottoni: vanno riassegnati ogni volta che la UI viene
    // (ri)costruita, perché i Button sono GameObject nuovi.
    void WireButtons()
    {
        btnCreate.onClick.AddListener(OnClickCreate);
        btnJoin.onClick.AddListener(OnClickJoin);
        btnStart.onClick.AddListener(OnClickStart);
        btnLeave.onClick.AddListener(OnClickLeave);
    }

    // Stato iniziale della UI. Ripetibile (primo avvio e ritorno dal gioco).
    void InitUIState()
    {
        ShowPanel(panelConnecting);
        WireButtons();
        AutoFillInputs();

        var nm = NetworkManager.Instance;
        if (nm == null) return;
        if (nm.IsConnected) nm.EnsureInLobby();   // ritorno dal gioco: già connessi → rientra in lobby
        else                nm.Connect();          // primo avvio
    }

    void AutoFillInputs()
    {
        // Solo nome room. I nomi giocatore sono assegnati da Photon al join (Server/Player1/...).
        string defaultRoom = PlayerPrefs.GetString("LastRoomName", "flo");
        if (inputRoomName != null) inputRoomName.text = defaultRoom;
    }

    // La MainMenu UI (Canvas) è figlia del GameObject DontDestroyOnLoad (NetworkManager).
    // Quando cambiamo scena (Game/Controller/EndGame) la UI sopravviverebbe → la distruggiamo.
    void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    void HandleSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                           UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (scene.name == "MainMenu")
        {
            // Tornati al menu: entrando in gioco la UI (Canvas) era stata distrutta,
            // ma questo oggetto è persistente. Se i pannelli non esistono più,
            // ricostruisci la UI e ripristina lo stato.
            if (panelConnecting == null)
            {
                GetComponent<MainMenuBuilder>()?.RebuildUI();
                InitUIState();
            }
            return;
        }

        // Distruggi il Canvas bakato sopravvissuto via DontDestroyOnLoad
        Transform anyPanel = panelConnecting != null ? panelConnecting.transform
                           : panelLobby      != null ? panelLobby.transform
                           : panelRoom       != null ? panelRoom.transform : null;
        if (anyPanel != null)
        {
            var canvas = anyPanel.GetComponentInParent<Canvas>();
            if (canvas != null) Destroy(canvas.gameObject);
        }
    }

    void OnDestroy()
    {
        var nm = NetworkManager.Instance;
        if (nm == null) return;
        nm.OnReady        -= OnReady;
        nm.OnRoomsUpdated -= OnRoomsUpdated;
        nm.OnRoomJoined   -= OnRoomJoined;
        nm.OnPlayerJoined -= OnPlayerJoined;
        nm.OnPlayerLeft   -= OnPlayerLeft;
        nm.OnError        -= OnError;
    }

    // ─── CALLBACKS NETWORKMANAGER ────────────────────────────────────────────

    void OnReady()
    {
        ShowPanel(panelLobby);
        SetError("");
    }

    void OnRoomsUpdated(List<RoomInfo> rooms)
    {
        if (roomEntryPrefab == null || roomListContainer == null) return;   // UI non pronta
        ClearList(roomEntries);
        foreach (var room in rooms)
        {
            if (!room.IsOpen || !room.IsVisible || room.RemovedFromList) continue;
            var entry = Instantiate(roomEntryPrefab, roomListContainer);
            var tmp   = entry.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp) tmp.text = $"{room.Name}  ({room.PlayerCount}/{room.MaxPlayers})";
            var btn = entry.GetComponent<Button>();
            if (btn)
            {
                string rName = room.Name;
                btn.onClick.AddListener(() => inputRoomName.text = rName);
            }
            roomEntries.Add(entry);
        }
    }

    void OnRoomJoined()
    {
        ShowPanel(panelRoom);
        RefreshPlayerList();

        bool isMaster = NetworkManager.Instance.IsMasterClient;
        roomNameText.text = $"Room: {Photon.Pun.PhotonNetwork.CurrentRoom.Name}";
        btnStart.gameObject.SetActive(isMaster);
        waitingText.gameObject.SetActive(!isMaster);

        // Toggle Portiere/Mischia: visibili SOLO al Master (chi crea la room).
        // Chi si unisce non li vede — la modalità la decide il creatore.
        if (toggleGoalkeeper != null)
        {
            toggleGoalkeeper.gameObject.SetActive(isMaster);
            toggleGoalkeeper.isOn = false;
        }
        if (toggleMischia != null)
        {
            toggleMischia.gameObject.SetActive(isMaster);
            toggleMischia.isOn = false;
        }
    }

    void OnPlayerJoined(Player _)
    {
        RefreshPlayerList();
        UpdateStartButton();
    }

    void OnPlayerLeft(Player _)
    {
        RefreshPlayerList();
        UpdateStartButton();
    }

    void OnError(string msg)
    {
        SetError(msg);
        if (panelConnecting != null && panelConnecting.activeSelf && connectingText != null)
            connectingText.text = $"Errore: {msg}";
    }

    // ─── BOTTONI ─────────────────────────────────────────────────────────────

    void OnClickCreate()
    {
        string roomName = inputRoomName.text.Trim();
        if (string.IsNullOrEmpty(roomName)) { SetError("Inserisci il nome della room."); return; }
        SetError("");
        PlayerPrefs.SetString("LastRoomName", roomName); PlayerPrefs.Save();
        NetworkManager.Instance.CreateRoom(roomName, null);   // nome assegnato al join
    }

    void OnClickJoin()
    {
        string roomName = inputRoomName.text.Trim();
        if (string.IsNullOrEmpty(roomName)) { SetError("Inserisci il nome della room."); return; }
        SetError("");
        PlayerPrefs.SetString("LastRoomName", roomName); PlayerPrefs.Save();
        NetworkManager.Instance.JoinRoom(roomName, null);     // nome assegnato al join
    }

    void OnClickStart()
    {
        if (NetworkManager.Instance.PlayerCount < 2)
        {
            SetError("Aspetta almeno un giocatore.");
            return;
        }
        bool gkEnabled = toggleGoalkeeper != null && toggleGoalkeeper.isOn;
        bool mischia   = toggleMischia    != null && toggleMischia.isOn;
        NetworkManager.Instance.StartGame(gkEnabled, mischia);
    }

    void OnClickLeave() => NetworkManager.Instance.LeaveRoom();

    // ─── HELPERS ─────────────────────────────────────────────────────────────

    void RefreshPlayerList()
    {
        if (playerEntryPrefab == null || playerListContainer == null) return;   // UI non pronta
        ClearList(playerEntries);
        foreach (var p in NetworkManager.Instance.PlayerList)
        {
            var entry = Instantiate(playerEntryPrefab, playerListContainer);
            entry.SetActive(true);
            var tmp = entry.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp)
            {
                string role = p.IsMasterClient ? " [PC]" : " [Telefono]";
                tmp.text    = p.NickName + role;
            }
            playerEntries.Add(entry);
        }
    }

    void UpdateStartButton()
    {
        if (!NetworkManager.Instance.IsMasterClient) return;
        bool canStart = NetworkManager.Instance.PlayerCount >= 2;
        btnStart.interactable  = canStart;
        waitingText.text       = canStart ? "" : "In attesa di giocatori…";
    }

    void ShowPanel(GameObject panel)
    {
        if (panelConnecting == null || panelLobby == null || panelRoom == null) return;
        panelConnecting.SetActive(panel == panelConnecting);
        panelLobby.SetActive(panel == panelLobby);
        panelRoom.SetActive(panel == panelRoom);
    }

    void SetError(string msg)
    {
        if (errorText) errorText.text = msg;
    }

    static void ClearList(List<GameObject> list)
    {
        foreach (var go in list) Destroy(go);
        list.Clear();
    }
}
