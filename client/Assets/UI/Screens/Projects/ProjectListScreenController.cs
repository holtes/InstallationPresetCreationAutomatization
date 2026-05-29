using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using InteractiveClient.Auth;
using InteractiveClient.Core;
using InteractiveClient.Network;
using InteractiveClient.Projects;
using InteractiveClient.UI.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace InteractiveClient.UI.Screens.Projects
{
    /// <summary>
    /// Экран списка проектов. Поддерживает:
    ///   • фильтрацию sidebar (Все / Мои / Недавние)
    ///   • переключение вида (сетка / список)
    ///   • создание нового проекта через модалку
    ///   • контекстное удаление
    ///   • выход из аккаунта
    ///
    /// Заглушки для будущих экранов (TODO):
    ///   • "Пресеты" — отдельный экран PresetGalleryScreen с превью всех пресетов
    ///   • "Настройки" — SettingsScreen (профиль, API, тема)
    /// </summary>
    public class ProjectListScreenController : BaseScreenController
    {
        protected override string UxmlResourcePath => "UI/Screens/Projects/ProjectListScreen";
        public override ScreenId Id => ScreenId.ProjectList;

        // ----- references -----
        private VisualElement projectsGrid;
        private VisualElement projectsList;
        private VisualElement emptyState;
        private TextField searchField;
        private Button newProjectBtn;
        private Button emptyCreateBtn;
        private Button userMenuBtn;
        private Label userNameLbl;

        private Button navAllBtn, navMyBtn, navRecentBtn, navPresetsBtn, navSettingsBtn;
        private Button viewGridBtn, viewListBtn;

        // ----- state -----
        private CancellationTokenSource cts;
        private List<ProjectModel> allProjects = new();
        private string searchQuery = string.Empty;

        private enum FilterMode { All, My, Recent }
        private enum ViewMode   { Grid, List }

        private FilterMode filterMode = FilterMode.All;
        private ViewMode   viewMode   = ViewMode.Grid;

        // ----------------------------------------------------------
        // Lifecycle
        // ----------------------------------------------------------

        protected override void OnInitialize()
        {
            // Layout / data containers
            projectsGrid   = Root.Q<VisualElement>("projects-grid");
            projectsList   = Root.Q<VisualElement>("projects-list");
            emptyState     = Root.Q<VisualElement>("empty-state");

            // Top bar
            searchField    = Root.Q<TextField>("search-field");
            userMenuBtn    = Root.Q<Button>("user-menu-btn");
            userNameLbl    = Root.Q<Label>("user-name");

            // Sidebar
            navAllBtn      = Root.Q<Button>("nav-all");
            navMyBtn       = Root.Q<Button>("nav-my");
            navRecentBtn   = Root.Q<Button>("nav-recent");
            navPresetsBtn  = Root.Q<Button>("nav-presets");
            navSettingsBtn = Root.Q<Button>("nav-settings");

            // Content header
            viewGridBtn    = Root.Q<Button>("view-grid-btn");
            viewListBtn    = Root.Q<Button>("view-list-btn");
            newProjectBtn  = Root.Q<Button>("new-project-btn");
            emptyCreateBtn = Root.Q<Button>("empty-create-btn");

            // Wire events
            if (newProjectBtn  != null) newProjectBtn.clicked  += OpenNewProjectModal;
            if (emptyCreateBtn != null) emptyCreateBtn.clicked += OpenNewProjectModal;
            if (userMenuBtn    != null) userMenuBtn.clicked    += OnUserMenuClicked;

            if (navAllBtn      != null) navAllBtn.clicked      += () => SetFilter(FilterMode.All);
            if (navMyBtn       != null) navMyBtn.clicked       += () => SetFilter(FilterMode.My);
            if (navRecentBtn   != null) navRecentBtn.clicked   += () => SetFilter(FilterMode.Recent);
            if (navPresetsBtn  != null) navPresetsBtn.clicked  += () => Toast.Info("Раздел «Пресеты» в разработке.");
            if (navSettingsBtn != null) navSettingsBtn.clicked += () => AppManager.Instance?.Router?.Navigate(ScreenId.Settings);

            if (viewGridBtn != null) viewGridBtn.clicked += () => SetViewMode(ViewMode.Grid);
            if (viewListBtn != null) viewListBtn.clicked += () => SetViewMode(ViewMode.List);

            if (searchField != null)
                searchField.RegisterValueChangedCallback(ev =>
                {
                    searchQuery = ev.newValue?.Trim() ?? string.Empty;
                    Render();
                });

            // Очистить "reference layout" из UXML
            projectsGrid?.Clear();
            projectsList?.Clear();
        }

        protected override async void OnShow(object data)
        {
            UpdateUserHeader();
            cts?.Cancel();
            cts = new CancellationTokenSource();

            await LoadProjectsAsync(cts.Token);
        }

        protected override void OnHide()
        {
            cts?.Cancel();
            cts = null;
        }

        // ----------------------------------------------------------
        // Filters / views
        // ----------------------------------------------------------

        private void SetFilter(FilterMode mode)
        {
            filterMode = mode;

            UpdateActiveClass(navAllBtn,    mode == FilterMode.All);
            UpdateActiveClass(navMyBtn,     mode == FilterMode.My);
            UpdateActiveClass(navRecentBtn, mode == FilterMode.Recent);

            Render();
        }

        private void SetViewMode(ViewMode mode)
        {
            viewMode = mode;

            UpdateActiveClass(viewGridBtn, mode == ViewMode.Grid, "view-toggle__btn--active");
            UpdateActiveClass(viewListBtn, mode == ViewMode.List, "view-toggle__btn--active");

            Render();
        }

        private static void UpdateActiveClass(VisualElement element, bool active, string className = "sidebar__item--active")
        {
            if (element == null) return;
            if (active) element.AddToClassList(className);
            else        element.RemoveFromClassList(className);
        }

        // ----------------------------------------------------------
        // Loading / rendering
        // ----------------------------------------------------------

        private async System.Threading.Tasks.Task LoadProjectsAsync(CancellationToken ct)
        {
            try
            {
                var service = ServiceLocator.Get<ProjectService>();
                allProjects = await service.ListAsync(ct);
                Render();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Toast.Error("Не удалось загрузить проекты.");
            }
        }

        private IEnumerable<ProjectModel> GetFilteredProjects()
        {
            IEnumerable<ProjectModel> result = allProjects;

            // 1) sidebar filter
            switch (filterMode)
            {
                case FilterMode.My:
                    var session = ServiceLocator.Get<UserSession>();
                    var myId = session?.UserId;
                    if (!string.IsNullOrEmpty(myId))
                        result = result.Where(p => p.OwnerId == myId);
                    break;

                case FilterMode.Recent:
                    result = result.OrderByDescending(p => p.UpdatedAt).Take(10);
                    break;

                case FilterMode.All:
                default:
                    break;
            }

            // 2) search
            if (!string.IsNullOrEmpty(searchQuery))
                result = result.Where(p =>
                    (p.Name ?? "").IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0);

            return result;
        }

        private void Render()
        {
            if (projectsGrid == null && projectsList == null) return;

            projectsGrid?.Clear();
            projectsList?.Clear();

            var filtered = GetFilteredProjects().ToList();

            if (filtered.Count == 0)
            {
                ShowEmpty(true);
                projectsGrid?.AddToClassList("hidden");
                projectsList?.AddToClassList("hidden");
                return;
            }
            ShowEmpty(false);

            // Show only active container
            if (viewMode == ViewMode.Grid)
            {
                projectsGrid?.RemoveFromClassList("hidden");
                projectsList?.AddToClassList("hidden");
                if (projectsGrid != null)
                    foreach (var p in filtered) projectsGrid.Add(BuildCard(p));
            }
            else
            {
                projectsGrid?.AddToClassList("hidden");
                projectsList?.RemoveFromClassList("hidden");
                if (projectsList != null)
                    foreach (var p in filtered) projectsList.Add(BuildRow(p));
            }
        }

        private void ShowEmpty(bool show)
        {
            if (emptyState == null) return;
            if (show) emptyState.RemoveFromClassList("hidden");
            else      emptyState.AddToClassList("hidden");
            emptyState.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ----------------------------------------------------------
        // Card / row builders
        // ----------------------------------------------------------

        private VisualElement BuildCard(ProjectModel p)
        {
            var card = new VisualElement();
            card.AddToClassList("project-card");

            var preview = new VisualElement();
            preview.AddToClassList("project-card__preview");
            var previewLbl = new Label("Нет превью");
            previewLbl.AddToClassList("project-card__preview-placeholder");
            preview.Add(previewLbl);
            card.Add(preview);

            var info = new VisualElement();
            info.AddToClassList("project-card__info");

            var header = new VisualElement();
            header.AddToClassList("project-card__header");
            var title = new Label(p.Name ?? "(без имени)");
            title.AddToClassList("project-card__title");
            Button menuBtn = null;
            menuBtn = new Button(() => OnCardMenuClicked(p, menuBtn)) { text = "⋯" };
            menuBtn.AddToClassList("btn-icon");
            menuBtn.AddToClassList("btn-ghost");
            menuBtn.AddToClassList("project-card__menu-btn");
            header.Add(title);
            header.Add(menuBtn);
            info.Add(header);

            var meta = new VisualElement();
            meta.AddToClassList("project-card__meta");
            var badge = new Label(p.Status.ToDisplayString());
            badge.AddToClassList("badge");
            badge.AddToClassList(p.Status.ToBadgeClass());
            var date = new Label(p.UpdatedAt == default
                ? ""
                : p.UpdatedAt.ToLocalTime().ToString("dd.MM.yyyy"));
            date.AddToClassList("project-card__date");
            meta.Add(badge);
            meta.Add(date);
            info.Add(meta);

            card.Add(info);

            card.RegisterCallback<ClickEvent>(ev =>
            {
                if (ev.target == menuBtn || (ev.target is VisualElement ve && ve.GetFirstAncestorOfType<Button>() == menuBtn))
                    return;
                OpenProject(p);
            });

            return card;
        }

        private VisualElement BuildRow(ProjectModel p)
        {
            var row = new VisualElement();
            row.AddToClassList("project-row");

            var preview = new VisualElement();
            preview.AddToClassList("project-row__preview");
            preview.Add(new Label("img") { });
            row.Add(preview);

            var title = new Label(p.Name ?? "(без имени)");
            title.AddToClassList("project-row__title");
            row.Add(title);

            var preset = new Label(p.PresetId ?? "—");
            preset.AddToClassList("project-row__preset");
            row.Add(preset);

            var statusWrap = new VisualElement();
            statusWrap.AddToClassList("project-row__status");
            var badge = new Label(p.Status.ToDisplayString());
            badge.AddToClassList("badge");
            badge.AddToClassList(p.Status.ToBadgeClass());
            statusWrap.Add(badge);
            row.Add(statusWrap);

            var date = new Label(p.UpdatedAt == default
                ? ""
                : p.UpdatedAt.ToLocalTime().ToString("dd.MM.yyyy"));
            date.AddToClassList("project-row__date");
            row.Add(date);

            Button menuBtn = null;
            menuBtn = new Button(() => OnCardMenuClicked(p, menuBtn)) { text = "⋯" };
            menuBtn.AddToClassList("btn-icon");
            menuBtn.AddToClassList("btn-ghost");
            row.Add(menuBtn);

            row.RegisterCallback<ClickEvent>(ev =>
            {
                if (ev.target == menuBtn || (ev.target is VisualElement ve && ve.GetFirstAncestorOfType<Button>() == menuBtn))
                    return;
                OpenProject(p);
            });

            return row;
        }

        // ----------------------------------------------------------
        // Actions
        // ----------------------------------------------------------

        private void OpenProject(ProjectModel p)
        {
            EventBus.Publish(new ProjectOpenedEvent(p.Id));
            AppManager.Instance?.Router?.Navigate(ScreenId.Editor, p);
        }

        private void OnCardMenuClicked(ProjectModel p, VisualElement anchor)
        {
            PopupMenu.Show(
                anchor,
                new PopupMenuItem("Открыть",      () => OpenProject(p)),
                new PopupMenuItem("Дублировать",  () => DuplicateProject(p)),
                PopupMenuItem.Separator(),
                new PopupMenuItem("Удалить",      () => ConfirmDelete(p), danger: true));
        }

        private void ConfirmDelete(ProjectModel p)
        {
            var modal = ServiceLocator.Get<ModalService>();
            modal.Confirm(
                "Удалить проект?",
                $"Вы действительно хотите удалить «{p.Name}»? Это действие необратимо.",
                "Удалить", "Отмена",
                onConfirm: async () =>
                {
                    try
                    {
                        await ServiceLocator.Get<ProjectService>().DeleteAsync(p.Id);
                        allProjects.RemoveAll(x => x.Id == p.Id);
                        Render();
                        Toast.Success("Проект удалён.");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                        Toast.Error("Не удалось удалить проект.");
                    }
                });
        }

        private async void DuplicateProject(ProjectModel src)
        {
            try
            {
                var service = ServiceLocator.Get<ProjectService>();
                var copy = await service.CreateAsync(
                    name: (src.Name ?? "Проект") + " (копия)",
                    description: src.Description ?? "",
                    targetProfileId: src.TargetProfileId);

                if (copy == null)
                {
                    Toast.Error("Не удалось дублировать проект.");
                    return;
                }

                allProjects.Insert(0, copy);
                Render();
                Toast.Success("Проект дублирован.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Toast.Error("Ошибка при дублировании проекта.");
            }
        }

        // ----------------------------------------------------------
        // New Project modal (built programmatically via global ModalService)
        // ----------------------------------------------------------

        private async void OpenNewProjectModal()
        {
            // Подгружаем актуальные hardware-profiles, чтобы dropdown отражал реальные id.
            List<HardwareProfileDto> profiles;
            try
            {
                profiles = await ServiceLocator.Get<HardwareProfileService>().ListAsync();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Toast.Error("Не удалось загрузить список аппаратных профилей.");
                return;
            }

            var modal = ServiceLocator.Get<ModalService>();

            TextField nameField = null;
            TextField descField = null;
            DropdownField hwField = null;

            // Опция «Не указан» для случая, когда профиль не нужен/не выбран.
            const string NoneLabel = "Не указан";
            var choices = new List<string> { NoneLabel };
            choices.AddRange(profiles.ConvertAll(p => p.Name ?? $"#{p.Id}"));

            modal.Show(
                "Новый проект",
                body =>
                {
                    nameField = new TextField("Название проекта");
                    nameField.AddToClassList("modal-text-input");
                    body.Add(nameField);

                    descField = new TextField("Описание (опционально)") { multiline = true };
                    descField.AddToClassList("modal-text-input");
                    body.Add(descField);

                    hwField = new DropdownField(
                        label: "Аппаратный профиль",
                        choices: choices,
                        defaultIndex: profiles.Count > 0 ? 1 : 0); // первый реальный, если есть
                    hwField.AddToClassList("modal-text-input");
                    body.Add(hwField);
                },
                footer =>
                {
                    var cancelBtn = new Button(modal.Close) { text = "Отмена" };
                    cancelBtn.AddToClassList("btn");
                    cancelBtn.AddToClassList("btn-ghost");

                    var createBtn = new Button(async () =>
                    {
                        var name = nameField?.value?.Trim();
                        if (string.IsNullOrEmpty(name))
                        {
                            Toast.Warning("Введите название проекта.");
                            return;
                        }

                        var desc = descField?.value ?? "";
                        var profileId = ResolveProfileId(hwField?.value, profiles);

                        try
                        {
                            var service = ServiceLocator.Get<ProjectService>();
                            var created = await service.CreateAsync(name, desc, profileId);
                            if (created == null)
                            {
                                Toast.Error("Не удалось создать проект.");
                                return;
                            }

                            allProjects.Insert(0, created);
                            modal.Close();
                            Toast.Success("Проект создан.");
                            OpenProject(created);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogException(ex);
                            Toast.Error("Ошибка при создании проекта.");
                        }
                    })
                    { text = "Создать" };
                    createBtn.AddToClassList("btn");
                    createBtn.AddToClassList("btn-primary");

                    footer.Add(cancelBtn);
                    footer.Add(createBtn);
                });
        }

        private static int? ResolveProfileId(string selectedName, List<HardwareProfileDto> profiles)
        {
            if (string.IsNullOrEmpty(selectedName) || selectedName == "Не указан") return null;
            var match = profiles.Find(p => p.Name == selectedName);
            return match != null ? match.Id : (int?)null;
        }

        // ----------------------------------------------------------
        // User header / logout
        // ----------------------------------------------------------

        private void UpdateUserHeader()
        {
            var session = ServiceLocator.Get<UserSession>();
            if (userNameLbl != null)
            {
                var label = !string.IsNullOrEmpty(session.Name) ? session.Name
                          : !string.IsNullOrEmpty(session.Email) ? session.Email
                          : "Пользователь";
                userNameLbl.text = label;
            }
        }

        private void OnUserMenuClicked()
        {
            // Выпадашка справа от аватарки: Настройки / Выйти.
            PopupMenu.Show(
                userMenuBtn,
                new PopupMenuItem("Настройки", () =>
                    AppManager.Instance?.Router?.Navigate(ScreenId.Settings)),
                PopupMenuItem.Separator(),
                new PopupMenuItem("Выйти", ConfirmLogout, danger: true));
        }

        private void ConfirmLogout()
        {
            var modal = ServiceLocator.Get<ModalService>();
            modal.Confirm(
                "Выход",
                "Вы уверены, что хотите выйти?",
                "Выйти", "Отмена",
                onConfirm: async () =>
                {
                    try
                    {
                        await ServiceLocator.Get<AuthService>().LogoutAsync();
                    }
                    finally
                    {
                        AppManager.Instance?.Router?.Navigate(ScreenId.Auth);
                    }
                });
        }
    }
}
