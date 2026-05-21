using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using ExitGames.Client.Photon;

/// <summary>
/// SoccerFlaw_Ai — ControllerPanel
/// Pannello UI nella scena Controller (telefono).
/// Ogni pannello corrisponde a una delle 9 risposte.
///
/// SETUP nell'editor (scena Controller):
/// 1. Crea un Canvas → Grid Layout Group (3×3).
/// 2. Aggiungi 9 Button figli, ognuno con questo script.
/// 3. Imposta panelIndex (0-8) su ciascuno.
/// 4. Trascina i 9 panel nell'array del ControllerManager.
///
/// Il pannello è abilitato solo quando è il turno del giocatore locale.
/// Al tocco invia il panelIndex via Room Custom Properties al GameManager (MasterClient).
/// </summary>
[RequireComponent(typeof(Button))]
public class ControllerPanel : MonoBehaviour
{
    [Header("─── Identità ──────────────────────────────────")]
    public int panelIndex;

    [Header("─── UI (auto-rilevati se non assegnati) ────────")]
    public TextMeshProUGUI labelText;
    public Button          button;

    [Header("─── Colori ─────────────────────────────────────")]
    public Color activeColor   = new Color(0.15f, 0.37f, 0.78f);
    public Color disabledColor = new Color(0.30f, 0.30f, 0.30f);
    public Color emptyColor    = new Color(0.20f, 0.20f, 0.20f);
    public Color correctColor  = new Color(0.10f, 0.72f, 0.25f);
    public Color wrongColor    = new Color(0.85f, 0.15f, 0.15f);
    public Color savedColor    = new Color(0.95f, 0.75f, 0.10f);  // giallo = scelto + parato

    Image bg;
    string currentLabel = "";

    void Awake()
    {
        button    = button    ?? GetComponent<Button>();
        labelText = labelText ?? GetComponentInChildren<TextMeshProUGUI>();
        bg        = GetComponent<Image>();

        button.onClick.AddListener(OnClick);
        SetInteractable(false);
    }

    // ─── API PUBBLICA ────────────────────────────────────────────────────────

    public void SetLabel(string text)
    {
        currentLabel = text ?? "";
        if (labelText) labelText.text = currentLabel;

        bool empty = string.IsNullOrEmpty(currentLabel);
        SetBg(empty ? emptyColor : (button.interactable ? activeColor : disabledColor));
        button.interactable = button.interactable && !empty;
    }

    public void SetInteractable(bool value)
    {
        bool empty = string.IsNullOrEmpty(currentLabel);
        button.interactable = value && !empty;
        SetBg(empty ? emptyColor : (value ? activeColor : disabledColor));
    }

    public void SetFeedback(bool correct)
    {
        button.interactable = false;
        SetBg(correct ? correctColor : wrongColor);
    }

    /// <summary>Pannello scelto dal giocatore ma intercettato dal portiere → giallo.</summary>
    public void SetFeedbackSaved()
    {
        button.interactable = false;
        SetBg(savedColor);
    }

    // ─── CLICK ───────────────────────────────────────────────────────────────

    void OnClick()
    {
        if (!PhotonNetwork.InRoom) return;

        // In Mischia: disabilita TUTTI i pannelli (lock al primo tap), non solo questo
        // In Turni: disabilita solo questo (gli altri li gestisce ControllerManager)
        SetInteractable(false);

        var mode = ReadGameMode();
        Hashtable props;

        if (mode == GameManager.GameMode.Free4All)
        {
            // Mischia: scrivi K_Sel{myPlayerIdx} = panelIndex
            int myIdx = ResolveMyPlayerIndex();
            if (myIdx < 0) return;
            string key = myIdx == 0 ? GameManager.K_Sel0
                       : myIdx == 1 ? GameManager.K_Sel1
                       : myIdx == 2 ? GameManager.K_Sel2
                       :              GameManager.K_Sel3;
            props = new Hashtable { { key, panelIndex } };

            // Lock di tutti i pannelli del telefono locale dopo il primo tap
            if (ControllerManager.Instance != null)
                ControllerManager.Instance.LockAllPanels();
        }
        else
        {
            // Turni: comportamento classico
            props = new Hashtable
            {
                { GameManager.K_SelPanel, panelIndex                         },
                { GameManager.K_SelActor, PhotonNetwork.LocalPlayer.ActorNumber }
            };
        }

        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    static GameManager.GameMode ReadGameMode()
    {
        if (!PhotonNetwork.InRoom) return GameManager.GameMode.Turns;
        var p = PhotonNetwork.CurrentRoom.CustomProperties;
        if (p.TryGetValue(GameManager.K_GameMode, out object m) && m is string s && s == "free4all")
            return GameManager.GameMode.Free4All;
        return GameManager.GameMode.Turns;
    }

    /// <summary>Ritorna l'indice 0-3 del giocatore locale (escludendo il MasterClient).</summary>
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

    // ─── INTERNAL ────────────────────────────────────────────────────────────

    void SetBg(Color c)
    {
        if (bg) bg.color = c;
    }
}
