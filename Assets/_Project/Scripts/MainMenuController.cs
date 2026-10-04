using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class MainMenuController : MonoBehaviour
{
    private const string FarmScenePath = "Assets/_Project/Scenes/FarmScene.unity";
    [SerializeField] private UnityEngine.UI.Button newGameButton;
    [SerializeField] private UnityEngine.UI.Button continueButton;
    [SerializeField] private UnityEngine.UI.Button settingsButton;
    [SerializeField] private UnityEngine.UI.Button quitButton;
    [SerializeField] private TMP_Text statusLabel;
    public bool IsLoading { get; private set; }
    private float nextFileCheck;

    private void OnEnable()
    {
        newGameButton?.onClick.AddListener(StartNewGame);
        continueButton?.onClick.AddListener(ContinueGame);
        settingsButton?.onClick.AddListener(ShowSettings);
        quitButton?.onClick.AddListener(QuitGame);
        RefreshButtons();
    }
    private void OnDisable()
    {
        newGameButton?.onClick.RemoveListener(StartNewGame);
        continueButton?.onClick.RemoveListener(ContinueGame);
        settingsButton?.onClick.RemoveListener(ShowSettings);
        quitButton?.onClick.RemoveListener(QuitGame);
    }
    private void Update()
    {
        if (IsLoading || Time.unscaledTime < nextFileCheck) return;
        nextFileCheck = Time.unscaledTime + .5f;
        RefreshButtons();
    }
    private void OnApplicationFocus(bool focused) { if (focused) RefreshButtons(); }
    private void RefreshButtons()
    {
        if (newGameButton != null) newGameButton.interactable = !IsLoading;
        if (continueButton != null) continueButton.interactable = !IsLoading && GameSaveSystem.HasSaveFile;
        if (settingsButton != null) settingsButton.interactable = !IsLoading;
        if (quitButton != null) quitButton.interactable = !IsLoading;
    }
    public void StartNewGame()
    {
        if (!IsLoading) StartCoroutine(EnterFarm(true));
    }
    public void ContinueGame()
    {
        if (IsLoading) return;
        if (!GameSaveSystem.HasSaveFile)
        {
            SetStatus("저장된 게임이 없습니다.");
            RefreshButtons();
            return;
        }
        StartCoroutine(EnterFarm(false));
    }
    public void ShowSettings() { if (!IsLoading) SetStatus("설정은 준비 중입니다."); }
    public void QuitGame()
    {
        if (IsLoading) return;
#if UNITY_EDITOR
        SetStatus("종료 요청: 에디터에서는 실행을 종료하지 않습니다.");
        Debug.Log("종료 요청 (Unity Editor): 빌드에서는 게임이 종료됩니다.", this);
#else
        Application.Quit();
#endif
    }
    private IEnumerator EnterFarm(bool newGame)
    {
        IsLoading = true;
        RefreshButtons();
        SetStatus(newGame ? "새 게임을 준비하고 있습니다." : "저장된 게임을 불러오고 있습니다.");
        AsyncOperation loading = null;
        try { loading = SceneManager.LoadSceneAsync(FarmScenePath, LoadSceneMode.Additive); }
        catch (System.Exception ex) { Debug.LogWarning($"농장 Scene 준비 실패: {ex.Message}", this); }
        if (loading == null)
        {
            IsLoading = false; RefreshButtons(); SetStatus("농장을 열 수 없습니다. Scene 등록을 확인해 주세요.");
            yield break;
        }
        yield return loading;
        var farm = SceneManager.GetSceneByPath(FarmScenePath);
        // Awake/Start initialize scene defaults before the existing save system replaces them.
        yield return null;
        var save = farm.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GameSaveSystem>(true)).SingleOrDefault();
        bool success = save != null && (newGame ? save.NewGame() : save.ContinueGame());
        if (!success)
        {
            yield return SceneManager.UnloadSceneAsync(farm);
            IsLoading = false; RefreshButtons();
            SetStatus(newGame ? "새 게임을 시작하지 못했습니다. 콘솔을 확인해 주세요." : "불러오기에 실패했습니다. 저장 파일을 확인해 주세요.");
            yield break;
        }
        SceneManager.SetActiveScene(farm);
        // Keep the opaque menu visible until data is ready, then reveal the farm.
        SceneManager.UnloadSceneAsync(gameObject.scene);
    }
    private void SetStatus(string message) { if (statusLabel != null) statusLabel.text = message; }
}
