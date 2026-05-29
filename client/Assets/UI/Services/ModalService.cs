using System;
using UnityEngine.UIElements;

namespace InteractiveClient.UI.Services
{
    /// <summary>
    /// Показывает модальные окна в #modal-host корневого UIDocument.
    /// Поддерживает programmatic ConfirmDialog и произвольный контент.
    /// </summary>
    public class ModalService
    {
        private readonly VisualElement host;

        public ModalService(VisualElement host)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
        }

        /// <summary>Простой да/нет диалог. onConfirm/onCancel могут быть null.</summary>
        public void Confirm(string title, string message,
            string confirmText = "ОК", string cancelText = "Отмена",
            Action onConfirm = null, Action onCancel = null)
        {
            Show(title, root =>
            {
                var lbl = new Label(message);
                lbl.AddToClassList("body-text");
                root.Add(lbl);
            },
            footer =>
            {
                var cancelBtn = new Button(() => { Close(); onCancel?.Invoke(); }) { text = cancelText };
                cancelBtn.AddToClassList("btn");
                cancelBtn.AddToClassList("btn-secondary");

                var confirmBtn = new Button(() => { Close(); onConfirm?.Invoke(); }) { text = confirmText };
                confirmBtn.AddToClassList("btn");
                confirmBtn.AddToClassList("btn-primary");

                footer.Add(cancelBtn);
                footer.Add(confirmBtn);
            });
        }

        /// <summary>
        /// Показ модалки с кастомным body и footer.
        /// buildBody/buildFooter получают контейнеры, в которые надо положить контент.
        /// </summary>
        public void Show(string title, Action<VisualElement> buildBody, Action<VisualElement> buildFooter = null)
        {
            Close();
            host.RemoveFromClassList("hidden");

            var overlay = new VisualElement { name = "modal-overlay" };
            overlay.AddToClassList("modal-overlay");

            var modal = new VisualElement { name = "modal" };
            modal.AddToClassList("modal");

            // Header
            var header = new VisualElement();
            header.AddToClassList("modal-header");
            var titleLbl = new Label(title); titleLbl.AddToClassList("modal-title");
            var closeBtn = new Button(Close) { text = "✕" }; closeBtn.AddToClassList("modal-close-btn");
            header.Add(titleLbl); header.Add(closeBtn);
            modal.Add(header);

            // Body
            var body = new VisualElement(); body.AddToClassList("modal-body");
            buildBody?.Invoke(body);
            modal.Add(body);

            // Footer
            var footer = new VisualElement(); footer.AddToClassList("modal-footer");
            if (buildFooter != null) buildFooter(footer);
            else
            {
                var okBtn = new Button(Close) { text = "OK" };
                okBtn.AddToClassList("btn"); okBtn.AddToClassList("btn-primary");
                footer.Add(okBtn);
            }
            modal.Add(footer);

            overlay.Add(modal);
            host.Add(overlay);
        }

        public void Close()
        {
            host.Clear();
            host.AddToClassList("hidden");
        }

        public bool IsOpen => host.childCount > 0;
    }
}
