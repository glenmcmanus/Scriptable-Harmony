using System;
using System.Collections.Generic;
using System.Linq;
using NuiN.NExtensions;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Pool;
using UnityEngine.SceneManagement;

namespace NuiN.ScriptableHarmony.Sound
{
    [CreateAssetMenu(menuName = "ScriptableHarmony/Sound/New Sound Player", fileName = "New Sound Player")]
    public class SoundPlayerSO : ScriptableObject
    {
        ObjectPool<AudioSource> _sourcePool;
        Transform _sourceContainer;

        AudioSource _sourcePrefab;
        AudioSource _activeSource;
        bool _sceneDisabledAudio;
    
        [Range(0,1)] public float masterVolume = 0.5f;
        
        [SerializeField] AudioMixerGroup mixerGroup;
        
        [Header("Options")]
        public bool disableAudio;
        [SerializeField] List<string> disableAudioOnScenes;

        public bool AudioDisabled => disableAudio || _sceneDisabledAudio;
        
        void OnEnable() => SceneManager.activeSceneChanged += SetupForNewScene;
        void OnDisable() => SceneManager.activeSceneChanged -= SetupForNewScene;

        void SetupForNewScene(Scene from, Scene to)
        {
            if (disableAudioOnScenes.Any(sceneName => sceneName != null && sceneName == to.name))
            {
                _sceneDisabledAudio = true;
                return;
            }

            _sourceContainer = new GameObject(name + " | ObjectPool").transform;
            _sourcePrefab = Resources.Load<AudioSource>("SH_AudioSourcePrefab");
            
            _sourcePool = new ObjectPool<AudioSource>(
                createFunc: () =>
                {
                    AudioSource source = Instantiate(_sourcePrefab, _sourceContainer);
                    source.name = "AudioSource_Pooled";
                    return source;
                },
                actionOnGet: s =>
                {
                    s.gameObject.SetActive(true);
                },
                actionOnRelease: s =>
                {
                    s.transform.SetParent(_sourceContainer);
                    s.gameObject.SetActive(false);
                });
            
            _sceneDisabledAudio = false;
        }

        // ReSharper disable Unity.PerformanceAnalysis 
        AudioSource InitializeNewSource(SoundSO sound, bool spatial, float volumeMult, float pitchMult)
        {
            SoundSettings settings = sound.Settings;
            
            AudioSource source = _sourcePool.Get();
            
            AudioClip clip = settings.Clip;
            if (clip == null) 
            {
                #if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning($"SoundSO {sound.name}: Attempted to play null clip", sound);
                #endif
                
                return source;
            }
            
            source.Stop();
            
            source.playOnAwake = false;
            source.clip = clip;
            source.outputAudioMixerGroup = mixerGroup;
            source.loop = settings.Loop;
            source.priority = settings.Priority;
            source.volume = settings.Volume * masterVolume * volumeMult;
            source.pitch = settings.Pitch * pitchMult;
            source.panStereo = settings.StereoPan;
            source.reverbZoneMix = settings.ReverbZoneMix;
            source.spatialBlend = spatial ? 1f : 0f;
            
            source.Play();

            float lifetime = source.clip.length / Mathf.Max(Math.Abs(source.pitch), Mathf.Epsilon);
            RuntimeHelper.DoAfter(lifetime, () => _sourcePool.Release(source));
            
            return source;
        }

        internal AudioSource Play(SoundSO sound, float volumeMult = 1f, float pitchMult = 1f)
        {
            return AudioDisabled ? new AudioSource() : InitializeNewSource(sound, false, volumeMult, pitchMult);
        }

        internal AudioSource PlaySpatial(SoundSO sound, Vector3 position, Transform parent = null, float volumeMult = 1f, float pitchMult = 1f)
        {
            if (AudioDisabled) return new AudioSource();

            AudioSource source = InitializeNewSource(sound, true, volumeMult, pitchMult);

            if (source.clip == null)
            {
                _sourcePool.Release(source);
                return source;
            }
            
            source.transform.position = position;
            
            if(parent != null) source.transform.SetParent(parent);

            return source;
        }
    }
}
