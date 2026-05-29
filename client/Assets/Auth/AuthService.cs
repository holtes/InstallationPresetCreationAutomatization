using System.Threading;
using System.Threading.Tasks;
using InteractiveClient.Core;
using InteractiveClient.Network;
using UnityEngine;

namespace InteractiveClient.Auth
{
    /// <summary>Авторизация и логаут через бэкенд.</summary>
    public class AuthService
    {
        private readonly ApiClient api;
        private readonly UserSession session;

        public AuthService(ApiClient api, UserSession session)
        {
            this.api = api;
            this.session = session;
        }

        public async Task<bool> LoginAsync(string email, string password, CancellationToken ct = default)
        {
            try
            {
                var resp = await api.PostAsync<LoginRequest, LoginResponse>(
                    ApiEndpoints.Login,
                    new LoginRequest { Email = email, Password = password },
                    ct);

                if (resp == null || string.IsNullOrEmpty(resp.AccessToken))
                {
                    Debug.LogWarning("[AuthService] Login returned empty token.");
                    return false;
                }

                session.SetAuthenticated(
                    resp.AccessToken,
                    resp.User?.Id,
                    resp.User?.Email ?? email,
                    resp.User?.Name,
                    resp.User?.Role);

                Debug.Log($"[AuthService] Access token: {resp.AccessToken}");
                EventBus.Publish(new UserLoggedInEvent(session.UserId, session.Email));
                return true;
            }
            catch (ApiException ex)
            {
                Debug.LogWarning($"[AuthService] Login failed: {ex.StatusCode} {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Регистрация нового пользователя и автоматический логин.
        /// Бэкенд POST /api/auth/register возвращает UserOut (без токена),
        /// после чего сразу делаем логин с теми же кредами.
        /// </summary>
        public async Task<(bool ok, string error)> RegisterAsync(
            string email, string displayName, string password, CancellationToken ct = default)
        {
            try
            {
                await api.PostAsync<RegisterRequest, UserDto>(
                    ApiEndpoints.Register,
                    new RegisterRequest
                    {
                        Email = email,
                        DisplayName = displayName,
                        Password = password,
                        Role = "editor"
                    },
                    ct);
            }
            catch (ApiException ex)
            {
                Debug.LogWarning($"[AuthService] Register failed: {ex.StatusCode} {ex.Message}");
                if (ex.StatusCode == 400)
                    return (false, "Email уже зарегистрирован.");
                return (false, "Не удалось создать аккаунт.");
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                return (false, "Ошибка сети.");
            }

            var loggedIn = await LoginAsync(email, password, ct);
            return loggedIn ? (true, null) : (false, "Аккаунт создан, но войти не удалось.");
        }

        /// <summary>
        /// Обновление профиля текущего пользователя (display name).
        /// Использует PATCH /api/users/{id} (бэк), при успехе обновляет UserSession.
        /// </summary>
        public async Task<(bool ok, string error)> UpdateDisplayNameAsync(
            string newDisplayName, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(session.UserId))
                return (false, "Сессия не инициализирована.");

            try
            {
                var dto = await api.PatchAsync<UserUpdateRequest, UserDto>(
                    ApiEndpoints.UserById(session.UserId),
                    new UserUpdateRequest { DisplayName = newDisplayName },
                    ct);

                var resolved = dto?.DisplayName ?? dto?.Name ?? newDisplayName;
                session.UpdateDisplayName(resolved);
                return (true, null);
            }
            catch (ApiException ex)
            {
                Debug.LogWarning($"[AuthService] UpdateDisplayName failed: {ex.StatusCode} {ex.Message}");
                return (false, "Не удалось обновить профиль.");
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                return (false, "Ошибка сети.");
            }
        }

        public async Task LogoutAsync(CancellationToken ct = default)
        {
            try
            {
                await api.PostAsync<object>(ApiEndpoints.Logout, null, ct);
            }
            catch { /* server-side logout is best-effort */ }

            session.Clear();
            EventBus.Publish(new UserLoggedOutEvent());
        }

        /// <summary>
        /// Проверяет валидность сохранённого токена через /api/auth/me.
        /// Возвращает true, если токен валиден.
        /// </summary>
        public async Task<bool> ValidateStoredTokenAsync(CancellationToken ct = default)
        {
            if (!session.IsAuthenticated) return false;

            try
            {
                var me = await api.GetAsync<UserDto>(ApiEndpoints.Me, ct);
                return me != null && !string.IsNullOrEmpty(me.Id);
            }
            catch (ApiException ex) when (ex.IsUnauthorized)
            {
                session.Clear();
                return false;
            }
            catch
            {
                // Сетевая ошибка — не удаляем токен.
                return session.IsAuthenticated;
            }
        }
    }
}
