using System.Threading;
using InteractiveClient.Auth;
using InteractiveClient.Core;
using InteractiveClient.UI.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace InteractiveClient.UI.Screens.Auth
{
    /// <summary>
    /// Экран входа. Привязывает поля email/password, кнопку Login,
    /// кнопку перехода на регистрацию и переключатель видимости пароля.
    /// </summary>
    public class AuthScreenController : BaseScreenController
    {
        protected override string UxmlResourcePath => "UI/Screens/Auth/AuthScreen";
        public override ScreenId Id => ScreenId.Auth;

        private TextField emailField;
        private TextField passwordField;
        private Button loginBtn;
        private Button goRegisterBtn;
        private Button togglePasswordBtn;
        private Label errorLbl;
        private VisualElement spinner;

        private CancellationTokenSource cts;
        private bool busy;
        private bool passwordVisible;

        protected override void OnInitialize()
        {
            emailField        = Root.Q<TextField>("email-field");
            passwordField     = Root.Q<TextField>("password-field");
            loginBtn          = Root.Q<Button>("login-btn");
            goRegisterBtn     = Root.Q<Button>("go-register-btn");
            togglePasswordBtn = Root.Q<Button>("toggle-password-btn");
            errorLbl          = Root.Q<Label>("error-label");
            spinner           = Root.Q<VisualElement>("login-spinner");

            if (passwordField != null) passwordField.isPasswordField = true;
            if (errorLbl != null) errorLbl.style.display = DisplayStyle.None;
            if (spinner != null) spinner.style.display = DisplayStyle.None;

            if (loginBtn != null) loginBtn.clicked += OnLoginClicked;
            if (goRegisterBtn != null) goRegisterBtn.clicked += OnGoRegisterClicked;
            if (togglePasswordBtn != null) togglePasswordBtn.clicked += OnTogglePasswordClicked;

            // Enter для быстрого логина
            Root.RegisterCallback<KeyDownEvent>(ev =>
            {
                if (!busy && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter))
                    OnLoginClicked();
            });
        }

        protected override void OnShow(object data)
        {
            ClearError();
            if (passwordField != null) passwordField.value = string.Empty;
        }

        protected override void OnHide()
        {
            cts?.Cancel();
            cts = null;
            busy = false;
        }

        private void OnGoRegisterClicked()
        {
            AppManager.Instance?.Router?.Navigate(ScreenId.Register);
        }

        private void OnTogglePasswordClicked()
        {
            if (passwordField == null) return;
            passwordVisible = !passwordVisible;
            passwordField.isPasswordField = !passwordVisible;

            // Переключаем CSS-класс иконки: показан пароль → eye-off, скрыт → eye.
            if (togglePasswordBtn != null)
            {
                var icon = togglePasswordBtn.Q<VisualElement>(className: "login-eye-btn__icon");
                if (icon != null)
                {
                    icon.EnableInClassList("icon-eye",     !passwordVisible);
                    icon.EnableInClassList("icon-eye-off",  passwordVisible);
                }
            }
        }

        private async void OnLoginClicked()
        {
            if (busy) return;

            var email = emailField?.value?.Trim();
            var password = passwordField?.value;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ShowError("Введите email и пароль.");
                return;
            }

            SetBusy(true);
            ClearError();

            cts = new CancellationTokenSource();
            var auth = ServiceLocator.Get<AuthService>();

            try
            {
                var ok = await auth.LoginAsync(email, password, cts.Token);
                if (!ok)
                {
                    ShowError("Неверный email или пароль.");
                    return;
                }

                AppManager.Instance?.Router?.Navigate(ScreenId.ProjectList);
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                ShowError("Ошибка сети. Попробуйте ещё раз.");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool b)
        {
            busy = b;
            if (loginBtn != null) loginBtn.SetEnabled(!b);
            if (goRegisterBtn != null) goRegisterBtn.SetEnabled(!b);
            if (spinner != null) spinner.style.display = b ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ShowError(string msg)
        {
            if (errorLbl == null) { Toast.Error(msg); return; }
            errorLbl.text = msg;
            errorLbl.style.display = DisplayStyle.Flex;
            errorLbl.RemoveFromClassList("hidden");
        }

        private void ClearError()
        {
            if (errorLbl != null)
            {
                errorLbl.text = string.Empty;
                errorLbl.style.display = DisplayStyle.None;
            }
        }
    }
}
