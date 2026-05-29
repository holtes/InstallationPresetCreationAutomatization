using System.Threading.Tasks;
using InteractiveClient.Projects;
using UnityEngine.UIElements;

namespace InteractiveClient.UI.Screens.Editor
{
    /// <summary>
    /// Контракт шага мастера редактора.
    /// EditorScreenController лениво создаёт каждый шаг, инициализирует один раз,
    /// а затем вызывает Enter/Leave при переходах стреппером.
    /// </summary>
    public interface IEditorStep
    {
        /// <summary>Путь к UXML фрагмента шага в Resources (без расширения).</summary>
        string UxmlResourcePath { get; }

        /// <summary>Один раз — после клонирования UXML в контейнер шага.</summary>
        void Initialize(VisualElement root, ProjectModel project);

        /// <summary>Вызывается при входе на шаг. Должен подготовить UI к показу.</summary>
        Task EnterAsync();

        /// <summary>Вызывается при уходе с шага. Может сохранить промежуточные данные.</summary>
        Task LeaveAsync();

        /// <summary>
        /// Проверка готовности шага к переходу «вперёд».
        /// Возвращает null если всё хорошо, иначе сообщение об ошибке для Toast'а.
        /// </summary>
        string Validate();

        /// <summary>Освобождение ресурсов.</summary>
        void Dispose();
    }
}
