// Copyright (c) 2018 RocketChicken Interactive Inc.

//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.18052
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------
using Motive.Core.Media;
using Motive.Unity.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Logger = Motive.Core.Diagnostics.Logger;

namespace Motive.Unity.Media
{
    /// <summary>
    /// Unity implementation of IAudioPlayerChannel.
    /// </summary>
	public class UnityAudioPlayerChannel : MonoBehaviour, IAudioPlayerChannel
	{
		public bool UseResources { get; set; }
		//public AudioSource AudioSource;

		//private bool m_isPlaying;

		private Logger m_logger;		
		private HashSet<UnityAudioPlayer> m_playingPlayers;
		private List<Action> m_actions;

		UnityAudioPlayerChannelImpl m_channelImpl;
        
        protected virtual void Awake()
        {
			m_playingPlayers = new HashSet<UnityAudioPlayer>();
			m_actions = new List<Action>();
			m_logger = new Logger(this);
			m_channelImpl = new UnityAudioPlayerChannelImpl(this);
        }

		internal void Call(Action action)
		{
			try {
			//if (Thread.CurrentThread.ManagedThreadId == m_unityThreadId && !
			//    Thread.CurrentThread.IsBackground) {
			if (ThreadHelper.Instance.IsUnityThread) {
				// Make sure to keep actions in order
				CallActions();

				action();
			} else {
				lock (m_actions) {
					m_actions.Add(action);
				}
			}
			} catch (Exception x)
			{
				m_logger.Exception(x);
			}
		}

		void AddPlayingPlayer(UnityAudioPlayer player)
		{
			lock (m_playingPlayers) {
				m_playingPlayers.Add(player);
			}
		}

		void RemovePlayingPlayer(UnityAudioPlayer player)
		{
			lock (m_playingPlayers) {
				m_playingPlayers.Remove(player);
			}
		}

		public void PreparePlayer(Uri source)
		{
			m_channelImpl.PreparePlayer(source);
		}

		public void PreparePlayers(Uri source, int count)
		{
			m_channelImpl.PreparePlayers(source, count);
		}

		public void ReleasePlayers ()
		{
			m_channelImpl.ReleasePlayers();
		}

		class UnityAudioPlayerChannelImpl : AudioPlayerChannelBase<UnityAudioPlayer>
		{
			UnityAudioPlayerChannel m_wrapper;

			public UnityAudioPlayerChannelImpl(UnityAudioPlayerChannel wrapper)
			{
				m_wrapper = wrapper;
			}

			public override IAudioPlayer CreatePlayer (Uri source)
			{				
				var player = new UnityAudioPlayer(m_wrapper)
				{
					Source = source
				};
				
				m_wrapper.Call(() => {
					if (m_wrapper.UseResources) {
						player.LoadResource();
					} else {
						m_wrapper.StartCoroutine(player.LoadCoroutine());
					}
				});
				
				return player;
			}

			protected override void Dispose (UnityAudioPlayer player)
			{
				player.Dispose();
			}
		}

		public class UnityAudioPlayer : IAudioPlayer
		{
			static int g_inst;
			public int Inst;

			public Action<bool> OnComplete;
			public AudioClip AudioClip;
			public WWW Loader;
			public AudioSource AudioSource;

			public Uri Source {
				get; internal set;
			}

			public bool DidStartPlaying;
			public bool Paused;
			public bool IsComplete
			{
				get {
					// is playing if:
					// - did start but not ready
					// - did start and ready and playing
					if (!AudioClip || !AudioSource) {
						return true;
					}

					return !Paused && DidStartPlaying && (AudioClip.loadState == AudioDataLoadState.Loaded) && !AudioSource.isPlaying;
				}
			}

			public TimeSpan Duration
			{
				get
				{
                    if (AudioClip)
                    {
                        return TimeSpan.FromSeconds(AudioClip.length);
                    }

                    return TimeSpan.FromSeconds(0);
				}
			}

			Logger m_logger;
			UnityAudioPlayerChannel m_channel;

			public UnityAudioPlayer(UnityAudioPlayerChannel channel)
			{
				m_logger = new Logger(this);
				m_channel = channel;

				Pitch = 1f;
				Volume = 1f;
				Loop = false;

				Inst = g_inst++;
			}

			public void OnStop(Action<bool> onComplete)
			{
				m_logger.Debug("OnStop ({0}) {1}", Inst, Source);

				m_channel.RemovePlayingPlayer(this);

				m_sampleTime = null;
				
				IsPlaying = false;
				
				m_samplePosition = 0;

				DidStartPlaying = false;

				if (Loader != null) {
					WWW www = Loader;
					Loader = null;
					www.Dispose();
				}

				if (AudioSource) {
					AudioSource.Stop();
				}

				if (onComplete != null)
				{
					onComplete(true);
				}

				if (m_audioEnded != null) {
					m_audioEnded(this, EventArgs.Empty);
				}
			}

			public void OnStop()
			{
				var onComplete = OnComplete;
				OnComplete = null;

				OnStop(onComplete);
			}

			const string g_streamingAssets = "StreamingAssets/";

			internal void LoadResource()
			{
				int idx = Source.AbsoluteUri.IndexOf(g_streamingAssets);
				var resourceName = Source.AbsoluteUri.Substring(idx + g_streamingAssets.Length);
				var ext = System.IO.Path.GetExtension(resourceName);

				resourceName = resourceName.Substring(0, resourceName.Length - ext.Length);

				AudioClip = Resources.Load<AudioClip>(resourceName);

				if (AudioClip) {
					m_logger.Debug("Loaded resource audio clip {0}", resourceName);

                    SetAudioClip(AudioClip);
				}
			}

            protected virtual void SetAudioClip(AudioClip audioClip)
            {
                // Should get caught in coroutine, but in case
                // we get swapped out, do one more check so we don't 
                // hang on to an unneeded object
                lock (this)
                {
                    if (!m_isDisposed)
                    {
                        var go = new GameObject("AudioPlayer");
                        AudioSource = go.AddComponent<AudioSource>();
                        AudioSource.playOnAwake = false;
                        AudioSource.clip = AudioClip;
                    }
                }
            }

			internal IEnumerator LoadCoroutine()
			{				
				Loader = new WWW(Source.AbsoluteUri);
				
				yield return Loader;

                lock (this)
                {
                    if (m_isDisposed)
                    {
                        yield break;
                    }
                }
				
				if (Loader != null) {
					// Streaming should be opt-in, driven by some
					// setting somewhere...
					AudioClip = Loader.GetAudioClip(false, true);
					//AudioClip = Loader.GetAudioClip(false, false);

					if (AudioClip) {
						m_logger.Debug("Loaded audio clip {0}", Source);
						
						if (!string.IsNullOrEmpty(Loader.error)) {
							OnStop();
							yield break;
						}

                        SetAudioClip(AudioClip);
					} else {
						m_logger.Error("Could not load audio clip {0}", Source);
						OnStop();
					}
				} else {
					OnStop();
				}
			}

			internal virtual void OnPlay()
			{
				m_logger.Debug("OnPlay ({0}) {1} pos={2}", Inst, Source, Position);

				if (AudioSource.isPlaying) {
					m_logger.Debug("OnPlay: already playing {0} pos={1}",
					               Source, Position);
					return;
				}

                if (m_setPosition.HasValue)
                {
                    AudioSource.time = m_setPosition.Value;
                }

				UpdateTime();
				AudioSource.volume = m_volume;
				AudioSource.pitch = Pitch;
				AudioSource.loop = Loop;
				AudioSource.Play();

				m_logger.Verbose("OnPlay: position now {0}", Position);
				
				DidStartPlaying = true;

				m_channel.AddPlayingPlayer(this);
			}

			EventHandler m_audioEnded;
			public event EventHandler AudioEnded
			{
				add { m_audioEnded += value; }
				remove { m_audioEnded -= value; }
			}

			EventHandler m_audioFailed;
			public event EventHandler AudioFailed
			{
				add { m_audioFailed += value; }
				remove { m_audioFailed -= value; }
			}

			public bool IsPlaying {
				get; private set;
			}

			float m_samplePosition;
			DateTime? m_sampleTime;
            float? m_setPosition;

			public TimeSpan Position {
				get {
					if (m_sampleTime.HasValue && IsPlaying) {
						TimeSpan dt = DateTime.Now - m_sampleTime.Value;
						return dt.Add(TimeSpan.FromSeconds(m_samplePosition));
					} else {
						return TimeSpan.FromSeconds(m_samplePosition);
					}
				}
				set {
                    m_setPosition = (float)value.TotalSeconds;

					m_channel.Call(() => {
                        if (AudioSource)
                        {
                            AudioSource.time = (float)value.TotalSeconds;
                        }
					});
				}
			}

			internal void UpdateTime()
			{
				m_sampleTime = DateTime.Now;
				m_samplePosition = AudioSource.time;
			}

			public void Play()
			{
				Play(OnComplete);
			}

			IEnumerator WaitPlay()
			{
				while (!(AudioSource || (Loader != null && !string.IsNullOrEmpty(Loader.error)))) {
                    if (m_isDisposed)
                    {
                        yield break;
                    }

					yield return null;
				}

				if (AudioSource) {
					OnPlay();
				} else {
					OnStop();
				}
			}

			public void Play(Action<bool> onComplete)
			{				
				IsPlaying = true;

				Paused = false;

				m_logger.Debug("Play ({0}) {1}", Inst, Source);

				OnComplete = onComplete;

				//m_channel.Play(this);
				m_channel.Call(() => {
					m_channel.StartCoroutine(WaitPlay());
				});
			}

			public void Stop()
			{
				Paused = false;

				m_samplePosition = 0;

				m_logger.Debug("Stop ({0}) {1}", Inst, Source);
				
				if (OnComplete != null)
				{
					var action = OnComplete;
					OnComplete = null;
					action(true);
				}

				m_channel.Call (() => {
					OnStop(null);
				});
			}

			public void Pause ()
			{
				Paused = true;

				m_logger.Debug("Pause ({0}) {1}", Inst, Source);
				
				IsPlaying = false;

				m_channel.Call (() => {
					if (AudioSource) {
						AudioSource.Pause();
						m_logger.Debug("OnPause ({2}) isPlaying={1} {0}", Source, AudioSource.isPlaying, Inst);
					}
				});
			}

			public void Dispose ()
			{
                lock (this)
                {
                    m_isDisposed = true;
                }

				m_logger.Debug("Dispose ({0}) {1}", Inst, Source);

				m_channel.RemovePlayingPlayer(this);

				m_channel.Call (() => {
                    if (AudioClip)
                    {
                        DestroyImmediate(AudioClip);
                    }

					if (AudioSource) {
						Destroy(AudioSource.gameObject);
						AudioSource = null;
					}
				});
			}

            float[] m_sampleBuf;

            public void GetOutputSamples(int channel, double[] samples)
            {
                if (m_sampleBuf == null || m_sampleBuf.Length != samples.Length)
                {
                    m_sampleBuf = new float[samples.Length];
                }

                if (AudioSource)
                {
                    AudioSource.GetOutputData(m_sampleBuf, channel);

                    for (int i = 0; i < m_sampleBuf.Length; i++)
                    {
                        samples[i] = m_sampleBuf[i];
                    }
                }
            }

            float m_volume = 1f;
            private bool m_isDisposed;

			public float Volume {
				get {
					return m_volume;
				}
				set {
					m_volume = value;
					m_channel.Call(() => {
						if (AudioSource) {
							AudioSource.volume = value;
						}
					});
				}
			}

			public float Pitch { get; set; }

			public bool Loop { get; set; }
		}

		public float Volume {
			get {
				return m_channelImpl.Volume;
			}
			set {
				m_channelImpl.Volume = value;
			}
		}

		public IAudioPlayer CreatePlayer(Uri source)
		{
			var player = new UnityAudioPlayer(this)
			{
				Source = source
			};

            ConfigurePlayer(player);

			return player;
		}

        internal void ConfigurePlayer(UnityAudioPlayer player)
        {
            Call(() =>
            {
                if (UseResources)
                {
                    player.LoadResource();
                }
                else
                {
                    StartCoroutine(player.LoadCoroutine());
                }
            });
        }

		public void Play(Uri source, float volume, float pitch, Action<bool> onComplete)
		{
			m_channelImpl.Play(source, volume, pitch, onComplete);
		}

		public void Play (Uri source, float volume, Action<bool> onComplete)
		{
			Play (source, volume, 1f, onComplete);
		}

		public void Play (Uri source, float volume, float pitch)
		{
			Play (source, volume, pitch, null);
		}

		public void Play (Uri source, float volume)
		{
			Play(source, volume, null);
		}

		public void Play (Uri source, Action<bool> onComplete)
		{
			Play(source, 1f, onComplete);
		}

		public void Play (Uri source)
		{
			Play (source, null);
		}

		public void Stop ()
		{
			Call(() => {
				m_channelImpl.Stop();
			});
		}

		public void Pause() 
		{
			// Use Call to make sure we clear any uncalled actions
			Call(() => {
				m_channelImpl.Pause();
			});
		}

		public void Resume()
		{
			m_channelImpl.Resume();
		}

		void OnApplicationPause(bool paused)
		{
			m_logger.Debug("OnApplicationPause({0})", paused);

			if (paused) {
				CallActions();
			} else {
				lock (m_playingPlayers) {
					foreach (UnityAudioPlayer p in m_playingPlayers) {
						bool srcPlaying = (p.AudioSource)?p.AudioSource.isPlaying:false;

						m_logger.Debug("Resume: ({0}) src.playing={1} p.playing={2} {3}",
						               p.Inst, srcPlaying,
						               p.IsPlaying, p.Source);

						// This is a hack...
						if (!p.IsPlaying && srcPlaying) {
							p.AudioSource.Pause();
						}
					}
				}
			}
		}

		void CallActions()
		{
			Action[] actions = null;
			
			lock (m_actions) {
				if (m_actions.Count > 0) {
					actions = m_actions.ToArray();
					m_actions.Clear();
				}
			}
			
			if (actions != null) {
				foreach (var action in actions) {
					action();
				}
			}
		}

		void Update()
		{
			CallActions();

			List<UnityAudioPlayer> toStop = null;

			lock (m_playingPlayers) {
				foreach (var playState in m_playingPlayers) {
					if (playState.IsComplete) {
						if (toStop == null) {
							toStop = new List<UnityAudioPlayer>();
						}

						if (playState.IsPlaying) {
							toStop.Add(playState);
						}
					} else {
						playState.UpdateTime();
					}
				}
			}

			if (toStop != null) {
				foreach (var playState in toStop) {
					playState.OnStop();
				}
			}
		}
	}
}

