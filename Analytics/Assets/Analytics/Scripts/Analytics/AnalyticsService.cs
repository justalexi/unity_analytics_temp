using System;
using System.Collections.Generic;
using Analytics.Scripts.LocalStorage;
using Analytics.Scripts.Utils;
using MEC;
using UnityEngine;
using UnityEngine.Networking;

namespace Analytics.Scripts.Analytics
{
    /// <summary>
    /// Analytics service.
    /// Initiates itself the first time its 'Instance' is referenced
    /// </summary>
    public class AnalyticsService
    {
        #region Primitive singleton

        private static AnalyticsService _instance;

        public static AnalyticsService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new AnalyticsService();
                }

                return _instance;
            }
        }

        private AnalyticsService()
        {
        }

        #endregion


        #region MEC

        private CoroutineHandle _sendCoroutine;

        // jTODO can be removed if MEC PRO is available
        private bool _isSendCoroutineRunning;

        private bool IsSendCoroutineRunning
        {
            get
            {
                // Without MEC PRO
                return _isSendCoroutineRunning;
                // 'IsRunning' is a MEC PRO feature
                // return _sendCoroutine.IsRunning;
            }
        }

        #endregion

        private bool _isSendingInProgress;

        // Used to clear local cache of successfully sent events
        private long _lastSendingTimestamp;

        // Service params
        private string _serverURL;
        private float _cooldownBeforeSend;
        private FileLocalStorage _localStorage;

        private readonly AnalyticsEvents _analyticsEvents = new AnalyticsEvents();

        #region Events, again?

        public Action<float> OnCountdownStarted;
        public Action<string> OnResponse;

        #endregion


        public void Init(string serverURL, float cooldownBeforeSend, FileLocalStorage localStorage)
        {
            _serverURL = serverURL;
            _cooldownBeforeSend = cooldownBeforeSend;

            _localStorage = localStorage;

            Timing.KillCoroutines(_sendCoroutine);
            _isSendCoroutineRunning = false;
            _isSendingInProgress = false;

            _lastSendingTimestamp = 0;
        }

        public void ForceSendingEvents()
        {
            if (_isSendingInProgress)
                return;

            SendImmediately();
        }

        public void TrackEvent(string type, string data)
        {
            var timestamp = TimestampUtils.GetTimestamp(DateTime.Now);
            var analyticsEvent = AnalyticsEvent.GetAnalyticsEvent(timestamp, type, data);

            _localStorage.AddAnalyticsEvent(analyticsEvent);

            WaitBeforeSend();
        }

        private void SendImmediately()
        {
            if (_isSendingInProgress)
                return;

            // 'SendImmediately' can be called while coroutine is running, so need to kill it first
            Timing.KillCoroutines(_sendCoroutine);
            _isSendCoroutineRunning = false;

            var analyticsEvents = _localStorage.GetAnalyticsEvents(0, long.MaxValue);
            if (analyticsEvents == null || analyticsEvents.Count == 0)
                return;

            _isSendingInProgress = true;

            var now = TimestampUtils.GetTimestamp(DateTime.Now);
            _lastSendingTimestamp = now;

            _analyticsEvents.events = analyticsEvents;
            string analyticsEventsJson = JsonUtility.ToJson(_analyticsEvents);

            var request = UnityWebRequest.Post(_serverURL, analyticsEventsJson);
            var unityWebRequestAsyncOperation = request.SendWebRequest();
            unityWebRequestAsyncOperation.completed += operation =>
            {
                _isSendingInProgress = false;

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var responseText = request.downloadHandler.text;

                    OnResponse?.Invoke(responseText);

                    _localStorage.ClearAnalyticsEvents(0, _lastSendingTimestamp);

                    CheckForFreshEvents();
                }

                // jTODO maybe resend, if failed
            };
        }

        private void CheckForFreshEvents()
        {
            var analyticsEvents = _localStorage.GetAnalyticsEvents(_lastSendingTimestamp, long.MaxValue);
            if (analyticsEvents.Count > 0)
            {
                WaitBeforeSend();
            }
        }

        private void WaitBeforeSend()
        {
            if (!IsSendCoroutineRunning)
            {
                _sendCoroutine = Timing.RunCoroutine(SendDelayedCoroutine());
                _isSendCoroutineRunning = true;
            }
        }

        private IEnumerator<float> SendDelayedCoroutine()
        {
            OnCountdownStarted?.Invoke(_cooldownBeforeSend);

            yield return Timing.WaitForSeconds(_cooldownBeforeSend);

            SendImmediately();

            _isSendCoroutineRunning = false;
        }
    }
}