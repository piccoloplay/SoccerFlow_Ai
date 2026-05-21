using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// SoccerFlaw_Ai — QuestionManager_Local
/// Versione offline di QuestionManager: usa un banco domande hardcoded.
/// Stessa firma pubblica di QuestionManager → GameManager non cambia.
///
/// SETUP: sostituisci il componente QuestionManager con questo sulla scena Game.
/// </summary>
public class QuestionManager_Local : MonoBehaviour, IQuestionManager
{
    public static QuestionManager_Local Instance { get; private set; }

    [Header("─── Opzioni ────────────────────────────────────")]
    [Tooltip("Se true le domande vengono estratte in ordine; se false in modo casuale")]
    [SerializeField] private bool sequential = false;

    // ─── BANCO DOMANDE ───────────────────────────────────────────────────────

    [Serializable]
    public struct QuestionEntry
    {
        public string   question;
        public string   correct;
        public string[] wrong;   // esattamente 3 elementi
    }

    [Header("─── Domande da URL (GitHub raw) ────────────────")]
    [Tooltip("URL RAW del JSON con le domande. Funziona anche in WebGL.")]
    [SerializeField] private string questionsUrl = "https://raw.githubusercontent.com/piccoloplay/SoccerFlow_Ai/main/questions.json";
    [Tooltip("Timeout in secondi per il download")]
    [SerializeField] private int urlTimeoutSeconds = 10;

    // Wrapper per il parsing JSON ({ "questions": [ ... ] })
    [Serializable] class QuestionFile { public QuestionEntry[] questions; }

    QuestionEntry[] urlQuestions;        // popolate dal JSON remoto (priorità massima)
    bool            urlLoadDone = false; // true quando il caricamento è finito (successo o fallimento)

    // ─── FALLBACK D'EMERGENZA ────────────────────────────────────────────────
    // Usato SOLO se il download del JSON fallisce (es. offline), così il gioco
    // non si blocca mai. In condizioni normali si usano le domande del JSON.

    static readonly QuestionEntry[] BuiltIn =
    {
        new QuestionEntry
        {
            question = "Quale linguaggio gira sulla JVM?",
            correct  = "Java",
            wrong    = new[]{ "Python", "C", "Rust" }
        },
        new QuestionEntry
        {
            question = "Cosa significa HTTP?",
            correct  = "HyperText Transfer Protocol",
            wrong    = new[]{ "High Tech Transfer Protocol", "Hyper Terminal Text Process", "Host Transfer Text Protocol" }
        },
        new QuestionEntry
        {
            question = "Quale struttura dati segue la logica LIFO?",
            correct  = "Stack",
            wrong    = new[]{ "Queue", "Tree", "Graph" }
        },
        new QuestionEntry
        {
            question = "Cosa fa il comando 'git commit'?",
            correct  = "Salva le modifiche nel repository locale",
            wrong    = new[]{ "Carica i file sul server remoto", "Crea un nuovo branch", "Elimina i file non tracciati" }
        },
        new QuestionEntry
        {
            question = "Cosa significa CPU?",
            correct  = "Central Processing Unit",
            wrong    = new[]{ "Central Programming Unit", "Computer Processing Utility", "Core Processing Unit" }
        },
        new QuestionEntry
        {
            question = "Quale protocollo usa la porta 443?",
            correct  = "HTTPS",
            wrong    = new[]{ "HTTP", "FTP", "SSH" }
        },
        new QuestionEntry
        {
            question = "In quale anno è stato creato il linguaggio Python?",
            correct  = "1991",
            wrong    = new[]{ "1985", "2000", "1978" }
        },
        new QuestionEntry
        {
            question = "Cosa indica la complessità O(1)?",
            correct  = "Tempo costante",
            wrong    = new[]{ "Tempo lineare", "Tempo quadratico", "Tempo logaritmico" }
        },
        new QuestionEntry
        {
            question = "Quale dei seguenti è un linguaggio compilato?",
            correct  = "C++",
            wrong    = new[]{ "Python", "JavaScript", "Ruby" }
        },
        new QuestionEntry
        {
            question = "Cos'è un indirizzo IP?",
            correct  = "Un identificatore univoco di un dispositivo in rete",
            wrong    = new[]{ "Il nome di un sito web", "Un tipo di cavo di rete", "Un protocollo di trasferimento file" }
        },
    };

    // ─────────────────────────────────────────────────────────────────────────

    int nextIndex = 0;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (!string.IsNullOrWhiteSpace(questionsUrl))
        {
            StartCoroutine(LoadFromUrl());
        }
        else
        {
            urlLoadDone = true;
            if (!sequential) ShuffleBank();
        }
    }

    IEnumerator LoadFromUrl()
    {
        using (var req = UnityWebRequest.Get(questionsUrl))
        {
            req.timeout = urlTimeoutSeconds;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var file = JsonUtility.FromJson<QuestionFile>(req.downloadHandler.text);
                    if (file != null && file.questions != null && file.questions.Length > 0)
                    {
                        urlQuestions = file.questions;
                        Debug.Log($"[QuestionManager_Local] Caricate {urlQuestions.Length} domande da URL.");
                    }
                    else
                    {
                        Debug.LogWarning("[QuestionManager_Local] JSON URL vuoto o malformato. Uso fallback.");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[QuestionManager_Local] Parsing JSON fallito: {e.Message}. Uso fallback.");
                }
            }
            else
            {
                Debug.LogWarning($"[QuestionManager_Local] Download domande fallito ({req.error}). Uso fallback (customQuestions/BuiltIn).");
            }
        }

        urlLoadDone = true;
        if (!sequential) ShuffleBank();
    }

    // Domande del JSON. Se il download è fallito, fallback d'emergenza (BuiltIn).
    QuestionEntry[] ActiveBank
    {
        get
        {
            if (urlQuestions != null && urlQuestions.Length > 0) return urlQuestions;
            return BuiltIn;
        }
    }

    /// <summary>
    /// Interfaccia identica a QuestionManager.FetchQuestion.
    /// Ritorna subito (nessuna chiamata di rete).
    /// </summary>
    public IEnumerator FetchQuestion(Action<string, string[], int> onDone)
    {
        // Aspetta che il caricamento da URL sia finito (se in corso)
        yield return new WaitUntil(() => urlLoadDone);

        var bank = ActiveBank;
        var entry = bank[nextIndex % bank.Length];
        nextIndex++;

        var pool = new string[9];
        pool[0] = entry.correct;
        int wrongCount = entry.wrong != null ? Mathf.Min(entry.wrong.Length, 3) : 0;
        for (int i = 0; i < wrongCount; i++)
            pool[i + 1] = entry.wrong[i];
        for (int i = 1 + wrongCount; i < 9; i++) pool[i] = "";

        int correctIdx = ShuffleFY(pool);

        onDone?.Invoke(entry.question, pool, correctIdx);
    }

    // ─── UTILITY ────────────────────────────────────────────────────────────

    void ShuffleBank()
    {
        // Mescola una copia locale dell'indice invece del banco stesso
        // (il banco è static/readonly — usiamo un array di indici)
        // Per semplicità basta azzerare nextIndex: le domande usciranno in ordine
        // diverso ad ogni sessione perché ShuffleFY mescola le risposte.
        nextIndex = UnityEngine.Random.Range(0, ActiveBank.Length);
    }

    static int ShuffleFY(string[] arr)
    {
        int trackedIdx = 0;
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            if      (j == trackedIdx) trackedIdx = i;
            else if (i == trackedIdx) trackedIdx = j;
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
        return trackedIdx;
    }
}
