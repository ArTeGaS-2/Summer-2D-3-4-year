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
    public Button choiceButton4;

    public TMP_Text choiceText1;
    public TMP_Text choiceText2;
    public TMP_Text choiceText3;
    public TMP_Text choiceText4;

    [Header("Game State")]
    [SerializeField] private GameStateStore gameState;

    [Header("OpenAI")]
    [SerializeField] private string apiKey = "";
    [SerializeField] private string model = "gpt-5.4-mini";

    private const string url = "https://api.openai.com/v1/responses";
    private const int maxHistoryTokens = 100000;

    private readonly List<ChatMessage> history = new();

    private readonly string[] currentChoices = new string[4];
    private bool isChoosingOpening;

    private readonly string storyInstructions =
        "Ти ведеш текстову пригодницьку гру українською мовою. " +
        "Пиши тільки короткий опис поточної сцени, без варіантів дій. " +
        "Враховуй JSON стану гри, але не показуй його як службові дані. " +
        "Не пиши приховані міркування.";

    private readonly string choiceInstructions =
        "Ти генеруєш рівно 4 варіанти дії для текстової пригодницької гри українською. " +
        "Варіанти мають бути короткі, різні за наміром і прив'язані до поточної сцени. " +
        "Не пиши приховані міркування. " +
        "Відповідай строго у форматі:\n" +
        "CHOICE_1: перший варіант\n" +
        "CHOICE_2: другий варіант\n" +
        "CHOICE_3: третій варіант\n" +
        "CHOICE_4: четвертий варіант";

    private readonly string effectInstructions =
        "Ти перевіряєш, чи подія в текстовій пригоді змінює стан гри. " +
        "Стан гри зберігається як словник key/value у JSON. " +
        "Основні ключі: location, time, hit_points, mana_points, stamina_points, strength, agility, intelligence. " +
        "Можна додавати нові характеристики, якщо вони справді потрібні сцені. " +
        "Якщо змін немає, поверни порожній масив changes. " +
        "Відповідай тільки валідним JSON без Markdown у форматі: " +
        "{\"changes\":[{\"key\":\"location\",\"value\":\"Нова локація\"}],\"summary\":\"коротко\"}";

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

    [Serializable]
    public class EffectResponse
    {
        public GameStateStore.StateEntry[] changes;
        public string summary;
    }

    private void Awake()
    {
        if (gameState == null)
        {
            gameState = GetComponent<GameStateStore>();
        }

        if (gameState == null)
        {
            gameState = gameObject.AddComponent<GameStateStore>();
        }

        AutoBindMissingChoices();
    }

    private void Start()
    {
        AddChoiceListener(choiceButton1, 0);
        AddChoiceListener(choiceButton2, 1);
        AddChoiceListener(choiceButton3, 2);
        AddChoiceListener(choiceButton4, 3);

        StartNewGame();
    }

    public void StartNewGame()
    {
        history.Clear();
        isChoosingOpening = true;

        storyText.text =
            "Обери початок фентезі RPG:\n\n" +
            "1. Ліс прокидається і шепоче твоє ім'я.\n" +
            "2. У місті магів зникає останній кристал світла.\n" +
            "3. Караван знаходить карту до підземного королівства.\n" +
            "4. Дракон просить допомоги у людини.";

        SetChoice(0, "Пробудження у зачарованому лісі");
        SetChoice(1, "Розслідування у місті магів");
        SetChoice(2, "Похід у підземне королівство");
        SetChoice(3, "Союз із пораненим драконом");
        SetButtonsInteractable(true);
    }

    private void Choose(int choiceIndex)
    {
        if (choiceIndex < 0 || choiceIndex >= currentChoices.Length)
        {
            return;
        }

        string selectedChoice = currentChoices[choiceIndex];
        if (string.IsNullOrWhiteSpace(selectedChoice))
        {
            return;
        }

        SetButtonsInteractable(false);

        string prompt;
        if (isChoosingOpening)
        {
            isChoosingOpening = false;
            history.Clear();
            prompt =
                "Почни нову фентезі RPG з такого старту: " + selectedChoice + "\n" +
                "Дай першу сцену, де гравець уже може діяти.\n" +
                "Поточний стан гри: " + gameState.ToJson();
        }
        else
        {
            prompt =
                "Гравець обрав дію: " + selectedChoice + "\n" +
                "Продовж історію з наслідками цього вибору.\n" +
                "Поточний стан гри: " + gameState.ToJson();
        }

        StartCoroutine(RunTurn(prompt, selectedChoice));
    }

    private IEnumerator RunTurn(string userText, string selectedChoice)
    {
        storyText.text = "Генерація історії...";

        history.Add(new ChatMessage("user", userText));
        TrimHistory();

        string story = "";
        yield return SendToAI(history, storyInstructions, text => story = text);

        if (string.IsNullOrWhiteSpace(story))
        {
            storyText.text = "AI повернув порожню історію.";
            SetButtonsInteractable(true);
            yield break;
        }

        history.Add(new ChatMessage("assistant", story));
        TrimHistory();

        storyText.text = "Генерація варіантів...";

        List<ChatMessage> choiceInput = new()
        {
            new ChatMessage("user",
                "Сцена:\n" + story + "\n\nПоточний стан гри:\n" + gameState.ToJson())
        };

        string choiceText = "";
        yield return SendToAI(choiceInput, choiceInstructions, text => choiceText = text);
        ApplyChoices(choiceText);

        storyText.text = "Перевірка змін стану...";

        List<ChatMessage> effectInput = new()
        {
            new ChatMessage("user",
                "Попередній вибір гравця: " + selectedChoice + "\n\n" +
                "Нова сцена:\n" + story + "\n\n" +
                "Поточний стан гри:\n" + gameState.ToJson())
        };

        string effectJson = "";
        yield return SendToAI(effectInput, effectInstructions, text => effectJson = text);
        ApplyStateEffects(effectJson);

        storyText.text = story.Trim();
        SetButtonsInteractable(true);
    }

    private IEnumerator SendToAI(List<ChatMessage> input, string instructions, Action<string> onSuccess)
    {
        var requestBody = new ResponseRequest
        {
            model = model,
            instructions = instructions,
            input = input
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

        onSuccess?.Invoke(ExtractAnswer(request.downloadHandler.text));
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

    private void ApplyChoices(string text)
    {
        string choice1 = GetValue(text, "CHOICE_1:", "CHOICE_2:");
        string choice2 = GetValue(text, "CHOICE_2:", "CHOICE_3:");
        string choice3 = GetValue(text, "CHOICE_3:", "CHOICE_4:");
        string choice4 = GetValue(text, "CHOICE_4:", null);

        if (string.IsNullOrWhiteSpace(choice1)) choice1 = "Оглянутися";
        if (string.IsNullOrWhiteSpace(choice2)) choice2 = "Піти вперед";
        if (string.IsNullOrWhiteSpace(choice3)) choice3 = "Зачекати";
        if (string.IsNullOrWhiteSpace(choice4)) choice4 = "Поговорити";

        SetChoice(0, choice1.Trim());
        SetChoice(1, choice2.Trim());
        SetChoice(2, choice3.Trim());
        SetChoice(3, choice4.Trim());
    }

    private void ApplyStateEffects(string text)
    {
        string json = ExtractJsonObject(text);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            EffectResponse effect = JsonUtility.FromJson<EffectResponse>(json);
            gameState.ApplyChanges(effect?.changes);
        }
        catch
        {
            Debug.LogWarning("Не вдалося прочитати JSON змін стану: " + text);
        }
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
        SetButtonInteractable(choiceButton1, value);
        SetButtonInteractable(choiceButton2, value);
        SetButtonInteractable(choiceButton3, value);
        SetButtonInteractable(choiceButton4, value);
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

    private string ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');

        if (start == -1 || end == -1 || end <= start)
        {
            return "";
        }

        return text.Substring(start, end - start + 1);
    }

    private void AddChoiceListener(Button button, int choiceIndex)
    {
        if (button != null)
        {
            button.onClick.AddListener(() => Choose(choiceIndex));
        }
    }

    private void SetButtonInteractable(Button button, bool value)
    {
        if (button != null)
        {
            button.interactable = value;
        }
    }

    private void SetChoice(int index, string text)
    {
        if (index < 0 || index >= currentChoices.Length)
        {
            return;
        }

        currentChoices[index] = text;
        TMP_Text label = GetChoiceText(index);
        if (label != null)
        {
            label.text = text;
        }
    }

    private TMP_Text GetChoiceText(int index)
    {
        return index switch
        {
            0 => choiceText1,
            1 => choiceText2,
            2 => choiceText3,
            3 => choiceText4,
            _ => null
        };
    }

    private void AutoBindMissingChoices()
    {
        BindChoiceIfMissing("Choose_1", ref choiceButton1, ref choiceText1);
        BindChoiceIfMissing("Choose_2", ref choiceButton2, ref choiceText2);
        BindChoiceIfMissing("Choose_3", ref choiceButton3, ref choiceText3);
        BindChoiceIfMissing("Choose_4", ref choiceButton4, ref choiceText4);
    }

    private void BindChoiceIfMissing(string objectName, ref Button button, ref TMP_Text label)
    {
        GameObject found = GameObject.Find(objectName);
        if (found == null)
        {
            return;
        }

        if (button == null)
        {
            button = found.GetComponent<Button>();
        }

        if (label == null)
        {
            label = found.GetComponentInChildren<TMP_Text>();
        }
    }
}
