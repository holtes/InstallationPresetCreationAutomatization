namespace InteractiveClient.Core
{
    /// <summary>Идентификаторы всех экранов приложения (для SceneRouter).</summary>
    public enum ScreenId
    {
        Auth,
        Register,
        ProjectList,
        Editor,
        Settings,
        Preview
    }

    /// <summary>Идентификаторы шагов wizard'а редактора.</summary>
    public enum EditorStep
    {
        Assets = 1,
        Preset = 2,
        Mapping = 3,
        Parameters = 4,
        Preview = 5,
        Build = 6
    }
}
