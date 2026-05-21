using System;
using System.Collections;

/// <summary>
/// Interfaccia comune a QuestionManager (Cheshire Cat) e QuestionManager_Local.
/// GameManager dipende solo da questa — nessuna modifica al cambio di implementazione.
/// </summary>
public interface IQuestionManager
{
    IEnumerator FetchQuestion(Action<string, string[], int> onDone);
}
