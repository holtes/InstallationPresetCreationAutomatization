using System.Collections.Generic;
using InteractiveClient.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace InteractiveClient.UI.Services
{
    /// <summary>
    /// Рендерит toast-уведомления в #toast-host корневого UIDocument.
    /// Автоматически подписывается на ToastRequestEvent через EventBus.
    /// Автоматически скрывает тост через 4 сек (§8 ТЗ).
    /// </summary>
    public class ToastService
    {
        private readonly VisualElement host;
        private readonly List<VisualElement> active = new();

        private const float DefaultDurationSec = 4f;

        public ToastService(VisualElement host)
        {
            this.host = host ?? throw new System.ArgumentNullException(nameof(host));
            EventBus.Subscribe<ToastRequestEvent>(OnToastRequested);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<ToastRequestEvent>(OnToastRequested);
        }

        private void OnToastRequested(ToastRequestEvent ev) => Show(ev.Kind, ev.Message);

        public void Show(ToastRequestEvent.ToastKind kind, string message, float? durationSec = null)
        {
            var toast = new VisualElement();
            toast.AddToClassList("toast");
            toast.AddToClassList(KindClass(kind));

            var label = new Label(message);
            label.AddToClassList("toast-message");
            toast.Add(label);

            var closeBtn = new Button(() => Dismiss(toast)) { text = "✕" };
            closeBtn.AddToClassList("btn-icon");
            closeBtn.AddToClassList("btn-ghost");
            toast.Add(closeBtn);

            host.Add(toast);
            active.Add(toast);

            var duration = durationSec ?? DefaultDurationSec;
            host.schedule.Execute(() => Dismiss(toast)).StartingIn((long)(duration * 1000));
        }

        private void Dismiss(VisualElement toast)
        {
            if (toast == null || toast.parent == null) return;
            toast.RemoveFromHierarchy();
            active.Remove(toast);
        }

        private static string KindClass(ToastRequestEvent.ToastKind k) => k switch
        {
            ToastRequestEvent.ToastKind.Success => "toast-success",
            ToastRequestEvent.ToastKind.Warning => "toast-warning",
            ToastRequestEvent.ToastKind.Error => "toast-error",
            _ => "toast-info"
        };
    }

    /// <summary>Удобные шорткаты для публикации тостов.</summary>
    public static class Toast
    {
        public static void Info(string msg) =>
            EventBus.Publish(new ToastRequestEvent(ToastRequestEvent.ToastKind.Info, msg));
        public static void Success(string msg) =>
            EventBus.Publish(new ToastRequestEvent(ToastRequestEvent.ToastKind.Success, msg));
        public static void Warning(string msg) =>
            EventBus.Publish(new ToastRequestEvent(ToastRequestEvent.ToastKind.Warning, msg));
        public static void Error(string msg) =>
            EventBus.Publish(new ToastRequestEvent(ToastRequestEvent.ToastKind.Error, msg));
    }
}
