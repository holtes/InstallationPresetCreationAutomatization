using System.Collections.Generic;
using InteractiveClient.AssetsModule;
using InteractiveClient.Mapping;
using InteractiveClient.Network;
using Newtonsoft.Json.Linq;

namespace InteractiveClient.Presets
{
    /// <summary>
    /// Локальные схемы 5 hardcoded пресетов.
    /// Используются как fallback когда серверный PresetRegistry не знает о пресете
    /// (сервер не синхронизирован с клиентскими реализациями).
    ///
    /// Slot-ids здесь должны совпадать с Context.GetAsset("slot_id") в каждом PresetBase.
    /// Parameter-ids должны совпадать с Context.GetParam("param_id").
    /// </summary>
    public static class LocalPresetSchemas
    {
        private static readonly Dictionary<string, PresetInfo> All = new()
        {
            ["puzzle"]     = BuildPuzzle(),
            ["memory"]     = BuildMemory(),
            ["quiz"]       = BuildQuiz(),
            ["video_quiz"] = BuildVideoQuiz(),
            ["scene_3d"]   = BuildScene3D(),
        };

        /// <summary>
        /// Возвращает локальную схему пресета или null если ID неизвестен.
        /// </summary>
        public static PresetInfo Get(string presetId)
            => All.TryGetValue(presetId, out var p) ? p : null;

        // ── Пазл ──────────────────────────────────────────────────────────────
        private static PresetInfo BuildPuzzle() => new()
        {
            Id = "puzzle",
            Name = "Пазл",
            Description = "Изображение делится на N×M кусочков — собери пазл!",
            Version = "1.0",
            Slots = new List<SlotDefinition>
            {
                new()
                {
                    Id            = "puzzle_image",
                    Name          = "Изображение пазла",
                    AcceptedTypes = new List<AssetType> { AssetType.Image },
                    Required      = true,
                    Group         = "Основные",
                    Description   = "Изображение, которое будет разрезано на кусочки"
                }
            },
            Schema = new PresetSchemaDto
            {
                Parameters = new List<ParameterSchemaDto>
                {
                    P("cols",          "Столбцы",                       "int",   3,     2,   8,   "Геймплей"),
                    P("rows",          "Строки",                        "int",   3,     2,   8,   "Геймплей"),
                    P("shuffle",       "Перемешивать при старте",       "bool",  true,  grp: "Геймплей"),
                    P("snap_distance", "Расстояние притягивания (px)",  "int",   30,    10,  100, "Геймплей"),
                    P("show_ghost",    "Показывать силуэт",             "bool",  true,  grp: "Визуал"),
                    P("border_color",  "Цвет обводки элементов",        "color", "#4A9EFF", grp: "Визуал"),
                    P("time_limit",    "Лимит времени (0 = без лимита)","int",   0,     0,   600, "Таймеры"),
                    P("show_timer",    "Показывать таймер",             "bool",  false, grp: "Таймеры"),
                }
            }
        };

        // ── Мемори ────────────────────────────────────────────────────────────
        private static PresetInfo BuildMemory() => new()
        {
            Id = "memory",
            Name = "Мемори",
            Description = "Переворачивай карточки и находи пары",
            Version = "1.0",
            Slots = new List<SlotDefinition>
            {
                Img("memory_image_1", "Изображение 1", required: true,  group: "Изображения"),
                Img("memory_image_2", "Изображение 2", required: true,  group: "Изображения"),
                Img("memory_image_3", "Изображение 3", required: true,  group: "Изображения"),
                Img("memory_image_4", "Изображение 4", required: true,  group: "Изображения"),
                Img("memory_image_5", "Изображение 5", required: false, group: "Изображения"),
                Img("memory_image_6", "Изображение 6", required: false, group: "Изображения"),
                Img("memory_image_7", "Изображение 7", required: false, group: "Изображения"),
                Img("memory_image_8", "Изображение 8", required: false, group: "Изображения"),
                new()
                {
                    Id            = "memory_sound",
                    Name          = "Звук совпадения",
                    AcceptedTypes = new List<AssetType> { AssetType.Audio },
                    Required      = false,
                    Group         = "Аудио",
                    Description   = "Воспроизводится при нахождении пары"
                }
            },
            Schema = new PresetSchemaDto
            {
                Parameters = new List<ParameterSchemaDto>
                {
                    P("card_pairs",   "Количество пар",                "int",   4,    2,  8,    "Геймплей"),
                    P("shuffle",      "Перемешивать карточки",         "bool",  true, grp: "Геймплей"),
                    P("flip_delay",   "Задержка переворота (сек)",     "float", 1.0,  0.3, 3.0, "Геймплей"),
                    P("time_limit",   "Лимит времени (0 = без лимита)","int",   0,    0,   300, "Таймеры"),
                    P("show_timer",   "Показывать таймер",             "bool",  true, grp: "Таймеры"),
                }
            }
        };

        // ── Викторина ─────────────────────────────────────────────────────────
        private static PresetInfo BuildQuiz() => new()
        {
            Id = "quiz",
            Name = "Викторина",
            Description = "Вопросы из JSON-файла, 4 варианта ответа, таймер",
            Version = "1.0",
            Slots = new List<SlotDefinition>
            {
                new()
                {
                    Id            = "quiz_data",
                    Name          = "Файл вопросов (JSON)",
                    AcceptedTypes = new List<AssetType> { AssetType.Text },
                    Required      = true,
                    Group         = "Основные",
                    Description   = "JSON-массив [{\"q\":\"...\",\"a\":[...],\"c\":0}]"
                },
                Img("quiz_image", "Фоновое изображение", required: false, group: "Оформление"),
            },
            Schema = new PresetSchemaDto
            {
                Parameters = new List<ParameterSchemaDto>
                {
                    P("time_per_question", "Время на вопрос (сек)",      "int",  30,   5,  120, "Геймплей"),
                    P("shuffle_questions", "Перемешивать вопросы",       "bool", true, grp: "Геймплей"),
                    P("shuffle_answers",   "Перемешивать варианты",      "bool", true, grp: "Геймплей"),
                    P("show_correct",      "Показывать правильный ответ","bool", true, grp: "Геймплей"),
                    P("show_timer",        "Показывать таймер",          "bool", true, grp: "Интерфейс"),
                }
            }
        };

        // ── Видео-угадайка ────────────────────────────────────────────────────
        private static PresetInfo BuildVideoQuiz() => new()
        {
            Id = "video_quiz",
            Name = "Видео-угадайка",
            Description = "Смотри видеофрагмент и выбери правильный ответ из 4 вариантов",
            Version = "1.0",
            Slots = new List<SlotDefinition>
            {
                new()
                {
                    Id            = "quiz_data",
                    Name          = "Файл вопросов (JSON)",
                    AcceptedTypes = new List<AssetType> { AssetType.Text },
                    Required      = true,
                    Group         = "Основные",
                    Description   = "JSON-массив с вопросами и id видео-клипов"
                },
                Vid("video_1", "Видео-клип 1", required: true,  group: "Видео"),
                Vid("video_2", "Видео-клип 2", required: true,  group: "Видео"),
                Vid("video_3", "Видео-клип 3", required: false, group: "Видео"),
                Vid("video_4", "Видео-клип 4", required: false, group: "Видео"),
                Vid("video_5", "Видео-клип 5", required: false, group: "Видео"),
            },
            Schema = new PresetSchemaDto
            {
                Parameters = new List<ParameterSchemaDto>
                {
                    P("time_per_question", "Время на ответ (сек)",        "int",  60,   5,  180, "Геймплей"),
                    P("loop_video",        "Зациклить видео",             "bool", true, grp: "Геймплей"),
                    P("show_correct",      "Показывать правильный ответ", "bool", true, grp: "Геймплей"),
                    P("shuffle_questions", "Перемешивать вопросы",        "bool", true, grp: "Геймплей"),
                    P("show_timer",        "Показывать таймер",           "bool", true, grp: "Интерфейс"),
                }
            }
        };

        // ── 3D Осмотр ─────────────────────────────────────────────────────────
        private static PresetInfo BuildScene3D() => new()
        {
            Id = "scene_3d",
            Name = "3D Осмотр",
            Description = "Вращай 3D-объект, получай подсказки и угадай что это",
            Version = "1.0",
            Slots = new List<SlotDefinition>
            {
                new()
                {
                    Id            = "model_1",
                    Name          = "3D-модель 1",
                    AcceptedTypes = new List<AssetType> { AssetType.Model3D },
                    Required      = true,
                    Group         = "Модели",
                    Description   = "GLB/GLTF файл первого объекта"
                },
                new()
                {
                    Id            = "model_2",
                    Name          = "3D-модель 2",
                    AcceptedTypes = new List<AssetType> { AssetType.Model3D },
                    Required      = false,
                    Group         = "Модели",
                    Description   = "GLB/GLTF файл второго объекта (опционально)"
                },
                new()
                {
                    Id            = "model_3",
                    Name          = "3D-модель 3",
                    AcceptedTypes = new List<AssetType> { AssetType.Model3D },
                    Required      = false,
                    Group         = "Модели",
                    Description   = "GLB/GLTF файл третьего объекта (опционально)"
                },
            },
            Schema = new PresetSchemaDto
            {
                Parameters = new List<ParameterSchemaDto>
                {
                    P("rotation_speed", "Скорость вращения",    "float", 1.0, 0.1, 10.0, "Управление"),
                    P("auto_rotate",    "Автовращение",          "bool",  false, grp: "Управление"),
                    P("show_hints",     "Показывать подсказки",  "bool",  true,  grp: "Управление"),
                    P("hint_delay",     "Задержка подсказки (сек)","int", 5, 1, 30, "Управление"),
                    P("bg_color",       "Цвет фона",             "color", "#1E1E2E", grp: "Визуал"),
                }
            }
        };

        // ── Хелперы слотов ────────────────────────────────────────────────────
        private static SlotDefinition Img(string id, string name, bool required, string group) => new()
        {
            Id = id, Name = name,
            AcceptedTypes = new List<AssetType> { AssetType.Image },
            Required = required, Group = group
        };

        private static SlotDefinition Vid(string id, string name, bool required, string group) => new()
        {
            Id = id, Name = name,
            AcceptedTypes = new List<AssetType> { AssetType.Video },
            Required = required, Group = group
        };

        // ── Хелпер параметра ──────────────────────────────────────────────────
        /// <summary>
        /// Создаёт ParameterSchemaDto с числовым дефолтом.
        /// </summary>
        private static ParameterSchemaDto P(
            string id, string name, string type,
            double defaultVal, double? min = null, double? max = null,
            string grp = null) => new()
        {
            Id      = id,
            Name    = name,
            Type    = type,
            Default = JToken.FromObject(defaultVal),
            Min     = min,
            Max     = max,
            Group   = grp
        };

        /// <summary>
        /// Создаёт ParameterSchemaDto с булевым дефолтом.
        /// </summary>
        private static ParameterSchemaDto P(
            string id, string name, string type,
            bool defaultVal, string grp = null) => new()
        {
            Id      = id,
            Name    = name,
            Type    = type,
            Default = JToken.FromObject(defaultVal),
            Group   = grp
        };

        /// <summary>
        /// Создаёт ParameterSchemaDto со строковым дефолтом (color / string).
        /// </summary>
        private static ParameterSchemaDto P(
            string id, string name, string type,
            string defaultVal, string grp = null) => new()
        {
            Id      = id,
            Name    = name,
            Type    = type,
            Default = JToken.FromObject(defaultVal),
            Group   = grp
        };
    }
}
