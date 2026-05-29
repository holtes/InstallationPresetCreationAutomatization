using System;
using System.Collections.Generic;

namespace InteractiveClient.Core
{
    /// <summary>
    /// Простой DI-контейнер. Сервисы регистрируются в AppManager на старте,
    /// а затем получаются по типу из любого места приложения.
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> services = new();

        /// <summary>Регистрирует сервис. Перезаписывает, если тип уже был зарегистрирован.</summary>
        public static void Register<T>(T service) where T : class
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            services[typeof(T)] = service;
        }

        /// <summary>Возвращает сервис. Бросает, если не зарегистрирован.</summary>
        public static T Get<T>() where T : class
        {
            if (services.TryGetValue(typeof(T), out var service))
                return (T)service;

            throw new InvalidOperationException(
                $"Service of type {typeof(T).Name} is not registered. " +
                "Register it in AppManager.InitializeServices().");
        }

        /// <summary>Пытается получить сервис. Возвращает false, если не зарегистрирован.</summary>
        public static bool TryGet<T>(out T service) where T : class
        {
            if (services.TryGetValue(typeof(T), out var obj))
            {
                service = (T)obj;
                return true;
            }

            service = null;
            return false;
        }

        /// <summary>Проверяет наличие сервиса.</summary>
        public static bool Has<T>() where T : class => services.ContainsKey(typeof(T));

        /// <summary>Удаляет сервис.</summary>
        public static void Unregister<T>() where T : class => services.Remove(typeof(T));

        /// <summary>Полная очистка (например, при выходе/логауте).</summary>
        public static void Clear() => services.Clear();
    }
}
