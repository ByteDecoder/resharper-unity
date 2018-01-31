using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using JetBrains.DataFlow;
using JetBrains.Platform.RdFramework;
using JetBrains.Platform.RdFramework.Util;
using JetBrains.Platform.Unity.Model;
using JetBrains.Rider.Unity.Editor.NonUnity;
using JetBrains.Util.Logging;
using UnityEditor;
using UnityEngine;

namespace JetBrains.Rider.Unity.Editor
{
  public class UnityApplication
  {
    private static readonly ILog Logger = Log.GetLog("UnityApplication");
    
    public static string GetExternalScriptEditor()
    {
      return EditorPrefs.GetString("kScriptsDefaultApp");
    }

    public static void SetExternalScriptEditor(string path)
    {
      EditorPrefs.SetString("kScriptsDefaultApp", path);
    }

    public void UnityLogRegisterCallBack()
    {
      var eventInfo = typeof(Application).GetEvent("logMessageReceived", BindingFlags.Static | BindingFlags.Public);
      LifetimeDefinition domainLifetime = Lifetimes.Define();
      
      if (eventInfo != null)
      {
        eventInfo.AddEventHandler(null, new Application.LogCallback(ApplicationOnLogMessageReceived));
        AppDomain.CurrentDomain.DomainUnload += (EventHandler) ((_, __) =>
        {
          eventInfo.RemoveEventHandler(null, new Application.LogCallback(ApplicationOnLogMessageReceived));
          domainLifetime.Terminate();
        });
      }
      else
      {
#pragma warning disable 612, 618
        Application.RegisterLogCallback(ApplicationOnLogMessageReceived);
#pragma warning restore 612, 618
      }
      
      RiderPlugin.Model.AdviseNotNull(domainLifetime.Lifetime, model =>
      {
        myDelayedLogEvents.ForEach(evt => SentLogEvent(model, evt));
        myDelayedLogEvents.Clear();
      });
    }
    
    List<RdLogEvent> myDelayedLogEvents = new List<RdLogEvent>();
    

    private void ApplicationOnLogMessageReceived(string message, string stackTrace, LogType type)
    {
      if (PluginSettings.SendConsoleToRider)
      {
        // use Protocol to pass log entries to Rider
        MainThreadDispatcher.Instance.InvokeOrQueue(() =>
        {
          RdLogEvent evt;
          switch (type)
          {
            case LogType.Error:
            case LogType.Exception:
              evt = new RdLogEvent(RdLogEventType.Error, EditorApplication.isPlaying?RdLogEventMode.Play:RdLogEventMode.Edit, message, stackTrace);
              break;
            case LogType.Warning:
              evt = new RdLogEvent(RdLogEventType.Warning, EditorApplication.isPlaying?RdLogEventMode.Play:RdLogEventMode.Edit, message, stackTrace);
              break;
            default:
              evt = new RdLogEvent(RdLogEventType.Message, EditorApplication.isPlaying?RdLogEventMode.Play:RdLogEventMode.Edit, message, stackTrace);
              break;
          }
          
          var model = RiderPlugin.Model.Maybe.ValueOrDefault;
          if (model == null)
          {
            myDelayedLogEvents.Add(evt);
          }
          else
          {
            SentLogEvent(model, evt);
          }
        });
      }
    }
    
    private void SentLogEvent(UnityModel model, RdLogEvent logEvent)
    {
      //if (!message.StartsWith("[Rider][TRACE]")) // avoid sending because in Trace mode log about sending log event to Rider, will also appear in unity log
      model.LogModelInitialized.Value.Log.Fire(logEvent);
    }

    /// <summary>
    /// Force Unity To Write Project File
    /// </summary>
    public static void SyncSolution()
    {
      var T = Type.GetType("UnityEditor.SyncVS,UnityEditor");
      var syncSolution = T.GetMethod("SyncSolution", BindingFlags.Public | BindingFlags.Static);
      syncSolution.Invoke(null, null);
    }

    internal static Version UnityVersion
    {
      get
      {
        var ver = Application.unityVersion.Split(".".ToCharArray()).Take(2).Aggregate((a, b) => a + "." + b);
        Logger.Verbose("Unity version: " + ver);
        return new Version(ver);
      }
    }
    
    public static void AddRiderToRecentlyUsedScriptApp(string userAppPath, string recentAppsKey)
    {
      for (int index = 0; index < 10; ++index)
      {
        string path = EditorPrefs.GetString(recentAppsKey + (object) index);
        if (File.Exists(path) && Path.GetFileName(path).ToLower().Contains("rider"))
          return;
      }
      EditorPrefs.SetString(recentAppsKey + 9, userAppPath);
    }
  }
}