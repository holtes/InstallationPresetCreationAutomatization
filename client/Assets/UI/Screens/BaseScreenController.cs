using InteractiveClient.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace InteractiveClient.UI.Screens
{
    /// <summary>
    /// Базовый класс экрана. Загружает UXML из Resources/UI/Screens/&lt;ScreenName&gt;.uxml
    /// и клонирует его в root контейнер, переданный SceneRouter'ом.
    ///
    /// Жизненный цикл:
    ///   Initialize(root) — один раз, при первой навигации.
    ///   Show(data)      — при каждом открытии экрана.
    ///   Hide()          — при уходе с экрана.
    ///   Dispose()       — при очистке (обычно при завершении приложения).
    /// </summary>
    public abstract class BaseScreenController : IScreen
    {
        protected VisualElement Root { get; private set; }
        protected bool IsInitialized { get; private set; }

        /// <summary>Путь к UXML относительно Resources, без расширения. Например "UI/Screens/Auth/AuthScreen".</summary>
        protected abstract string UxmlResourcePath { get; }

        public abstract ScreenId Id { get; }

        public void Initialize(VisualElement container)
        {
            if (IsInitialized) return;

            var asset = Resources.Load<VisualTreeAsset>(UxmlResourcePath);
#if UNITY_EDITOR
            if (asset == null)
                asset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"Assets/{UxmlResourcePath}.uxml");
#endif
            if (asset == null)
            {
                Debug.LogError($"[{GetType().Name}] UXML not found at Resources/{UxmlResourcePath}.uxml or Assets/{UxmlResourcePath}.uxml");
                Root = new VisualElement { name = GetType().Name + "-missing" };
            }
            else
            {
                Root = asset.CloneTree();
                Root.style.flexGrow = 1;
            }

            container.Add(Root);
            Root.style.display = DisplayStyle.None;

            OnInitialize();
            IsInitialized = true;
        }

        public virtual void Show(object data = null)
        {
            if (Root != null) Root.style.display = DisplayStyle.Flex;
            OnShow(data);
        }

        public virtual void Hide()
        {
            OnHide();
            if (Root != null) Root.style.display = DisplayStyle.None;
        }

        public virtual void Dispose()
        {
            OnDispose();
            Root?.RemoveFromHierarchy();
            Root = null;
            IsInitialized = false;
        }

        // ------- Хуки для потомков -------
        protected virtual void OnInitialize() { }
        protected virtual void OnShow(object data) { }
        protected virtual void OnHide() { }
        protected virtual void OnDispose() { }
    }
}
