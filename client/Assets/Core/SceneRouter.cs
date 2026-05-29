using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace InteractiveClient.Core
{
    /// <summary>
    /// Навигация между экранами без перезагрузки Unity-сцены.
    /// Держит единственный #screen-container корневого UIDocument и
    /// переключает в нём активный экран.
    ///
    /// Экраны регистрируются в AppManager.InitializeServices() один раз,
    /// лениво инициализируются при первом показе.
    /// </summary>
    public class SceneRouter
    {
        private readonly VisualElement screenContainer;
        private readonly Dictionary<ScreenId, IScreen> screens = new();
        private readonly Dictionary<ScreenId, VisualElement> screenRoots = new();

        private IScreen currentScreen;

        public IScreen Current => currentScreen;

        public SceneRouter(VisualElement screenContainer)
        {
            this.screenContainer = screenContainer
                ?? throw new System.ArgumentNullException(nameof(screenContainer));
        }

        /// <summary>Регистрирует экран. Вызывается один раз на запуске приложения.</summary>
        public void Register(IScreen screen)
        {
            if (screen == null) return;

            if (screens.ContainsKey(screen.Id))
            {
                Debug.LogWarning($"[SceneRouter] Screen {screen.Id} already registered, overwriting.");
            }

            screens[screen.Id] = screen;
        }

        /// <summary>Переключает на указанный экран. Опционально передаёт данные.</summary>
        public void Navigate(ScreenId target, object data = null)
        {
            if (!screens.TryGetValue(target, out var next))
            {
                Debug.LogError($"[SceneRouter] Screen {target} is not registered.");
                return;
            }

            // Скрыть текущий
            if (currentScreen != null)
            {
                currentScreen.Hide();
                if (screenRoots.TryGetValue(currentScreen.Id, out var prevRoot))
                    prevRoot.style.display = DisplayStyle.None;
            }

            // Лениво инициализировать следующий
            if (!screenRoots.TryGetValue(target, out var root))
            {
                root = new VisualElement { name = $"screen-{target}" };
                root.style.flexGrow = 1;
                screenContainer.Add(root);
                screenRoots[target] = root;

                next.Initialize(root);
            }

            root.style.display = DisplayStyle.Flex;
            next.Show(data);
            currentScreen = next;

            Debug.Log($"[SceneRouter] Navigated to {target}");
        }

        /// <summary>Полная очистка (например, на OnDestroy AppManager).</summary>
        public void Dispose()
        {
            foreach (var s in screens.Values)
                s.Dispose();

            screens.Clear();
            screenRoots.Clear();
            currentScreen = null;
        }
    }
}
