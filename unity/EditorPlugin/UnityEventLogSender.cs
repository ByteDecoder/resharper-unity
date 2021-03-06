using System;
using System.Reflection;
using JetBrains.Platform.Unity.EditorPluginModel;
using JetBrains.Rider.Unity.Editor.NonUnity;
using UnityEditor;
using UnityEngine;

namespace JetBrains.Rider.Unity.Editor
{
  public class UnityEventCollector
  {
    public readonly BoundedSynchronizedQueue<RdLogEvent> DelayedLogEvents = new BoundedSynchronizedQueue<RdLogEvent>(1000);
    private bool myLogEventsCollectorEnabled;

    public UnityEventCollector()
    {
      if (!PluginSettings.LogEventsCollectorEnabled)
        return;

      EditorApplication.update += () =>
      {
          myLogEventsCollectorEnabled = PluginSettings.LogEventsCollectorEnabled; // can be called only from main thread
      };

      // Both of these methods were introduced in 5.0+ but EditorPlugin.csproj still targets 4.7
      var eventInfo = typeof(Application).GetEvent("logMessageReceivedThreaded", BindingFlags.Static | BindingFlags.Public);
      if (eventInfo == null)
        eventInfo = typeof(Application).GetEvent("logMessageReceived", BindingFlags.Static | BindingFlags.Public);

      if (eventInfo != null)
      {
        var handler = new Application.LogCallback(ApplicationOnLogMessageReceived);
        eventInfo.AddEventHandler(null, handler);
        AppDomain.CurrentDomain.DomainUnload += (EventHandler) ((_, __) =>
        {
          eventInfo.RemoveEventHandler(null, handler);
        });

        // Introduced in 3.4, obsolete but still working in 2017.2+
        // Should use EditorApplication.playModeStateChanged, introduced in 2017.2
#pragma warning disable 618
        EditorApplication.playmodeStateChanged += () =>
#pragma warning restore 618
        {
          // Work around an issue in Unity 2017.1+ that stops sending log messages to the handler when leaving play mode.
          // The issue will not be fixed because it might break compatibility of existing workarounds
          // https://issuetracker.unity3d.com/issues/general-unityengine-dot-application-dot-logmessagereceived-is-not-being-raised-after-exiting-play-mode
          // Note that although the issue says 2017.4+ it is actually 2017.1 and above. I haven't been able to test 5.x
          if (!EditorApplication.isPlayingOrWillChangePlaymode)
          {
            eventInfo.RemoveEventHandler(null, handler);
            eventInfo.AddEventHandler(null, handler);
          }
        };
      }
      else
      {
#pragma warning disable 612, 618
        // Obsolete from 5.0+
        Application.RegisterLogCallback(ApplicationOnLogMessageReceived);
#pragma warning restore 612, 618
      }
    }

    private void ApplicationOnLogMessageReceived(string message, string stackTrace, LogType type)
    {
      if (!myLogEventsCollectorEnabled) // stop collecting, if setting was disabled
        return;
      
      RdLogEventType eventType;
      switch (type)
      {
        case LogType.Error:
        case LogType.Exception:
          eventType = RdLogEventType.Error;
          break;
        case LogType.Warning:
          eventType = RdLogEventType.Warning;
          break;
        default:
          eventType = RdLogEventType.Message;
          break;
      }

      RdLogEventMode mode = RdLogEventMode.Play;
      if (PluginEntryPoint.PlayModeSavedState == PluginEntryPoint.PlayModeState.Stopped)
        mode = RdLogEventMode.Edit;

      var ticks = DateTime.UtcNow.Ticks;
      var evt = new RdLogEvent(ticks, eventType, mode, message, stackTrace);
      DelayedLogEvents.Enqueue(evt);
      OnAddEvent(new EventArgs());
    }

    public event EventHandler AddEvent;

    public void ClearEvent()
    {
      AddEvent = null;
    }

    private void OnAddEvent(EventArgs e)
    {
      AddEvent?.Invoke(this, e);
    }
  }

  public class UnityEventLogSender
  {
    public UnityEventLogSender(UnityEventCollector collector)
    {
      ProcessQueue(collector);

      collector.ClearEvent();
      collector.AddEvent += (col, _) =>
      {
        ProcessQueue((UnityEventCollector)col);
      };
    }

    private void ProcessQueue(UnityEventCollector collector)
    {
      RdLogEvent element;
      while ((element  = collector.DelayedLogEvents.Dequeue()) != null)
      {
        SendLogEvent(element);
      }
    }

    private void SendLogEvent(RdLogEvent logEvent)
    {
      MainThreadDispatcher.Instance.Queue(() =>
      {
        foreach (var modelWithLifetime in PluginEntryPoint.UnityModels)
        {
          if (modelWithLifetime.Lifetime.IsAlive)
          {
            modelWithLifetime.Model.Log(logEvent);
          }
        }
      });
    }
  }
}