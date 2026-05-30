using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.UI;

public class AIGameChat : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text storyText;

    public Button choiceButton1;
    public Button choiceButton2;
    public Button choiceButton3;

    public TMP_Text choiceText1;
    public TMP_Text choiceText2;
    public TMP_Text choiceText3;

    [Header("OpenAI")]
    [SerializeField] private string apiKey = "";
    [SerializeField] private string model = "gpt-5.4-mini";

    private const string url = "https://api.openai.com/v1/responses";
    private const int maxHistoryTokens = 100000;

    private readonly List<ChatMessage> history = new();

    private string[] currentChoices = new string[3];

    private string instructions =
        "Ти ведеш текстову пригодницьку гру українською мовою. " +
        "Гравець має кожного ходу бачити короткий опис ситуації і рівно 3 варіанти дії. " +
        "Не пиши приховані міркування. " +
        "Відповідай строго у такому форматі:\n" +
        "STORY: текст сцени\n" +
        "CHOICE_1: перший варіант\n" +
        "CHOICE_2: другий варіант\n" +
        "CHOICE_3: третій варіант";

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

    private void Start()
    {
        choiceButton1.onClick.AddListener(() => Choose(0));
        choiceButton2.onClick.AddListener(() => Choose(1));
        choiceButton3.onClick.AddListener(() => Choose(2));

        StartNewGame();
    }

    public void StartNewGame()
    {
        history.Clear();

        SetButtonsInteractable(false);

        string startPrompt =
            "Почни нову пригодницьку гру. Жанр: фентезі. " +
            "Гравець прокидається у незнайомому місці. " +
            "Згенеруй першу сцену і 3 варіанти ходу.";

        StartCoroutine(SendToAI(startPrompt));
    }

    private void Choose(int choiceIndex)
    {
        string selectedChoice = currentChoices[choiceIndex];

        SetButtonsInteractable(false);

        string prompt =
            "Гравець обрав дію: " + selectedChoice + "\n" +
            "Продовж історію з наслідками цього вибору. " +
            "Згенеруй нову сцену і рівно 3 нові варіанти ходу.";

        StartCoroutine(SendToAI(prompt));
    }

    private IEnumerator SendToAI(string userText)
    {
        storyText.text = "Генерація...";

        history.Add(new ChatMessage("user", userText));
        TrimHistory();

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
            storyText.text = "Помилка API:\n" + request.downloadHandler.text;
            yield break;
        }

        string aiText = ExtractAnswer(request.downloadHandler.text);

        history.Add(new ChatMessage("assistant", aiText));
        TrimHistory();

        ApplyAIResponse(aiText);
    }

    private string ExtractAnswer(string json)
    {
        try
        {
            ResponseRoot root = JsonUtility.FromJson<ResponseRoot>(json);

            if (root?.output == null)
                return "";

            foreach (var output in root.output)
            {
                if (output.content == null) continue;

                foreach (var content in output.content)
                {
                    if (content.type == "output_text" && !string.IsNullOrEmpty(content.text))
                        return content.text;
                }
            }

            return "";
        }
        catch
        {
            return "";
        }
    }

    private void ApplyAIResponse(string text)
    {
        string story = GetValue(text, "STORY:", "CHOICE_1:");
        string choice1 = GetValue(text, "CHOICE_1:", "CHOICE_2:");
        string choice2 = GetValue(text, "CHOICE_2:", "CHOICE_3:");
        string choice3 = GetValue(text, "CHOICE_3:", null);

        if (string.IsNullOrWhiteSpace(story))
            story = text;

        if (string.IsNullOrWhiteSpace(choice1)) choice1 = "Оглянутися";
        if (string.IsNullOrWhiteSpace(choice2)) choice2 = "Піти вперед";
        if (string.IsNullOrWhiteSpace(choice3)) choice3 = "Зачекати";

        storyText.text = story.Trim();

        currentChoices[0] = choice1.Trim();
        currentChoices[1] = choice2.Trim();
        currentChoices[2] = choice3.Trim();

        choiceText1.text = currentChoices[0];
        choiceText2.text = currentChoices[1];
        choiceText3.text = currentChoices[2];

        SetButtonsInteractable(true);
    }

    private string GetValue(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);

        if (start == -1)
            return "";

        start += startMarker.Length;

        int end;

        if (endMarker == null)
            end = source.Length;
        else
            end = source.IndexOf(endMarker, start, StringComparison.Ordinal);

        if (end == -1)
            end = source.Length;

        return source.Substring(start, end - start);
    }

    private void SetButtonsInteractable(bool value)
    {
        choiceButton1.interactable = value;
        choiceButton2.interactable = value;
        choiceButton3.interactable = value;
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
        {
            chars += msg.role.Length;
            chars += msg.content.Length;
        }

        return Mathf.CeilToInt(chars / 4f);
    }
}
