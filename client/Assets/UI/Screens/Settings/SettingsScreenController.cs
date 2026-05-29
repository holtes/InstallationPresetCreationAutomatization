using System;
using InteractiveClient.Auth;
using InteractiveClient.Core;
using InteractiveClient.UI.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace InteractiveClient.UI.Screens.Settings
{
    /// <summary>
    /// Экран настроек: профиль (display name, email/role read-only),
    /// смена пароля (заглушка — нет серверного эндпоинта),
    /// настройки подключения (API base URL),
    /// информация о приложении.
    /// </summary>
    public class SettingsScreenController : BaseScreenController
    {
        protected override string UxmlResourcePath => "UI/Screens/Settings/SettingsScreen";
        public override ScreenId Id => ScreenId.Settings;

        private const string ApiBaseUrlPrefKey = "iac_api_base_url";
        private const string DefaultApiBaseUrl = "http://localhost:8000";

        private Button backBtn;
        private TextField displayNameField;
        private TextField emailField;
        private TextField roleField;
        private Button saveProfileBtn;

        private TextField apiUrlField;
        private Button saveApiBtn;
        private Button resetApiBtn;

        private Label clientVersionLbl;
        private Label currentApiLbl;
        private Label unityVersionLbl;

        protected override void OnInitialize()
        {
            backBtn = Root.Q<Button>("back-btn");

            displayNameField = Root.Q<TextField>("display-name-field");
            emailField       = Root.Q<TextField>("email-field");
            roleField        = Root.Q<TextField>("role-field");
            saveProfileBtn   = Root.Q<Button>("save-profile-btn");

            apiUrlField  = Root.Q<TextField>("api-url-field");
            saveApiBtn   = Root.Q<Button>("save-api-btn");
            resetApiBtn  = Root.Q<Button>("reset-api-btn");

            clientVersionLbl = Root.Q<Label>("client-version");
            currentApiLbl    = Root.Q<Label>("current-api");
            unityVersionLbl  = Root.Q<Label>("unity-version");

            // Read-only fields — TextField не имеет атрибута readonly, ставим через isReadOnly.
            if (emailField != null) emailField.isReadOnly = true;
            if (roleField  != null) roleField.isReadOnly  = true;

            if (backBtn        != null) backBtn.clicked        += OnBackClicked;
            if (saveProfileBtn != null) saveProfileBtn.clicked += OnSaveProfileClicked;
            if (saveApiBtn     != null) saveApiBtn.clicked     += OnSaveApiClicked;
            if (resetApiBtn    != null) resetApiBtn.clicked    += OnResetApiClicked;
        }

        protected override void OnShow(object data)
        {
            // Profile
            var session = ServiceLocator.Get<UserSession>();
            if (displayNameField != null) displayNameField.value = session.Name ?? "";
            if (emailField       != null) emailField.value       = session.Email ?? "";
            if (roleField        != null) roleField.value        = session.Role ?? "";

            // API URL
            var saved = PlayerPrefs.GetString(ApiBaseUrlPrefKey, "");
            if (apiUrlField != null)
                apiUrlField.value = string.IsNullOrEmpty(saved)
                    ? (AppManager.Instance?.ApiBaseUrl ?? DefaultApiBaseUrl)
                    : saved;

            // About
            if (clientVersionLbl != null) clientVersionLbl.text = Application.version;
            if (currentApiLbl    != null) currentApiLbl.text    = AppManager.Instance?.ApiBaseUrl ?? DefaultApiBaseUrl;
            if (unityVersionLbl  != null) unityVersionLbl.text  = Application.unityVersion;
        }

        // ----------------------------------------------------------

        private void OnBackClicked()
        {
            AppManager.Instance?.Router?.Navigate(ScreenId.ProjectList);
        }

        private async void OnSaveProfileClicked()
        {
            var newName = displayNameField?.value?.Trim();
            if (string.IsNullOrEmpty(newName))
            {
                Toast.Warning("Имя не может быть пустым.");
                return;
            }

            saveProfileBtn?.SetEnabled(false);
            try
            {
                var auth = ServiceLocator.Get<AuthService>();
                var (ok, error) = await auth.UpdateDisplayNameAsync(newName);
                if (ok) Toast.Success("Профиль обновлён.");
                else    Toast.Error(error ?? "Не удалось обновить профиль.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Toast.Error("Ошибка при обновлении профиля.");
            }
            finally
            {
                saveProfileBtn?.SetEnabled(true);
            }
        }

        private void OnSaveApiClicked()
        {
            var url = apiUrlField?.value?.Trim();
            if (string.IsNullOrEmpty(url))
            {
                Toast.Warning("Введите URL.");
                return;
            }
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                Toast.Warning("URL должен начинаться с http:// или https://");
                return;
            }

            PlayerPrefs.SetString(ApiBaseUrlPrefKey, url.TrimEnd('/'));
            PlayerPrefs.Save();
            Toast.Success("Сохранено. Перезапустите приложение, чтобы применить.");
        }

        private void OnResetApiClicked()
        {
            PlayerPrefs.DeleteKey(ApiBaseUrlPrefKey);
            PlayerPrefs.Save();
            if (apiUrlField != null) apiUrlField.value = DefaultApiBaseUrl;
            Toast.Info("Сброшено к значению по умолчанию.");
        }
    }
}
