using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

public class OpenAIChat : MonoBehaviour
{
    [Header("UI")]
    public TMP_InputField inputField;
    public TMP_Text chatText;

    [Header("OpenAI")]
    [SerializeField] private string apiKey = "";
    [SerializeField] private string model = "gpt-5.4-mini";
    [TextArea]
    [SerializeField]
    private string instructions =
        "Ти корисний асистент. Відповідай українською, коротко і практично. Не виводь приховані міркування.";

    private const string url = "https://api.openai.com/v1/responses";
    private const int maxHistoryTokens = 100000;

    private readonly List<ChatMessage> history = new();

    [Serializable]
    public class ChatMessage
    {
        public string role;
        public string content;

        public ChatMessage(string role, string content)
        {
            this.role = role;
            this.content = content;
        }
    }

    [Serializable]
    public class ResponseRequest
    {
        public string model;
        public string instructions;
        public List<ChatMessage> input;
    }

    [Serializable]
    public class ResponseRoot
    {
        public OutputItem[] output;
    }

    [Serializable]
    public class OutputItem
    {
        public string type;
        public string role;
        public ContentItem[] content;
    }

    [Serializable]
    public class ContentItem
    {
        public string type;
        public string text;
    }

    public void SendFromInput()
    {
        string userText = inputField.text.Trim();
        if (string.IsNullOrEmpty(userText)) return;

        inputField.text = "";
        StartCoroutine(SendMessage(userText));
    }

    private IEnumerator SendMessage(string userText)
    {
        history.Add(new ChatMessage("user", userText));
        TrimHistory();

        chatText.text += $"\n\n<b>Ти:</b> {userText}\n<b>AI:</b> ...";

        var requestBody = new ResponseRequest
        {
            model = model,
            instructions = instructions,
            input = history
        };

        string json = JsonUtility.ToJson(requestBody);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();

        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + apiKey);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            chatText.text = chatText.text.Replace("<b>AI:</b> ...",
                "<b>AI:</b> [Помилка] " + request.downloadHandler.text);
            yield break;
        }

        string answer = ExtractAnswer(request.downloadHandler.text);

        history.Add(new ChatMessage("assistant", answer));
        TrimHistory();

        chatText.text = chatText.text.Replace("<b>AI:</b> ...",
            "<b>AI:</b> " + answer);
    }

    private string ExtractAnswer(string json)
    {
        try
        {
            ResponseRoot root = JsonUtility.FromJson<ResponseRoot>(json);

            if (root?.output == null)
                return "[Порожня відповідь]";

            foreach (var output in root.output)
            {
                if (output.content == null) continue;

                foreach (var content in output.content)
                {
                    if (content.type == "output_text" && !string.IsNullOrEmpty(content.text))
                        return content.text;
                }
            }

            return "[Не знайдено текст відповіді]";
        }
        catch
        {
            return "[Не вдалося прочитати JSON відповіді]";
        }
    }

    private void TrimHistory()
    {
        while (ApproxTokenCount(history) > maxHistoryTokens && history.Count > 1)
        {
            history.RemoveAt(0);
        }
    }

    private int ApproxTokenCount(List<ChatMessage> messages)
    {
        int chars = 0;

        foreach (var msg in messages)
            chars += msg.role.Length + msg.content.Length;

        return Mathf.CeilToInt(chars / 4f);
    }

    public void ClearHistory()
    {
        history.Clear();
        chatText.text = "";
    }
}
