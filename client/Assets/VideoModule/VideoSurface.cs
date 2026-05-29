using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace InteractiveClient.VideoModule
{
    /// <summary>
    /// Воспроизводит видео в RenderTexture и привязывает её как backgroundImage
    /// к указанному VisualElement. Используется как:
    ///  • анимированный фон (Spot Hunt scene_background, если задано видео);
    ///  • интро / результат / реворд-клип (Memory, Quiz).
    ///
    /// Видео грузится по URL (HTTP) — UnityEngine.Video.VideoPlayer умеет стримить
    /// MP4/WebM напрямую.
    ///
    /// Использование:
    ///   var surface = new VideoSurface(targetElement);
    ///   surface.Load(url, loop: true);
    ///   surface.Play();
    ///   ...
    ///   surface.Dispose();
    /// </summary>
    public class VideoSurface : IDisposable
    {
        private readonly VisualElement target;
        private readonly GameObject host;
        private readonly VideoPlayer player;
        private RenderTexture rt;

        public event Action OnLoopPointReached;          // событие конца ролика (если loop=false)

        public bool IsPlaying => player != null && player.isPlaying;
        public bool IsPrepared => player != null && player.isPrepared;

        public VideoSurface(VisualElement target, int width = 1280, int height = 720)
        {
            this.target = target;
            host = new GameObject("VideoSurface");
            UnityEngine.Object.DontDestroyOnLoad(host);

            player = host.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.audioOutputMode = VideoAudioOutputMode.Direct;
            player.source = VideoSource.Url;
            player.skipOnDrop = true;
            player.loopPointReached += _ => OnLoopPointReached?.Invoke();

            rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) { name = "VideoSurface_RT" };
            rt.Create();
            player.targetTexture = rt;

            if (target != null)
                target.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
        }

        /// <summary>
        /// Загрузить видео по URL. Не воспроизводит автоматически — позови Play() после.
        /// </summary>
        public void Load(string url, bool loop = false)
        {
            if (player == null || string.IsNullOrEmpty(url)) return;
            player.url = url;
            player.isLooping = loop;
            player.Prepare();
        }

        public void Play()
        {
            if (player != null) player.Play();
        }

        public void Pause()
        {
            if (player != null) player.Pause();
        }

        public void Stop()
        {
            if (player != null) player.Stop();
        }

        public void Dispose()
        {
            if (player != null) player.Stop();
            if (target != null) target.style.backgroundImage = StyleKeyword.Null;
            if (rt != null)
            {
                rt.Release();
                UnityEngine.Object.Destroy(rt);
                rt = null;
            }
            if (host != null) UnityEngine.Object.Destroy(host);
        }
    }
}
