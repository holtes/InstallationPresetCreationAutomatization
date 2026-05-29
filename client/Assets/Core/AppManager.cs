using InteractiveClient.AssetsModule;
using InteractiveClient.Auth;
using InteractiveClient.Build;
using InteractiveClient.LLM;
using InteractiveClient.Network;
using InteractiveClient.Presets;
using InteractiveClient.Projects;
using InteractiveClient.UI.Screens.Auth;
using InteractiveClient.UI.Screens.Editor;
using InteractiveClient.UI.Screens.Preview;
using InteractiveClient.UI.Screens.Projects;
using InteractiveClient.UI.Screens.Settings;
using InteractiveClient.UI.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace InteractiveClient.Core
{
    /// <summary>
    /// Точка входа приложения. Единственный синглтон-MonoBehaviour.
    /// Вешается на GameObject в стартовой сцене вместе с UIDocument (AppRoot.uxml).
    ///
    /// Ответственность:
    ///  • инициализация ServiceLocator'а и всех сервисов;
    ///  • регистрация экранов в SceneRouter'е;
    ///  • выбор стартового экрана (Auth либо ProjectList — если токен валиден).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class AppManager : MonoBehaviour
    {
        public static AppManager Instance { get; private set; }

        [Header("Конфигурация API")]
        [SerializeField] private string apiBaseUrl = "http://localhost:8000";

        [Header("Настройки")]
        [SerializeField] private int autosaveIntervalSeconds = 60;

        private UIDocument uiDocument;
        private SceneRouter router;
        private ToastService toastService;
        private ModalService modalService;

        public string ApiBaseUrl => apiBaseUrl;
        public int AutosaveIntervalSeconds => autosaveIntervalSeconds;
        public SceneRouter Router => router;

        // ------------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            uiDocument = GetComponent<UIDocument>();

            InitializeServices();
            InitializeRouter();
            DecideStartScreen();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                router?.Dispose();
                toastService?.Dispose();
                ServiceLocator.Clear();
                EventBus.Clear();
                Instance = null;
            }
        }

        private void OnApplicationQuit()
        {
            // Финальное сохранение состояния будет добавлено позже
            // (через ProjectService.SaveAllPending()).
        }

        // ------------------------------------------------------------

        private const string ApiBaseUrlPrefKey = "iac_api_base_url";

        private void InitializeServices()
        {
            // Если пользователь переопределил API URL в Settings, используем его.
            var savedUrl = PlayerPrefs.GetString(ApiBaseUrlPrefKey, null);
            if (!string.IsNullOrEmpty(savedUrl)) apiBaseUrl = savedUrl;

            // Core-сервисы (stateless или с конфигурацией).
            var apiClient = new ApiClient(apiBaseUrl);
            ServiceLocator.Register(apiClient);

            var userSession = new UserSession();
            ServiceLocator.Register(userSession);

            // ApiClient берёт токен из UserSession через делегат
            apiClient.AuthTokenProvider = () => userSession.Token;

            // Сервисы домена
            ServiceLocator.Register(new AuthService(apiClient, userSession));
            ServiceLocator.Register(new ProjectService(apiClient));
            ServiceLocator.Register(new HardwareProfileService(apiClient));

            // Assets
            ServiceLocator.Register(new AssetUploader(apiClient));
            ServiceLocator.Register(new AssetLibrary(apiClient));
            ServiceLocator.Register(new AssetThumbnail());

            // Presets / Mapping
            ServiceLocator.Register(new PresetRegistry(apiClient));
            // MappingValidator — статический класс, регистрации не требует.

            // Build
            var buildService = new BuildService(apiClient);
            ServiceLocator.Register(buildService);
            ServiceLocator.Register(new BuildStatusTracker(buildService));

            // LLM
            ServiceLocator.Register(new LLMService(apiClient));

            Debug.Log("[AppManager] Services initialized.");
        }

        private void InitializeRouter()
        {
            var root = uiDocument.rootVisualElement;
            var screenContainer = root.Q<VisualElement>("screen-container")
                                  ?? throw new System.Exception(
                                      "[AppManager] AppRoot.uxml must contain #screen-container.");

            // UI-сервисы: toast и модалки живут поверх screen-container.
            var toastHost   = root.Q<VisualElement>("toast-host")   ?? root;
            var modalHost   = root.Q<VisualElement>("modal-host")   ?? root;
            var tooltipHost = root.Q<VisualElement>("tooltip-host") ?? root;
            toastService = new ToastService(toastHost);
            modalService = new ModalService(modalHost);
            ServiceLocator.Register(toastService);
            ServiceLocator.Register(modalService);

            // PopupMenu (контекстные меню / dropdown'ы) использует tooltip-host.
            PopupMenu.Host = tooltipHost;

            router = new SceneRouter(screenContainer);
            router.Register(new AuthScreenController());
            router.Register(new RegisterScreenController());
            router.Register(new ProjectListScreenController());
            router.Register(new EditorScreenController());
            router.Register(new SettingsScreenController());
            router.Register(new PreviewScreenController());

            Debug.Log("[AppManager] Router initialized.");
        }

        private async void DecideStartScreen()
        {
            var session = ServiceLocator.Get<UserSession>();
            session.RestoreFromStorage();

            // Локальный dev-токен (см. AuthScreenController.OnDevSkipClicked) не валидируем
            // через сервер — это просто заглушка для проверки UI.
            if (session.Token == "dev-local-token")
            {
                session.Clear();
            }

            // Сразу показываем Auth, чтобы UI не висел пустым во время сетевых retry.
            router.Navigate(ScreenId.Auth);

            // Если есть сохранённый токен — валидируем в фоне и при успехе уходим в ProjectList.
            if (session.IsAuthenticated)
            {
                try
                {
                    var ok = await ServiceLocator.Get<AuthService>().ValidateStoredTokenAsync();
                    if (ok && router.Current?.Id == ScreenId.Auth)
                        router.Navigate(ScreenId.ProjectList);
                }
                catch
                {
                    // остаёмся на экране Auth
                }
            }
        }
    }
}
