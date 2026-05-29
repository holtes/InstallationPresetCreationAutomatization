using System;
using UnityEngine;

namespace InteractiveClient.Auth
{
    /// <summary>
    /// Хранит состояние авторизованного пользователя и JWT-токен.
    /// Токен персистится в PlayerPrefs (простое обфусцированное хранение —
    /// для production заменить на системный keystore / DPAPI).
    /// </summary>
    public class UserSession
    {
        private const string TokenKey = "iac_auth_token";
        private const string UserIdKey = "iac_user_id";
        private const string EmailKey = "iac_email";
        private const string NameKey = "iac_name";
        private const string RoleKey = "iac_role";

        public string Token { get; private set; }
        public string UserId { get; private set; }
        public string Email { get; private set; }
        public string Name { get; private set; }
        public string Role { get; private set; }

        public bool IsAuthenticated => !string.IsNullOrEmpty(Token);

        public event Action OnLogin;
        public event Action OnLogout;

        /// <summary>Обновляет только displayName (после PATCH /api/users/{id}).</summary>
        public void UpdateDisplayName(string newName)
        {
            Name = newName;
            PlayerPrefs.SetString(NameKey, Name ?? "");
            PlayerPrefs.Save();
        }

        public void SetAuthenticated(string token, string userId, string email, string name, string role)
        {
            Token = token;
            UserId = userId;
            Email = email;
            Name = name;
            Role = role;

            PersistToStorage();
            OnLogin?.Invoke();
        }

        public void Clear()
        {
            Token = null;
            UserId = null;
            Email = null;
            Name = null;
            Role = null;

            PlayerPrefs.DeleteKey(TokenKey);
            PlayerPrefs.DeleteKey(UserIdKey);
            PlayerPrefs.DeleteKey(EmailKey);
            PlayerPrefs.DeleteKey(NameKey);
            PlayerPrefs.DeleteKey(RoleKey);
            PlayerPrefs.Save();

            OnLogout?.Invoke();
        }

        public void RestoreFromStorage()
        {
            Token = NullIfEmpty(PlayerPrefs.GetString(TokenKey, null));
            if (!string.IsNullOrEmpty(Token))
                Token = Deobfuscate(Token);

            // PersistToStorage сохраняет null-поля как "", здесь нормализуем обратно к null,
            // чтобы оператор ?? в потребителях корректно фолбэчил на следующее значение.
            UserId = NullIfEmpty(PlayerPrefs.GetString(UserIdKey, null));
            Email  = NullIfEmpty(PlayerPrefs.GetString(EmailKey, null));
            Name   = NullIfEmpty(PlayerPrefs.GetString(NameKey, null));
            Role   = NullIfEmpty(PlayerPrefs.GetString(RoleKey, null));
        }

        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        private void PersistToStorage()
        {
            PlayerPrefs.SetString(TokenKey, Obfuscate(Token));
            PlayerPrefs.SetString(UserIdKey, UserId ?? "");
            PlayerPrefs.SetString(EmailKey, Email ?? "");
            PlayerPrefs.SetString(NameKey, Name ?? "");
            PlayerPrefs.SetString(RoleKey, Role ?? "");
            PlayerPrefs.Save();
        }

        // Простой XOR для минимальной обфускации хранимого токена.
        // Не криптография — только чтобы токен не лежал плейн-текстом в реестре.
        private static string Obfuscate(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            const string key = "iac_local_store_v1";
            var bytes = System.Text.Encoding.UTF8.GetBytes(input);
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] ^= (byte)key[i % key.Length];
            return Convert.ToBase64String(bytes);
        }

        private static string Deobfuscate(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            try
            {
                var bytes = Convert.FromBase64String(input);
                const string key = "iac_local_store_v1";
                for (int i = 0; i < bytes.Length; i++)
                    bytes[i] ^= (byte)key[i % key.Length];
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return "";
            }
        }
    }
}
