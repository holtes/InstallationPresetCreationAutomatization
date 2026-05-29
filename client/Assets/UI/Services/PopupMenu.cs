using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace InteractiveClient.UI.Services
{
    /// <summary>
    /// Лёгкое выпадающее меню (контекстное / dropdown).
    /// Использует #tooltip-host корневого UIDocument как overlay-host:
    ///   • заполняет экран прозрачным захватчиком кликов вне меню
    ///   • рисует панель с position:absolute рядом с anchor-элементом
    ///   • закрывается при клике вне или при выборе пункта
    ///
    /// Использование:
    ///   PopupMenu.Show(anchorButton,
    ///       new PopupMenuItem("Открыть", () => Open(p)),
    ///       new PopupMenuItem("Дублировать", () => Duplicate(p)),
    ///       PopupMenuItem.Separator(),
    ///       new PopupMenuItem("Удалить", () => Delete(p), danger: true));
    /// </summary>
    public static class PopupMenu
    {
        private static VisualElement currentHost;

        public static VisualElement Host
        {
            get => currentHost;
            set => currentHost = value;
        }

        public static void Show(VisualElement anchor, params PopupMenuItem[] items)
        {
            if (currentHost == null || anchor == null || items == null) return;

            Close();

            currentHost.RemoveFromClassList("hidden");

            // Прозрачный фон, ловит клики вне меню — закрывает попап.
            var dismiss = new VisualElement { name = "popup-dismiss" };
            dismiss.AddToClassList("popup-dismiss");
            dismiss.RegisterCallback<PointerDownEvent>(_ => Close());

            // Сама панель меню.
            var panel = new VisualElement { name = "popup-menu" };
            panel.AddToClassList("popup-menu");

            foreach (var item in items)
            {
                if (item.IsSeparator)
                {
                    var sep = new VisualElement();
                    sep.AddToClassList("popup-menu__separator");
                    panel.Add(sep);
                    continue;
                }

                var btn = new Button(() =>
                {
                    Close();
                    try { item.OnClick?.Invoke(); }
                    catch (Exception e) { UnityEngine.Debug.LogException(e); }
                })
                { text = item.Label };
                btn.AddToClassList("popup-menu__item");
                if (item.Danger) btn.AddToClassList("popup-menu__item--danger");
                panel.Add(btn);
            }

            currentHost.Add(dismiss);
            currentHost.Add(panel);

            // Позиционирование рядом с anchor — после первого layout pass.
            panel.RegisterCallback<GeometryChangedEvent>(evt => PositionPanel(panel, anchor));
        }

        public static void Close()
        {
            if (currentHost == null) return;
            currentHost.Clear();
            currentHost.AddToClassList("hidden");
        }

        private static void PositionPanel(VisualElement panel, VisualElement anchor)
        {
            var anchorBound = anchor.worldBound;
            var hostBound   = currentHost.worldBound;
            var panelW      = panel.resolvedStyle.width;
            var panelH      = panel.resolvedStyle.height;

            // По умолчанию — снизу слева от anchor.
            var x = anchorBound.xMin - hostBound.xMin;
            var y = anchorBound.yMax - hostBound.yMin + 4f;

            // Если panel вылазит вправо — выравниваем по правому краю anchor.
            if (x + panelW > hostBound.width)
                x = anchorBound.xMax - hostBound.xMin - panelW;
            if (x < 0) x = 4f;

            // Если внизу не помещается — над anchor.
            if (y + panelH > hostBound.height)
                y = anchorBound.yMin - hostBound.yMin - panelH - 4f;
            if (y < 0) y = 4f;

            panel.style.left = x;
            panel.style.top = y;
        }
    }

    public class PopupMenuItem
    {
        public string Label { get; }
        public Action OnClick { get; }
        public bool Danger { get; }
        public bool IsSeparator { get; }

        public PopupMenuItem(string label, Action onClick, bool danger = false)
        {
            Label = label;
            OnClick = onClick;
            Danger = danger;
            IsSeparator = false;
        }

        private PopupMenuItem(bool separator)
        {
            IsSeparator = separator;
        }

        public static PopupMenuItem Separator() => new PopupMenuItem(separator: true);
    }
}
