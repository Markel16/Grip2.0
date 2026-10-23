using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class MainMenuController : MonoBehaviour
{
    [Header("Existing Menu Objects")]
    [SerializeField] private GameObject[] mainMenuElements;
    [SerializeField] private GameObject circuitMenuPanel;
    [SerializeField] private GameObject trackSelectionPanel;

    [Header("Existing Buttons")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button openCircuitMenuButton;
    [SerializeField] private Button openTrackSelectionButton;
    [SerializeField] private Button backButton;
    [SerializeField] private Button exitButton;

    [Header("Track Selection")]
    [SerializeField] private TrackCarouselController trackCarousel;
    [SerializeField] private string defaultTrackSceneName = "CIrcuito1";

    private void Awake()
    {
        ShowMainMenu();
    }

    private void OnEnable()
    {
        AddListener(playButton, StartSelectedTrack);
        AddListener(openCircuitMenuButton, OpenCircuitMenu);
        AddListener(openTrackSelectionButton, OpenTrackSelection);
        AddListener(backButton, GoBack);
        AddListener(exitButton, ExitGame);
    }

    private void OnDisable()
    {
        RemoveListener(playButton, StartSelectedTrack);
        RemoveListener(openCircuitMenuButton, OpenCircuitMenu);
        RemoveListener(openTrackSelectionButton, OpenTrackSelection);
        RemoveListener(backButton, GoBack);
        RemoveListener(exitButton, ExitGame);
    }

    public void ShowMainMenu()
    {
        SetMainMenuElementsActive(true);
        SetActive(circuitMenuPanel, false);
        SetActive(trackSelectionPanel, false);
    }

    public void OpenCircuitMenu()
    {
        SetMainMenuElementsActive(false);
        SetActive(circuitMenuPanel, true);
        SetActive(trackSelectionPanel, false);
    }

    public void OpenTrackSelection()
    {
        if (circuitMenuPanel == null || trackSelectionPanel == null)
        {
            Debug.LogWarning("Assign the circuit and track-selection panels in the Inspector.", this);
            return;
        }

        SetMainMenuElementsActive(false);
        SetActive(circuitMenuPanel, true);
        SetActive(trackSelectionPanel, true);
    }

    public void GoBack()
    {
        if (trackSelectionPanel != null && trackSelectionPanel.activeSelf)
        {
            SetActive(trackSelectionPanel, false);
            return;
        }

        ShowMainMenu();
    }

    public void StartSelectedTrack()
    {
        string sceneName = trackCarousel != null && !string.IsNullOrWhiteSpace(trackCarousel.SelectedSceneName)
            ? trackCarousel.SelectedSceneName
            : defaultTrackSceneName;

        if (string.IsNullOrWhiteSpace(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"Track scene '{sceneName}' is not enabled in Build Settings.", this);
            return;
        }

        SceneManager.LoadScene(sceneName);
    }

    public void ExitGame()
    {
        Application.Quit();
    }

    private void SetMainMenuElementsActive(bool isActive)
    {
        if (mainMenuElements == null)
        {
            return;
        }

        foreach (GameObject menuElement in mainMenuElements)
        {
            SetActive(menuElement, isActive);
        }
    }

    private static void SetActive(GameObject target, bool isActive)
    {
        if (target != null)
        {
            target.SetActive(isActive);
        }
    }

    private static void AddListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
        {
            button.onClick.AddListener(action);
        }
    }

    private static void RemoveListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
        {
            button.onClick.RemoveListener(action);
        }
    }
}