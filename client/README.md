# Unity Client

Десктопное приложение на Unity (URP, Windows Standalone) — визуальный low-code редактор
для прототипирования игровых механик. Клиентская часть платформы.

UI полностью реализован на **UI Toolkit** (UXML + USS).

## Структура

```
client/
├── Assets/
│   ├── Core/            # AppManager, SceneRouter, EventBus, ServiceLocator
│   ├── Auth/            # AuthService, UserSession
│   ├── Projects/        # ProjectService, ProjectModel
│   ├── AssetsModule/    # AssetUploader, AssetValidator, AssetLibrary, AssetThumbnail, AssetModel
│   ├── Presets/         # PresetRegistry, PresetConfigEditor, PresetPreview
│   │   └── PresetModels/  # Puzzle, Quiz, Gallery, MemoryGame, Timeline3D
│   ├── Mapping/         # MappingEditor, SlotDefinition, MappingValidator
│   ├── Build/           # BuildService, BuildStatusTracker
│   ├── LLM/             # LLMService
│   ├── Preview/         # PreviewRunner, PreviewCamera
│   ├── Network/         # ApiClient, ApiEndpoints, ApiModels
│   │
│   ├── UI/              # === UI Toolkit: UXML + USS ===
│   │   ├── AppRoot.uxml            # Корневой документ, хост для всех экранов
│   │   ├── Theme/
│   │   │   ├── variables.uss       # :root токены дизайн-системы
│   │   │   └── theme.uss           # Глобальные стили (кнопки, инпуты, карточки, modal...)
│   │   ├── Components/             # Переиспользуемые шаблоны
│   │   │   ├── Modal.uxml
│   │   │   ├── Toast.uxml
│   │   │   ├── ProjectCard.uxml
│   │   │   ├── AssetCard.uxml
│   │   │   ├── PresetCard.uxml
│   │   │   └── SlotItem.uxml
│   │   └── Screens/
│   │       ├── Auth/               # AuthScreen.uxml + .uss
│   │       ├── Projects/           # ProjectListScreen.uxml + .uss
│   │       └── Editor/             # EditorScreen.uxml + Stepper
│   │           └── Steps/          # Step1_Assets … Step6_Build + Steps.uss
│   │
│   ├── Scenes/
│   ├── Resources/
│   ├── StreamingAssets/
│   └── Settings/
│
├── Packages/
└── ProjectSettings/
```

## Дизайн-система

Тёмная тема, минимализм. Акцент — электрический синий `#4A9EFF`.
Все цвета/отступы/радиусы вынесены в `Assets/UI/Theme/variables.uss` (переменные `:root`),
глобальные стили — в `theme.uss`.

Палитра (основные):
- `--bg-primary` `#1E1E2E` — фон приложения
- `--bg-secondary` `#2A2A3D` — панели, карточки
- `--bg-tertiary` `#363650` — инпуты, ховеры
- `--accent` `#4A9EFF` — акцентный цвет
- `--success` `#4ADE80` / `--warning` `#FBBF24` / `--danger` `#F87171`

## Навигация

Все экраны живут в `#screen-container` корневого `AppRoot.uxml`.
`SceneRouter` (C#) активирует нужный экран, не перезагружая Unity-сцену.

```
[Login] → [ProjectList] → [Editor]
                             └─ Stepper: Assets → Preset → Mapping → Parameters → Preview → Build
```

## Статус

- ✅ Структура проекта, дизайн-система, все UXML/USS-экраны и общие компоненты
- ⏳ C#-скрипты (Core/Auth/Projects/… — следующий этап)
- ⏳ PanelSettings + UIDocument GameObject в сцене
- ⏳ Интеграция с бэкендом через `Network/ApiClient`

## Unity

- Unity 2022 LTS+ / URP
- UI Toolkit (`com.unity.ui` — штатно)
