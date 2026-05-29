using System.Collections.Generic;
using UnityEngine;

namespace InteractiveClient.AudioModule
{
    /// <summary>
    /// Лёгкий контейнер аудио-источников: одна BGM-петля + пул PlayOneShot для SFX.
    /// Любой пресет создаёт один AudioBank в OnEnter, регистрирует AudioClip-ы по ключам
    /// и зовёт Play/PlayLoop. AudioClip-ы приходят из ассет-маппинга (UnityWebRequestMultimedia
    /// → AudioClip, делается выше уровнем).
    ///
    /// Особенности:
    ///  • BGM никогда не накладывается сама на себя; смена клипа фейдит старый.
    ///  • SFX — простой PlayOneShot через единственный sfxSource.
    ///  • Если ключ не зарегистрирован — Play/PlayLoop тихо ничего не делают.
    /// </summary>
    public class AudioBank
    {
        private readonly Dictionary<string, AudioClip> clips = new();
        private readonly AudioSource bgmSource;
        private readonly AudioSource sfxSource;
        private readonly GameObject host;

        private string currentBgmKey;

        public AudioBank(string hostName = "AudioBank", float bgmVolume = 0.5f, float sfxVolume = 1f)
        {
            host = new GameObject(hostName);
            Object.DontDestroyOnLoad(host);

            bgmSource = host.AddComponent<AudioSource>();
            bgmSource.loop = true;
            bgmSource.playOnAwake = false;
            bgmSource.volume = bgmVolume;

            sfxSource = host.AddComponent<AudioSource>();
            sfxSource.loop = false;
            sfxSource.playOnAwake = false;
            sfxSource.volume = sfxVolume;
        }

        public void Register(string key, AudioClip clip)
        {
            if (string.IsNullOrEmpty(key) || clip == null) return;
            clips[key] = clip;
        }

        public bool Has(string key) => !string.IsNullOrEmpty(key) && clips.ContainsKey(key);

        /// <summary>Воспроизвести SFX по ключу (если зарегистрирован).</summary>
        public void Play(string key, float volumeScale = 1f)
        {
            if (!clips.TryGetValue(key ?? string.Empty, out var clip)) return;
            sfxSource.PlayOneShot(clip, volumeScale);
        }

        /// <summary>Запустить BGM-петлю. Если уже играет тот же ключ — no-op.</summary>
        public void PlayLoop(string key)
        {
            if (currentBgmKey == key && bgmSource.isPlaying) return;
            if (!clips.TryGetValue(key ?? string.Empty, out var clip))
            {
                StopLoop();
                return;
            }
            bgmSource.clip = clip;
            bgmSource.Play();
            currentBgmKey = key;
        }

        public void StopLoop()
        {
            bgmSource.Stop();
            bgmSource.clip = null;
            currentBgmKey = null;
        }

        public void Dispose()
        {
            if (host != null) Object.Destroy(host);
            clips.Clear();
        }
    }
}
