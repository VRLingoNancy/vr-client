using UnityEngine;
using UnityEngine.UI;

public class ConversationEndUI : MonoBehaviour
{
    public Button endButton;
    public Selectable[] settingsToLock;

    private bool locked = false;

    void Start()
    {
        if (endButton == null) return;
        endButton.gameObject.SetActive(false);
        endButton.onClick.AddListener(OnEndClicked);
    }

    void Update()
    {
        if (string.IsNullOrEmpty(VRLingoClient.CurrentConversationId)) return;

        // Bouton "Terminer la session" masqué : on ne le ré-affiche plus
        // quand une conversation démarre.

        if (!locked && settingsToLock != null)
        {
            foreach (var s in settingsToLock)
                if (s != null) s.interactable = false;
            locked = true;
        }
    }

    async void OnEndClicked()
    {
        endButton.interactable = false;

        var id = VRLingoClient.CurrentConversationId;
        if (string.IsNullOrEmpty(id))
        {
            VRLingoClient.PostSystemMessage("No conversation to score yet.");
            endButton.interactable = true;
            return;
        }

        VRLingoClient.PostSystemMessage("Scoring...");
        var result = await ConversationScoreApi.GetScore(id);
        if (result == null)
            VRLingoClient.PostSystemMessage("Failed to fetch score.");
        else
            VRLingoClient.PostSystemMessage($"Score: {result.aiScore}/100 — {result.aiFeedback}");

        endButton.interactable = true;
    }
}
