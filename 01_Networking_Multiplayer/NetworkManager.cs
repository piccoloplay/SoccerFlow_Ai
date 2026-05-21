using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;

/// <summary>
/// SoccerFlaw_Ai — NetworkManager
/// Gestisce connessione a Photon, lobby e room. Persiste tra le scene.
///
/// SETUP nell'editor:
/// 1. Crea un GameObject "NetworkManager" nella scena MainMenu.
/// 2. Aggiungi questo script.
/// 3. Crea il file PhotonServerSettings (Window → Photon Unity Networking → Setup Wizard).
/// 4. Collega questo singleton alla tua UI di MainMenu via i delegate pubblici.
/// </summary>
public class NetworkManager : MonoBehaviourPunCallbacks
{
    public static NetworkManager Instance { get; private set; }

    [Header("─── Nomi Scena ────────────────────────────────")]
    public string mainMenuScene   = "MainMenu";
    public string gameScene       = "Game";
    public string controllerScene = "Controller";
    public string endGameScene    = "EndGame";

    [Header("─── Room ──────────────────────────────────────")]
    [Tooltip("1 PC + 4 telefoni")]
    public byte maxPlayers = 5;

    // ─── DELEGATE per la UI della MainMenu ──────────────────────────────────
    /// <summary>Connessione riuscita, pronti per creare/joinare room.</summary>
    public System.Action OnReady;
    /// <summary>Aggiornamento lista stanze in lobby.</summary>
    public System.Action<List<RoomInfo>> OnRoomsUpdated;
    /// <summary>Siamo entrati nella room.</summary>
    public System.Action OnRoomJoined;
    /// <summary>Un altro giocatore è entrato nella room.</summary>
    public System.Action<Player> OnPlayerJoined;
    /// <summary>Un giocatore ha lasciato la room.</summary>
    public System.Action<Player> OnPlayerLeft;
    /// <summary>Errore generico con messaggio.</summary>
    public System.Action<string> OnError;

    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        // I client NON sincronizzano automaticamente la scena — ciascuno la carica
        // in base al proprio dispositivo (Desktop → Game, Mobile → Controller).
        PhotonNetwork.AutomaticallySyncScene = false;
    }

    // ─── PUBLIC API ──────────────────────────────────────────────────────────

    /// <summary>Avvia connessione a Photon. Chiama prima del resto.</summary>
    public void Connect()
    {
        if (PhotonNetwork.IsConnected) { OnReady?.Invoke(); return; }
        PhotonNetwork.ConnectUsingSettings();
        PhotonNetwork.GameVersion = "1.0";
    }

    /// <summary>Crea una nuova room. Chiamare dopo Connect().</summary>
    public void CreateRoom(string roomName, string playerName)
    {
        SetLocalNickname(playerName);
        var opts = new RoomOptions { MaxPlayers = maxPlayers };
        PhotonNetwork.CreateRoom(roomName, opts);
    }

    /// <summary>Entra in una room esistente.</summary>
    public void JoinRoom(string roomName, string playerName)
    {
        SetLocalNickname(playerName);
        PhotonNetwork.JoinRoom(roomName);
    }

    /// <summary>Torna al MainMenu.</summary>
    public void LeaveRoom() => PhotonNetwork.LeaveRoom();

    /// <summary>
    /// Tornando al MainMenu dopo una partita siamo ancora connessi ma fuori dalla room.
    /// Rientra in lobby per rivedere la lista stanze (→ OnJoinedLobby → OnReady).
    /// Se siamo già in lobby, notifica subito OnReady.
    /// </summary>
    public void EnsureInLobby()
    {
        if (PhotonNetwork.InLobby)            OnReady?.Invoke();
        else if (PhotonNetwork.IsConnected && !PhotonNetwork.InRoom) PhotonNetwork.JoinLobby();
    }

    public bool IsConnected    => PhotonNetwork.IsConnected;
    public bool IsInRoom       => PhotonNetwork.InRoom;
    public bool IsMasterClient => PhotonNetwork.IsMasterClient;
    public int  PlayerCount    => PhotonNetwork.CurrentRoom?.PlayerCount ?? 0;
    public Player[] PlayerList => PhotonNetwork.PlayerList;

    // ─── PHOTON CALLBACKS ────────────────────────────────────────────────────

    public override void OnConnectedToMaster()    => PhotonNetwork.JoinLobby();
    public override void OnJoinedLobby()          => OnReady?.Invoke();
    public override void OnCreatedRoom()          { /* OnJoinedRoom segue subito */ }

    public override void OnRoomListUpdate(List<RoomInfo> roomList)
        => OnRoomsUpdated?.Invoke(roomList);

    public override void OnJoinedRoom()
    {
        AssignAutoNickname();
        OnRoomJoined?.Invoke();
        // NON caricare la scena qui — aspetta StartGame()
    }

    /// <summary>
    /// Nome automatico in base al ruolo/ordine di join:
    ///   MasterClient (primo connesso) → "Server"
    ///   altri → "Player1", "Player2", ... in ordine di ActorNumber (= ordine di join)
    /// </summary>
    void AssignAutoNickname()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.LocalPlayer.NickName = "Server";
            return;
        }
        int idx = 0;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (p.IsMasterClient) continue;
            if (p.ActorNumber == PhotonNetwork.LocalPlayer.ActorNumber) break;
            idx++;
        }
        PhotonNetwork.LocalPlayer.NickName = $"Player{idx + 1}";
    }

    /// <summary>Chiamato dal MasterClient quando clicca Inizia Partita.</summary>
    public void StartGame(bool goalkeeperEnabled = false, bool mischiaMode = false)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        var props = new ExitGames.Client.Photon.Hashtable
        {
            { "gameStarted",            true                                 },
            { GameManager.K_Goalkeeper, goalkeeperEnabled                    },
            { GameManager.K_GameMode,   mischiaMode ? "free4all" : "turns"   }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable changedProps)
    {
        if (changedProps.ContainsKey("gameStarted") && (bool)changedProps["gameStarted"])
        {
            string scene = PhotonNetwork.IsMasterClient ? gameScene : controllerScene;
            SceneManager.LoadScene(scene);
        }
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
        => OnPlayerJoined?.Invoke(newPlayer);

    public override void OnPlayerLeftRoom(Player otherPlayer)
        => OnPlayerLeft?.Invoke(otherPlayer);

    public override void OnLeftRoom()
        => SceneManager.LoadScene(mainMenuScene);

    public override void OnJoinRoomFailed(short code, string msg)
        => OnError?.Invoke($"Join fallito ({code}): {msg}");

    public override void OnCreateRoomFailed(short code, string msg)
        => OnError?.Invoke($"Crea room fallito ({code}): {msg}");

    public override void OnDisconnected(DisconnectCause cause)
        => OnError?.Invoke($"Disconnesso: {cause}");

    // ─── INTERNAL ────────────────────────────────────────────────────────────

    static void SetLocalNickname(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
            PhotonNetwork.LocalPlayer.NickName = name;
    }
}
