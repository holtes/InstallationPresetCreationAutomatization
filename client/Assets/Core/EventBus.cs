using System;
using System.Collections.Generic;
using UnityEngine;

namespace InteractiveClient.Core
{
    /// <summary>
    /// Глобальная шина событий. Типизированная публикация/подписка.
    /// Каждое событие — собственный struct/класс (payload).
    ///
    /// Использование:
    ///   EventBus.Subscribe&lt;ProjectSavedEvent&gt;(OnProjectSaved);
    ///   EventBus.Publish(new ProjectSavedEvent { ProjectId = id });
    ///   EventBus.Unsubscribe&lt;ProjectSavedEvent&gt;(OnProjectSaved);
    /// </summary>
    public static class EventBus
    {
        private static readonly Dictionary<Type, Delegate> handlers = new();

        public static void Subscribe<T>(Action<T> handler)
        {
            if (handler == null) return;

            if (handlers.TryGetValue(typeof(T), out var existing))
                handlers[typeof(T)] = Delegate.Combine(existing, handler);
            else
                handlers[typeof(T)] = handler;
        }

        public static void Unsubscribe<T>(Action<T> handler)
        {
            if (handler == null) return;

            if (!handlers.TryGetValue(typeof(T), out var existing))
                return;

            var remaining = Delegate.Remove(existing, handler);
            if (remaining == null)
                handlers.Remove(typeof(T));
            else
                handlers[typeof(T)] = remaining;
        }

        public static void Publish<T>(T payload)
        {
            if (!handlers.TryGetValue(typeof(T), out var existing))
                return;

            // Защита: исключение в одном обработчике не должно останавливать остальные.
            foreach (var d in existing.GetInvocationList())
            {
                try
                {
                    ((Action<T>)d).Invoke(payload);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        public static void Clear() => handlers.Clear();
    }

    // ============================================================
    // Стандартные события приложения
    // ============================================================

    public readonly struct UserLoggedInEvent
    {
        public readonly string UserId;
        public readonly string Email;
        public UserLoggedInEvent(string userId, string email) { UserId = userId; Email = email; }
    }

    public readonly struct UserLoggedOutEvent { }

    public readonly struct ProjectOpenedEvent
    {
        public readonly string ProjectId;
        public ProjectOpenedEvent(string projectId) { ProjectId = projectId; }
    }

    public readonly struct ProjectSavedEvent
    {
        public readonly string ProjectId;
        public readonly bool Success;
        public ProjectSavedEvent(string projectId, bool success) { ProjectId = projectId; Success = success; }
    }

    public readonly struct AssetUploadedEvent
    {
        public readonly string AssetId;
        public AssetUploadedEvent(string assetId) { AssetId = assetId; }
    }

    public readonly struct PresetSelectedEvent
    {
        public readonly string PresetId;
        public PresetSelectedEvent(string presetId) { PresetId = presetId; }
    }

    public readonly struct StepChangedEvent
    {
        public readonly int FromStep;
        public readonly int ToStep;
        public StepChangedEvent(int from, int to) { FromStep = from; ToStep = to; }
    }

    public readonly struct ToastRequestEvent
    {
        public enum ToastKind { Info, Success, Warning, Error }
        public readonly ToastKind Kind;
        public readonly string Message;
        public ToastRequestEvent(ToastKind kind, string message) { Kind = kind; Message = message; }
    }
}
