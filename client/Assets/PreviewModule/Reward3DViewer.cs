using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace InteractiveClient.PreviewModule
{
    /// <summary>
    /// Окошко с 3D-моделью-наградой. Грузит glTF/GLB по URL во вспомогательную сцену,
    /// рендерит её отдельной камерой в RenderTexture; саму текстуру ставит наружу
    /// (через свойство OutputTexture) — её можно привязать как backgroundImage
    /// к UI Toolkit VisualElement.
    ///
    /// Реальная загрузка glTF включается, когда в проект подтянут пакет glTFast
    /// (com.atteneder.gltfast). До этого момента LoadAsync логирует предупреждение
    /// и возвращает false. Это нужно, чтобы Слой 5.0 компилировался и без пакета,
    /// а тестировать рендер можно было любой моделью, переданной как готовый GameObject
    /// (LoadFromGameObject).
    /// </summary>
    public class Reward3DViewer : IDisposable
    {
        private readonly GameObject root;
        private readonly Camera renderCamera;
        private readonly Light fillLight;
        private GameObject loadedModel;
        private RenderTexture rt;
        private float rotationSpeedDegPerSec = 30f;

        public RenderTexture OutputTexture => rt;
        public bool HasModel => loadedModel != null;

        public Reward3DViewer(int textureSize = 512, float rotationSpeedDegPerSec = 30f)
        {
            this.rotationSpeedDegPerSec = rotationSpeedDegPerSec;

            // Корневой контейнер далеко от мира (не пересекается с пользовательской сценой).
            root = new GameObject("Reward3DViewer");
            root.transform.position = new Vector3(0, -1000, 0);
            UnityEngine.Object.DontDestroyOnLoad(root);

            var cam = new GameObject("Camera");
            cam.transform.SetParent(root.transform, false);
            cam.transform.localPosition = new Vector3(0, 0.5f, -2f);
            cam.transform.LookAt(root.transform);
            renderCamera = cam.AddComponent<Camera>();
            renderCamera.clearFlags = CameraClearFlags.SolidColor;
            renderCamera.backgroundColor = new Color(0.06f, 0.06f, 0.08f, 0f);
            renderCamera.fieldOfView = 35f;
            renderCamera.cullingMask = 1 << 0; // Default
            renderCamera.enabled = false;       // вручную рендерим в Update

            var light = new GameObject("FillLight");
            light.transform.SetParent(root.transform, false);
            light.transform.localRotation = Quaternion.Euler(35f, -30f, 0f);
            fillLight = light.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.intensity = 1.2f;

            rt = new RenderTexture(textureSize, textureSize, 16, RenderTextureFormat.ARGB32) { name = "Reward3D_RT" };
            rt.Create();
            renderCamera.targetTexture = rt;

            // Тикалка для авто-вращения и ручного Render().
            var ticker = root.AddComponent<Ticker>();
            ticker.Init(this);
        }

        /// <summary>Использовать готовый GameObject (например, префаб) как модель.</summary>
        public void LoadFromGameObject(GameObject prefabOrInstance)
        {
            ClearModel();
            if (prefabOrInstance == null) return;
            loadedModel = UnityEngine.Object.Instantiate(prefabOrInstance, root.transform);
            loadedModel.transform.localPosition = Vector3.zero;
            FrameObject(loadedModel);
        }

        /// <summary>
        /// Загрузить glTF/GLB по URL. Заглушка до интеграции glTFast.
        /// Возвращает false, пока пакет не подключён.
        /// </summary>
        public Task<bool> LoadAsync(string url, CancellationToken ct = default)
        {
            Debug.LogWarning("[Reward3DViewer] glTF runtime loading not enabled. " +
                             "Add `com.atteneder.gltfast` package to manifest.json and uncomment the implementation.");
            return Task.FromResult(false);
        }

        private void ClearModel()
        {
            if (loadedModel != null)
            {
                UnityEngine.Object.Destroy(loadedModel);
                loadedModel = null;
            }
        }

        private void FrameObject(GameObject go)
        {
            // Простое автомасштабирование: вписываем bbox в куб 1×1×1 относительно камеры.
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            float maxExtent = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));
            if (maxExtent <= 0f) return;
            float scale = 0.5f / maxExtent;
            go.transform.localScale = Vector3.one * scale;
            go.transform.localPosition = -bounds.center * scale;
        }

        public void Dispose()
        {
            ClearModel();
            if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); rt = null; }
            if (root != null) UnityEngine.Object.Destroy(root);
        }

        // Внутренний MonoBehaviour для крутилки и принудительного Render().
        private class Ticker : MonoBehaviour
        {
            private Reward3DViewer owner;
            public void Init(Reward3DViewer o) { owner = o; }

            private void Update()
            {
                if (owner == null) return;
                if (owner.loadedModel != null)
                    owner.loadedModel.transform.Rotate(Vector3.up, owner.rotationSpeedDegPerSec * Time.deltaTime, Space.World);
                if (owner.renderCamera != null)
                    owner.renderCamera.Render();
            }
        }
    }
}
