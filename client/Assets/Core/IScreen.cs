using UnityEngine.UIElements;

namespace InteractiveClient.Core
{
    /// <summary>
    /// Общий контракт для экрана. Контроллер экрана подгружает свой UXML
    /// (обычно из Resources или через VisualTreeAsset-поле) и биндит его
    /// к корневому VisualElement, полученному в Show().
    /// </summary>
    public interface IScreen
    {
        ScreenId Id { get; }

        /// <summary>Вызывается один раз, когда экран впервые запрошен. Здесь — построение UI.</summary>
        void Initialize(VisualElement root);

        /// <summary>Вызывается при активации экрана. Можно передать data (например, projectId).</summary>
        void Show(object data = null);

        /// <summary>Вызывается при деактивации экрана.</summary>
        void Hide();

        /// <summary>Освобождение ресурсов, отписки от событий.</summary>
        void Dispose();
    }
}
