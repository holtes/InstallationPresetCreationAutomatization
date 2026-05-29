using System.Threading;
using InteractiveClient.Auth;
using InteractiveClient.Core;
using InteractiveClient.UI.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace InteractiveClient.UI.Screens.Auth
{
    /// <summary>
    /// Экран регистрации. POST /api/auth/register, при успехе автоматически
    /// логинит пользователя и переходит в ProjectList.
    /// </summary>
    public class RegisterScreenController : BaseScreenController
    {
        protected override string UxmlResourcePath => "UI/Screens/Auth/RegisterScreen";
        public override ScreenId Id => ScreenId.Register;

        private TextField nameField;
        private TextField emailField;
        private TextField passwordField;
        private TextField passwordConfirmField;
        private Button registerBtn;
        private Button goLoginBtn;
        private Button togglePasswordBtn;
        private Label errorLbl;
        private VisualElement spinner;

        private CancellationTokenSource cts;
        private bool busy;
        private bool passwordVisible;

        protected override void OnInitialize()
        {
            nameField            = Root.Q<TextField>("name-field");
            emailField           = Root.Q<TextField>("email-field");
            passwordField        = Root.Q<TextField>("password-field");
            passwordConfirmField = Root.Q<TextField>("password-confirm-field");
            registerBtn          = Root.Q<Button>("register-btn");
            goLoginBtn           = Root.Q<Button>("go-login-btn");
            togglePasswordBtn    = Root.Q<Button>("toggle-password-btn");
            errorLbl             = Root.Q<Label>("error-label");
            spinner              = Root.Q<VisualElement>("register-spinner");

            if (passwordField != null) passwordField.isPasswordField = true;
            if (passwordConfirmField != null) passwordConfirmField.isPasswordField = true;
            if (errorLbl != null) errorLbl.style.display = DisplayStyle.None;
            if (spinner != null) spinner.style.display = DisplayStyle.None;

            if (registerBtn != null) registerBtn.clicked += OnRegisterClicked;
            if (goLoginBtn != null) goLoginBtn.clicked += OnGoLoginClicked;
            if (togglePasswordBtn != null) togglePasswordBtn.clicked += OnTogglePasswordClicked;

            Root.RegisterCallback<KeyDownEvent>(ev =>
            {
                if (!busy && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter))
                    OnRegisterClicked();
            });
        }

        protected override void OnShow(object data)
        {
            ClearError();
            if (nameField != null) nameField.value = string.Empty;
            if (emailField != null) emailField.value = string.Empty;
            if (passwordField != null) passwordField.value = string.Empty;
            if (passwordConfirmField != null) passwordConfirmField.value = string.Empty;
        }

        protected override void OnHide()
        {
            cts?.Cancel();
            cts = null;
            busy = false;
        }

        private void OnGoLoginClicked()
        {
            AppManager.Instance?.Router?.Navigate(ScreenId.Auth);
        }

        private void OnTogglePasswordClicked()
        {
            if (passwordField == null) return;
            passwordVisible = !passwordVisible;
            passwordField.isPasswordField = !passwordVisible;
            if (passwordConfirmField != null) passwordConfirmField.isPasswordField = !passwordVisible;

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

        private async void OnRegisterClicked()
        {
            if (busy) return;

            var name     = nameField?.value?.Trim();
            var email    = emailField?.value?.Trim();
            var password = passwordField?.value;
            var confirm  = passwordConfirmField?.value;

            if (string.IsNullOrEmpty(name))     { ShowError("Введите имя."); return; }
            if (string.IsNullOrEmpty(email))    { ShowError("Введите email."); return; }
            if (string.IsNullOrEmpty(password)) { ShowError("Введите пароль."); return; }
            if (password.Length < 6)            { ShowError("Пароль должен быть не короче 6 символов."); return; }
            if (password != confirm)            { ShowError("Пароли не совпадают."); return; }

            SetBusy(true);
            ClearError();

            cts = new CancellationTokenSource();
            var auth = ServiceLocator.Get<AuthService>();

            try
            {
                var (ok, error) = await auth.RegisterAsync(email, name, password, cts.Token);
                if (!ok)
                {
                    ShowError(error ?? "Не удалось зарегистрироваться.");
                    return;
                }

                Toast.Success("Аккаунт создан.");
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
            if (registerBtn != null) registerBtn.SetEnabled(!b);
            if (goLoginBtn != null) goLoginBtn.SetEnabled(!b);
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
